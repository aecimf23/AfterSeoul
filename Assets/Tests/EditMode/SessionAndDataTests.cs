using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// P1-F 검증 — 세이브 왕복, 창고 용량, 판매, 직접 노동, 세션 부팅.
    /// <see cref="FakeRegistry"/> 를 쓴다. 실제 JSON 은 <see cref="DataFileTests"/> 가 본다.
    /// </summary>
    [TestFixture]
    public class SessionTests
    {
        // KST 2026-09-11 10:00
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        // ── 세이브 ──────────────────────────────────────────────

        [Test]
        public void Save_RoundTrip_IsLossless()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var codec = new NewtonsoftJsonCodec();
            var saves = new SaveService(files, codec, clock);

            var save = saves.CreateNew();
            save.Player.Money = 1_840_000;
            save.Player.EmployerNpcId = "HWANG";
            save.TakeSeed();
            save.ClockAnomalyCount = 2;

            var scav = new ScavState
            {
                Uid = "sc_0001", Name = "김철수", Level = 7, Search = 8, Combat = 5, Survival = 9,
                Status = ScavStatus.OnExpedition, HiredAt = T0, ExpeditionCount = 23, TotalLootValue = 1_240_000,
            };
            scav.TraitIds.Add("TR_COWARD");
            scav.Equipment["weapon"] = "WPN04";
            scav.Equipment["armor"] = null;
            save.Scavs.Add(scav);

            var exp = new ExpeditionState
            {
                Uid = "ex_0042", MapId = "GURO_FACTORY", DepartedAt = T0, ReturnsAt = T0.AddMinutes(35),
                CostPaid = 85000, Seed = 0xDEADBEEF,
            };
            exp.ScavUids.Add("sc_0001");
            save.Expeditions.Add(exp);

            save.Factory.Queue.Add(new CraftJob
            {
                RecipeId = "RCP_BOLT", StartedAt = T0, CompletesAt = T0.AddSeconds(8), Seed = 42,
            });
            Warehouse.TryAdd(save.Warehouse, _data, "JUNK03", 12);
            save.Quests.ActiveGameDate = "2026-09-11";
            save.Quests.Active.Add(new ActiveQuest { QuestId = "DQ_HWANG_BOLT_01" });
            save.Quests.CompletedIds.Add("DQ_HWANG_MED_01");
            save.NpcTrust["HWANG"] = 34;
            save.Mail.OutboxTxIds.Add("m2p_20260911_a7f3e9c1");

            saves.Save(save);
            string written = files.ReadAllText(SaveService.FileName);
            var loaded = saves.LoadOrCreate();

            Assert.IsNull(saves.LastLoadError);
            Assert.AreEqual(written, codec.Serialize(loaded),
                "직렬화 → 역직렬화 → 직렬화 결과가 같아야 한다 (필드 유실·타임존 변형 없음)");
            Assert.AreEqual(ScavStatus.OnExpedition, loaded.Scavs[0].Status);
            Assert.AreEqual(0xDEADBEEFu, loaded.Expeditions[0].Seed, "시드가 바뀌면 결과가 바뀐다");
            Assert.AreEqual(T0.AddMinutes(35), loaded.Expeditions[0].ReturnsAt);
            Assert.AreEqual(TimeSpan.Zero, loaded.SavedAt.Offset, "시각은 UTC 로 남아야 한다");
            Assert.AreEqual(12, Warehouse.CountOf(loaded.Warehouse, "JUNK03"));
            Assert.AreEqual(34, loaded.NpcTrust["HWANG"]);
            Assert.AreEqual(save.RngCounter, loaded.RngCounter);
            StringAssert.Contains("\"OnExpedition\"", written, "enum 은 문자열로 저장한다");
        }

        [Test]
        public void Save_Corrupt_IsMovedAsideAndGameStartsFresh()
        {
            var files = new MemoryFileStore();
            files.WriteAllText(SaveService.FileName, "{ 이건 json 이 아니다");
            var saves = new SaveService(files, new NewtonsoftJsonCodec(), new TestClock(T0));

            var save = saves.LoadOrCreate();

            Assert.IsNotNull(save);
            Assert.IsNotNull(saves.LastLoadError);
            Assert.IsTrue(files.Exists(SaveService.FileName + ".corrupt"), "깨진 세이브는 지우지 않고 보관한다");
        }

        // ── 창고 / 판매 ─────────────────────────────────────────

        [Test]
        public void Warehouse_RejectsNewStackWhenFull()
        {
            var w = new WarehouseState { Capacity = 1 };

            Assert.AreEqual(0, Warehouse.TryAdd(w, _data, "JUNK03", 3));
            Assert.AreEqual(1, Warehouse.TryAdd(w, _data, "MED16", 1), "새 스택 자리가 없으면 전량 반환");
            Assert.AreEqual(1, w.Stacks.Count);

            Assert.AreEqual(0, Warehouse.TryAdd(w, _data, "JUNK03", 7), "기존 스택 빈자리는 채운다 (maxStack 10)");
            Assert.AreEqual(1, Warehouse.TryAdd(w, _data, "JUNK03", 1), "스택이 꽉 차면 넘친다");
            Assert.AreEqual(10, Warehouse.CountOf(w, "JUNK03"));
        }

        [Test]
        public void Market_Sell_IsAllOrNothing()
        {
            var save = new GameSave();
            Warehouse.TryAdd(save.Warehouse, _data, "JUNK03", 3);

            Assert.IsFalse(Market.TrySell(save, _data, "JUNK03", 5));
            Assert.AreEqual(3, Warehouse.CountOf(save.Warehouse, "JUNK03"), "실패하면 차감 없음");
            Assert.AreEqual(0, save.Player.Money);

            Assert.IsTrue(Market.TrySell(save, _data, "JUNK03", 2));
            Assert.AreEqual(1, Warehouse.CountOf(save.Warehouse, "JUNK03"));
            Assert.AreEqual(30000, save.Player.Money, "basePrice 15,000 × 2");

            Assert.IsFalse(Market.TrySell(save, _data, "NOT_AN_ITEM", 1));
            Assert.IsFalse(Market.TrySell(save, _data, "JUNK03", 0));
        }

        // ── 직접 노동 ───────────────────────────────────────────

        [Test]
        public void ManualWork_ScoreGradesQualityAndPays()
        {
            Assert.AreEqual(CraftQuality.Failed, FactorySystem.GradeManualWork(_data, 0.1));
            Assert.AreEqual(CraftQuality.Normal, FactorySystem.GradeManualWork(_data, 0.3), "경계값은 위 등급");
            Assert.AreEqual(CraftQuality.Good, FactorySystem.GradeManualWork(_data, 0.7));
            Assert.AreEqual(CraftQuality.Excellent, FactorySystem.GradeManualWork(_data, 0.85));

            var save = new GameSave();
            var factory = new FactorySystem();
            Assert.AreEqual(0, factory.CompleteManualWork(save, _data, CraftQuality.Failed));
            Assert.AreEqual(11000, factory.CompleteManualWork(save, _data, CraftQuality.Good));
            Assert.AreEqual(11000, save.Player.Money);
        }

        // ── 세션 ────────────────────────────────────────────────

        [Test]
        public void Session_NewGame_AssignsEmployerAndIssuesQuests_ThenReloadsSame()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();

            var first = NewSession(files, clock);
            first.Boot();

            Assert.AreEqual("HWANG", first.Save.Player.EmployerNpcId);
            Assert.AreEqual(3, first.Save.Quests.Active.Count, "첫 부팅에 오늘의 의뢰가 발급돼야 한다");
            Assert.IsTrue(files.Exists(SaveService.FileName));

            clock.Advance(TimeSpan.FromMinutes(5));
            var second = NewSession(files, clock);
            second.Boot();

            Assert.AreEqual(first.Save.Quests.ActiveGameDate, second.Save.Quests.ActiveGameDate);
            CollectionAssert.AreEqual(
                first.Save.Quests.Active.ConvertAll(q => q.QuestId),
                second.Save.Quests.Active.ConvertAll(q => q.QuestId),
                "재실행해도 같은 날 의뢰는 그대로");
        }

        [Test]
        public void Session_CommitAndSuspend_DoNotSkipDayBoundary()
        {
            // 회귀 테스트: Commit/Suspend 가 SavedAt 을 now 로 밀면
            // 앱이 켜진 채 넘긴 새벽 5시 경계가 정산되지 않고 사라졌다.
            var clock = new TestClock(T0);
            var session = NewSession(new MemoryFileStore(), clock);
            session.Boot();
            Assert.AreEqual("2026-09-11", session.Save.Quests.ActiveGameDate);

            clock.Advance(TimeSpan.FromHours(20));   // KST 09-12 06:00 — 경계를 넘었다
            session.Commit();
            session.Suspend();
            session.Resume();

            Assert.AreEqual("2026-09-12", session.Save.Quests.ActiveGameDate);
        }

        [Test]
        public void Session_Tick_ResolvesWhileAppIsOpen_AndRaisesEvent()
        {
            var clock = new TestClock(T0);
            var session = NewSession(new MemoryFileStore(), clock);
            session.Boot();
            session.Save.Player.Money = 1_000_000;
            session.Save.Scavs.Add(new ScavState { Uid = "sc_0000", Search = 8, Combat = 6, Survival = 9 });

            Assert.IsNotNull(session.Depart("MYEONGDONG", new[] { "sc_0000" }));
            Assert.AreEqual(1_000_000 - 27000, session.Save.Player.Money, "파견비는 출발 즉시 차감");

            ResolveReport raised = null;
            session.Resolved += r => raised = r;

            clock.Advance(TimeSpan.FromMinutes(19));
            session.Tick();
            Assert.IsNull(raised, "복귀 전에는 아무 일도 없다");

            clock.Advance(TimeSpan.FromMinutes(2));
            session.Tick();
            Assert.IsNotNull(raised);
            Assert.AreEqual(1, raised.Expeditions.Count);
        }

        [Test]
        public void Session_ManualWorkAndSell_PersistImmediately()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var session = NewSession(files, clock);
            session.Boot();

            long pay = session.DoManualWork(0.9, out var quality);
            Assert.AreEqual(CraftQuality.Excellent, quality);
            Assert.AreEqual(15000, pay);

            Warehouse.TryAdd(session.Save.Warehouse, _data, "MED16", 1);
            Assert.IsTrue(session.Sell("MED16", 1));

            var reloaded = NewSession(files, clock);
            reloaded.Boot();
            Assert.AreEqual(15000 + 4500, reloaded.Save.Player.Money, "조작 직후 앱이 죽어도 남아 있어야 한다");
        }

        private GameSession NewSession(MemoryFileStore files, TestClock clock) =>
            new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), _data, clock);
    }

    /// <summary>
    /// 실제 <c>StreamingAssets/Data/*.json</c> 검증. 데이터를 고칠 때마다 여기가 먼저 깨져야 한다.
    /// 같은 검사를 Unity 없이 돌리려면 <c>Tools/check_data.py</c>.
    /// </summary>
    [TestFixture]
    public class DataFileTests
    {
        /// <summary>
        /// 파견비 검증에 쓰는 기대 롤 보정. 티어1 스캐브의 탐색은 3~5 라 floor(탐색/4) = +1 로 본다.
        /// 공식은 <c>ExpeditionSystem</c> 의 rolls 계산과 같다.
        /// </summary>
        private const int ExpectedSearchRollBonus = 1;

        /// <summary>파견비 비율 허용 폭. 목표는 balance.expeditionCostRatio(0.55).</summary>
        private const double CostRatioTolerance = 0.10;

        /// <summary>
        /// 본편 로케일에 이름이 없는 아이템 13종 (MAINLINE_REFERENCE §9). 본편 누락이라 여기서 고칠 수 없다.
        /// 모바일 데이터가 이 중 하나를 쓰기 시작하면 <see cref="MobileReferencedIds_HaveNamesInAllLanguages"/> 가 잡는다.
        /// </summary>
        private static readonly HashSet<string> KnownMainlineLocaleGaps = new HashSet<string>
        {
            "AMO24", "AMO26", "KEY_DORM_206", "KEY_DORM_208", "KEY_DORM_306",
            "QUEST_CULT_MARKED_DOCUMENTS", "QUEST_CULT_SUYU_RITUAL", "QUEST_NEURONIX_CREATURE_DNA",
            "QUEST_SCA008_REAGENT", "QUEST_HAN_RIVER_DORM_SURVEY", "QUEST_HAN_RIVER_206_MANIFEST",
            "QUEST_HAN_RIVER_208_INSPECTION", "QUEST_HAN_RIVER_306_LEDGER",
        };

        private static readonly string[] Languages = { "ko", "en", "jp", "zh", "ru" };

        private JsonDataRegistry _data;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name =>
            {
                string path = Path.Combine(dir, name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });
        }

        [Test]
        public void Registry_LoadsRealFiles()
        {
            var bolt = _data.GetItem("JUNK03");
            Assert.IsNotNull(bolt);
            Assert.AreEqual(15000, bolt.BasePrice);
            Assert.AreEqual(10, bolt.MaxStack);
            CollectionAssert.Contains(bolt.Tags, "볼트");

            Assert.AreEqual(60000, _data.GetMap("GURO_FACTORY").BaseCostWage);
            Assert.AreEqual("LT_GURO_FACTORY", _data.GetMap("GURO_FACTORY").LootTableId);
            Assert.AreEqual(4, _data.GetLootTable("LT_GURO_FACTORY").RollsMin);
            Assert.AreEqual("JUNK03", _data.GetRecipe("RCP_BOLT").OutputItemId);
            Assert.AreEqual("JUNK16", _data.GetRecipe("RCP_BOLT").Inputs[0].ItemId);
            Assert.AreEqual(8, _data.GetQuestPool("DQP_HWANG").Count);
            Assert.AreEqual(15000, _data.Balance.LaborPayByQuality[(int)CraftQuality.Excellent]);
        }

        [Test]
        public void AllReferencedIds_Exist()
        {
            var problems = new List<string>();

            foreach (var map in _data.Maps)
            {
                if (!_data.IsMainlineMap(map.Id)) problems.Add($"{map.Id}: 본편에 없는 지역");
                if (_data.GetLootTable(map.LootTableId) == null) problems.Add($"{map.Id}: 없는 전리품 표 {map.LootTableId}");
            }
            foreach (var table in _data.LootTables)
                foreach (var e in table.Entries)
                {
                    if (_data.GetItem(e.ItemId) == null) problems.Add($"{table.Id}: 없는 아이템 {e.ItemId}");
                    if (e.Weight <= 0 || e.CountMin < 1 || e.CountMax < e.CountMin)
                        problems.Add($"{table.Id}/{e.ItemId}: weight/count 범위 이상");
                }
            foreach (var r in _data.Recipes)
            {
                if (_data.GetItem(r.OutputItemId) == null) problems.Add($"{r.Id}: 없는 산출물 {r.OutputItemId}");
                foreach (var i in r.Inputs)
                    if (_data.GetItem(i.ItemId) == null) problems.Add($"{r.Id}: 없는 재료 {i.ItemId}");
            }
            foreach (var poolId in _data.QuestPoolIds)
                foreach (var q in _data.GetQuestPool(poolId))
                    foreach (var req in q.Requires)
                        if (!string.IsNullOrEmpty(req.ItemId) && _data.GetItem(req.ItemId) == null)
                            problems.Add($"{q.Id}: 없는 요구 아이템 {req.ItemId}");

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryQuest_IsObtainable_AndTouchesTwoSystems()
        {
            var obtainable = ObtainableItemIds();
            var problems = new List<string>();

            foreach (var q in _data.GetQuestPool("DQP_HWANG"))
            {
                if (q.Touches.Length < 2) problems.Add($"{q.Id}: touches {q.Touches.Length} < 2 (GDD §5)");
                foreach (var req in q.Requires)
                {
                    if (!string.IsNullOrEmpty(req.ItemId) && !obtainable.Contains(req.ItemId))
                        problems.Add($"{q.Id}: {req.ItemId} 는 어느 전리품 표·레시피에서도 안 나온다");
                    if (!string.IsNullOrEmpty(req.Tag) && CheapestObtainableWithTag(req.Tag, obtainable) == null)
                        problems.Add($"{q.Id}: 태그 '{req.Tag}' 를 가진 입수 가능 아이템이 없다");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void QuestRewards_FollowFormula()
        {
            // 보상금 = Σ(basePrice × 수량) × 티어 배수 (DATA_SCHEMA §3-1). 반올림 여유 ±5%.
            var obtainable = ObtainableItemIds();
            var problems = new List<string>();

            foreach (var q in _data.GetQuestPool("DQP_HWANG"))
            {
                long value = 0;
                foreach (var req in q.Requires)
                {
                    var def = !string.IsNullOrEmpty(req.ItemId)
                        ? _data.GetItem(req.ItemId)
                        : CheapestObtainableWithTag(req.Tag, obtainable);
                    value += (def?.BasePrice ?? 0) * req.Count;
                }
                double expected = value * _data.Balance.QuestRewardMultiplierByTier[q.Tier - 1];
                double diff = (q.RewardMoney - expected) / expected;
                if (Math.Abs(diff) > 0.05)
                    problems.Add($"{q.Id}: 보상 {q.RewardMoney:N0} / 공식 {expected:N0} ({diff:+0.0%;-0.0%})");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void ExpeditionCosts_MatchCostRatio()
        {
            // 파견비 = 기대 회수가치 × expeditionCostRatio. 크면 파견을 안 보내고 작으면 무뇌 반복이 된다.
            double target = _data.Balance.ExpeditionCostRatio;
            var problems = new List<string>();

            foreach (var map in _data.Maps)
            {
                var table = _data.GetLootTable(map.LootTableId);
                double weightSum = 0, valueSum = 0;
                foreach (var e in table.Entries)
                {
                    weightSum += e.Weight;
                    valueSum += e.Weight * _data.GetItem(e.ItemId).BasePrice * (e.CountMin + e.CountMax) / 2.0;
                }
                double expectedLoot = valueSum / weightSum * (table.RollsMin + ExpectedSearchRollBonus);
                double ratio = (map.BaseCostWage + map.BaseCostSupply) / expectedLoot;
                if (Math.Abs(ratio - target) > CostRatioTolerance)
                    problems.Add($"{map.Id}: 파견비/기대가치 {ratio:0.00} (목표 {target:0.00}±{CostRatioTolerance:0.00})");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void Items_HaveNamesInAllLanguages()
        {
            var missing = new List<string>();
            foreach (var lang in Languages)
            {
                LoadLocale(lang);
                foreach (var item in _data.Items)
                    if (!KnownMainlineLocaleGaps.Contains(item.Id) && !Loc.Has($"ITEM_{item.Id}_NAME"))
                        missing.Add($"{lang}: ITEM_{item.Id}_NAME");
            }
            Assert.IsEmpty(missing, $"{missing.Count}건 누락\n" + string.Join("\n", missing));
        }

        [Test]
        public void MobileReferencedIds_HaveNamesInAllLanguages()
        {
            var keys = new List<string> { "TRADER_" + GameSession.DefaultEmployerNpcId + "_NAME" };
            foreach (var id in ObtainableItemIds()) keys.Add($"ITEM_{id}_NAME");
            foreach (var map in _data.Maps) keys.Add($"MAP_{map.Id}_NAME");

            var missing = new List<string>();
            foreach (var lang in Languages)
            {
                LoadLocale(lang);
                foreach (var key in keys)
                    if (!Loc.Has(key)) missing.Add($"{lang}: {key}");
            }
            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        // ── 헬퍼 ────────────────────────────────────────────────

        private HashSet<string> ObtainableItemIds()
        {
            var set = new HashSet<string>();
            foreach (var t in _data.LootTables)
                foreach (var e in t.Entries) set.Add(e.ItemId);
            foreach (var r in _data.Recipes) set.Add(r.OutputItemId);
            return set;
        }

        /// <summary>태그 조건 차감은 싼 것부터라(DailyQuestSystem.RemoveByTag) 보상 기준도 최저가다.</summary>
        private ItemDef CheapestObtainableWithTag(string tag, HashSet<string> obtainable)
        {
            ItemDef best = null;
            foreach (var id in obtainable)
            {
                var def = _data.GetItem(id);
                if (def == null || Array.IndexOf(def.Tags, tag) < 0) continue;
                if (best == null || def.BasePrice < best.BasePrice) best = def;
            }
            return best;
        }

        private static void LoadLocale(string lang)
        {
            string path = Path.Combine(Application.dataPath, "Resources", "Locales", lang + ".json");
            Loc.Load(lang, File.ReadAllText(path), null);
        }
    }

    /// <summary>테스트용 인메모리 파일 저장소. 실제 디스크를 건드리지 않는다.</summary>
    internal sealed class MemoryFileStore : IFileStore
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>();

        public bool Exists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void WriteAllText(string path, string content) => _files[path] = content;

        public void Replace(string source, string dest)
        {
            _files[dest] = _files[source];
            _files.Remove(source);
        }

        public bool TryMove(string source, string dest)
        {
            if (!_files.ContainsKey(source)) return false;
            _files[dest] = _files[source];
            _files.Remove(source);
            return true;
        }
    }
}
