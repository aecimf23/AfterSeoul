using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Scav;

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
            scav.Equipment["Weapon"] = "WPN04";
            scav.Equipment["BodyArmor"] = null;   // 빈 슬롯도 그대로 왕복해야 한다
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
            var shipment = new MailShipment
            {
                TxId = "m2p_20260911_a7f3e9c1", QueuedAt = T0, TotalValue = 15000,
            };
            shipment.Items.Add(new ItemStack("JUNK03", 1));
            save.Mail.Outbox.Add(shipment);
            save.Mail.DailyQuotaGameDate = "2026-09-11";
            save.Mail.DailyQuotaUsedValue = 15000;
            save.Mail.DailyShipmentsUsed = 1;

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
        public void ManualWork_ScoreGradesQualityAndQuantity()
        {
            Assert.AreEqual(CraftQuality.Failed, FactorySystem.GradeManualWork(_data, 0.1));
            Assert.AreEqual(CraftQuality.Normal, FactorySystem.GradeManualWork(_data, 0.3), "경계값은 위 등급");
            Assert.AreEqual(CraftQuality.Good, FactorySystem.GradeManualWork(_data, 0.7));
            Assert.AreEqual(CraftQuality.Excellent, FactorySystem.GradeManualWork(_data, 0.85));

            // 품질은 이제 보수가 아니라 산출 개수를 바꾼다.
            var recipe = _data.GetRecipe("RCP_SALVAGE");
            int fail = Workbench.OutputCountFor(_data, recipe, CraftQuality.Failed);
            int good = Workbench.OutputCountFor(_data, recipe, CraftQuality.Good);
            int best = Workbench.OutputCountFor(_data, recipe, CraftQuality.Excellent);

            Assert.AreEqual(recipe.OutputCount, good, "'양호' 가 레시피 기준 개수여야 한다");
            Assert.Less(fail, good, "실패하면 덜 나와야 한다");
            Assert.Greater(best, good, "잘하면 더 나와야 한다 — 눈에 보이는 보상이다");
            Assert.GreaterOrEqual(fail, 1, "재료를 넣고 다 두드렸는데 0 개면 고장으로 읽힌다");
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
            var scav = new ScavState { Uid = "sc_0000", Search = 8, Combat = 6, Survival = 9 };
            scav.Equipment[EquipSlot.Weapon] = "MEL01";
            session.Save.Scavs.Add(scav);

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

            // 작업대에서 하나 만들고 판다.
            Assert.IsTrue(session.StartWork("RCP_SALVAGE"));
            var recipe = _data.GetRecipe("RCP_SALVAGE");

            WorkStepResult result = default;
            for (int i = 0; i < recipe.ManualSteps; i++) result = session.AdvanceWork(0.95);

            Assert.IsTrue(result.Completed);
            Assert.AreEqual(CraftQuality.Excellent, result.Quality);
            Assert.Greater(result.Output.Count, 0);

            long unit = Market.SellPrice(_data, result.Output.ItemId);
            Assert.IsTrue(session.Sell(result.Output.ItemId, result.Output.Count));

            var reloaded = NewSession(files, clock);
            reloaded.Boot();
            Assert.AreEqual(unit * result.Output.Count, reloaded.Save.Player.Money,
                "조작 직후 앱이 죽어도 남아 있어야 한다");
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

        [TestCase("HWANG", 0)]
        [TestCase("DR_CHOI", 0)]
        [TestCase("YONGSAN_KIM", 0)]
        [TestCase("HWANG", 20)]
        [TestCase("DR_CHOI", 20)]
        [TestCase("YONGSAN_KIM", 20)]
        public void EmployerChoice_IssuesTodaysQuestsOnceAndPersists(string employerId, int hoursBeforeChoice)
        {
            var clock = new TestClock(new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero));
            var files = new MemoryFileStore();
            var session = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();
            Assert.IsTrue(session.NeedsEmployerChoice);
            Assert.IsEmpty(session.Save.Quests.Active);
            clock.Advance(TimeSpan.FromHours(hoursBeforeChoice));

            Assert.IsTrue(session.ChooseEmployer(employerId));
            Assert.AreEqual(GameTime.GameDateOf(clock.UtcNow).ToString(), session.Save.Quests.ActiveGameDate);
            Assert.IsNotEmpty(session.Save.Quests.Active, "Choosing an employer must issue work immediately, without waiting until tomorrow.");
            var expectedIds = new List<string>();
            var poolIds = new HashSet<string>();
            foreach (var quest in _data.GetQuestPool(Employers.Find(_data, employerId).QuestPoolId))
                poolIds.Add(quest.Id);
            foreach (var quest in session.Save.Quests.Active)
            {
                Assert.IsTrue(poolIds.Contains(quest.QuestId), "Only the chosen employer's quests may be issued.");
                expectedIds.Add(quest.QuestId);
            }
            long money = session.Save.Player.Money;

            var reloaded = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), _data, clock);
            reloaded.Boot();
            Assert.AreEqual(employerId, reloaded.Save.Player.EmployerNpcId);
            foreach (var candidate in _data.Employers)
                Assert.IsFalse(reloaded.ChooseEmployer(candidate.NpcId), "Employer choice cannot be repeated to reroll quests or reset starting money.");
            reloaded.Tick();
            var actualIds = new List<string>();
            foreach (var quest in reloaded.Save.Quests.Active) actualIds.Add(quest.QuestId);
            CollectionAssert.AreEqual(expectedIds, actualIds);
            Assert.AreEqual(money, reloaded.Save.Player.Money);
            Assert.AreEqual(GameTime.GameDateOf(clock.UtcNow).ToString(), reloaded.Save.Quests.ActiveGameDate);
        }

        [TestCase("HWANG")]
        [TestCase("DR_CHOI")]
        [TestCase("YONGSAN_KIM")]
        public void Boot_RepairsMissingFirstDayQuestsButPreservesCompletedQuests(string employerId)
        {
            var clock = new TestClock(new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero));
            var files = new MemoryFileStore();
            var session = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();
            // Persist the exact old shape: employer chosen, today's marker, no issued quests.
            session.Save.Player.EmployerNpcId = employerId;
            session.Save.Player.Money = 12345;
            session.Save.Quests.ActiveGameDate = GameTime.GameDateOf(clock.UtcNow).ToString();
            session.Save.Quests.Active.Clear();
            session.Commit();

            var repaired = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), _data, clock);
            repaired.Boot();
            Assert.IsNotEmpty(repaired.Save.Quests.Active, "An already chosen employer must recover the missing first day's work.");
            Assert.AreEqual(employerId, repaired.Save.Player.EmployerNpcId);
            Assert.AreEqual(12345, repaired.Save.Player.Money, "Repair must not grant starting money again.");
            var ids = new List<string>();
            foreach (var quest in repaired.Save.Quests.Active)
            {
                ids.Add(quest.QuestId);
                quest.Delivered = true;
            }
            repaired.Commit();

            var completed = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), _data, clock);
            completed.Boot();
            var restoredIds = new List<string>();
            foreach (var quest in completed.Save.Quests.Active)
            {
                restoredIds.Add(quest.QuestId);
                Assert.IsTrue(quest.Delivered, "Completed quests must not be replaced by a fresh daily board.");
            }
            CollectionAssert.AreEqual(ids, restoredIds);
            Assert.AreEqual(12345, completed.Save.Player.Money);
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

        /// <summary>
        /// 티어 1 시급과 <c>balance.baseWagePerHour</c> 는 같아야 한다.
        /// 둘이 어긋나면 <c>expeditions.json</c> 의 파견비가 "티어 1 한 명 기준"이 아니게 되고,
        /// 바로 위/아래의 회수가치 비율 검증이 조용히 다른 것을 재게 된다.
        /// </summary>
        [Test]
        public void TierOneWage_IsTheBaseWage()
        {
            ScavTierDef tier1 = null;
            foreach (var t in _data.ScavPool.Tiers)
                if (t.Tier == 1) { tier1 = t; break; }

            Assert.IsNotNull(tier1, "scav_pool.json 에 티어 1 이 없다");
            Assert.AreEqual(_data.Balance.BaseWagePerHour, tier1.WagePerHour,
                "scav_pool.json tier1 wagePerHour 와 balance.json baseWagePerHour 가 다르다");
        }

        /// <summary>
        /// 상위 티어의 시급이 능력치에 비해 지나치게 가파르지 않은가.
        ///
        /// <para>인건비가 시급에 비례하므로, 시급 비율이 능력치 비율보다 훨씬 크면
        /// 상위 티어는 어떤 상황에서도 손해가 되어 티어 시스템 전체가 죽은 콘텐츠가 된다.
        /// 상위 티어는 생존이 높아 상실 위험이 낮으니 약간의 프리미엄은 허용한다.</para>
        /// </summary>
        [Test]
        public void HigherTiers_ArePricedWithinReachOfTheirStats()
        {
            const double premiumCap = 1.45;   // 능력치 비율 대비 시급 비율 상한

            ScavTierDef t1 = null;
            foreach (var t in _data.ScavPool.Tiers)
                if (t.Tier == 1) { t1 = t; break; }
            Assert.IsNotNull(t1);

            double baseStat = (t1.StatTotalMin + t1.StatTotalMax) / 2.0;
            var problems = new List<string>();

            foreach (var t in _data.ScavPool.Tiers)
            {
                if (t.Tier == 1) continue;
                double statRatio = (t.StatTotalMin + t.StatTotalMax) / 2.0 / baseStat;
                double wageRatio = (double)t.WagePerHour / t1.WagePerHour;

                if (wageRatio > statRatio * premiumCap)
                    problems.Add(
                        $"tier{t.Tier}: 시급 {wageRatio:0.00}배 / 능력치 {statRatio:0.00}배 " +
                        $"(상한 {statRatio * premiumCap:0.00}배) — 쓸 이유가 없어진다");
                if (wageRatio <= 1.0)
                    problems.Add($"tier{t.Tier}: 시급이 티어 1 이하다 — 상위 티어가 공짜 이득이 된다");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>
        /// <b>새 플레이어에게 못 깨는 의뢰가 걸리지 않는가.</b>
        ///
        /// <para>이 검사가 없어서 탄약 의뢰(5.45x39mm 30발)가 티어 1 에 있었다.
        /// 탄약은 구로·의정부에서만 나오고 둘 다 레벨 잠금이라, 레벨 1 플레이어에게는
        /// 하루 종일 붙어 있는 빈 칸이었다.</para>
        /// </summary>
        /// <summary>
        /// <b>빈손 플레이어가 시작할 수 있는가.</b> 모든 레시피가 재료를 요구하면
        /// 게임이 첫 화면에서 끝난다 — 파견을 보낼 돈도 그 물건을 팔아서 버는 구조다.
        /// </summary>
        [Test]
        public void AtLeastOneRecipe_NeedsNoInputs()
        {
            var free = new List<string>();
            foreach (var r in _data.Recipes)
                if (r.ManualSteps > 0 && r.Inputs.Length == 0) free.Add(r.Id);

            Assert.IsNotEmpty(free, "재료 없이 만들 수 있는 레시피가 없다 — 빈손으로는 아무것도 못 한다");
        }

        /// <summary>
        /// 작업대 레시피의 산출물이 실제 아이템이고, 공정 수가 말이 되는가.
        /// </summary>
        [Test]
        public void ManualRecipes_AreWellFormed()
        {
            var problems = new List<string>();

            foreach (var r in _data.Recipes)
            {
                if (r.ManualSteps <= 0) continue;

                if (_data.GetItem(r.OutputItemId) == null)
                    problems.Add($"{r.Id}: 산출물 {r.OutputItemId} 이 items.json 에 없다");
                if (r.OutputCount < 1)
                    problems.Add($"{r.Id}: 산출 개수가 0 이하");
                if (r.ManualSteps > 6)
                    problems.Add($"{r.Id}: 공정 {r.ManualSteps}단계 — 한 물건에 너무 많이 두드린다");

                foreach (var input in r.Inputs)
                    if (_data.GetItem(input.ItemId) == null)
                        problems.Add($"{r.Id}: 재료 {input.ItemId} 이 items.json 에 없다");
            }

            Assert.IsEmpty(problems, string.Join("\n", problems.ToArray()));
        }

        [Test]
        public void TierOneQuests_AreAchievableOnAFreshSave()
        {
            var save = new GameSave
            {
                Player = new PlayerState { EmployerNpcId = "HWANG" },
            };

            var stuck = new List<string>();
            foreach (var poolId in _data.QuestPoolIds)
            {
                foreach (var q in _data.GetQuestPool(poolId))
                {
                    if (q.Tier != 1) continue;
                    if (!AfterSeoul.Quest.QuestReach.IsAchievable(save, _data, q)) stuck.Add(q.Id);
                }
            }

            Assert.IsEmpty(stuck,
                "레벨 1 · 신뢰도 0 에서 못 깨는 티어 1 의뢰: " + string.Join(", ", stuck.ToArray()));
        }

        /// <summary>
        /// 지역과 해당 제작 단계가 열린 시점에 그 티어 의뢰를 실제로 깰 수 있는가.
        /// 중급은 작업대 3, 고급 의료품은 작업대 5가 필요하다.
        /// </summary>
        [Test]
        public void HigherTierQuests_AreAchievableWithTheirMapsAndWorkstations()
        {
            var stuck = new List<string>();

            foreach (var probe in new[] { (tier: 2, level: 5, trust: 12, station: 3), (tier: 3, level: 10, trust: 30, station: 5) })
            {
                var save = new GameSave
                {
                    Player = new PlayerState { EmployerNpcId = "HWANG", Level = probe.level },
                };
                save.NpcTrust["HWANG"] = probe.trust;
                save.Factory.StationLevel = probe.station;

                foreach (var poolId in _data.QuestPoolIds)
                {
                    foreach (var q in _data.GetQuestPool(poolId))
                    {
                        if (q.Tier != probe.tier) continue;
                        if (!AfterSeoul.Quest.QuestReach.IsAchievable(save, _data, q))
                            stuck.Add($"{q.Id}(티어 {probe.tier} / 레벨 {probe.level})");
                    }
                }
            }

            Assert.IsEmpty(stuck,
                "티어가 열렸는데 못 깨는 의뢰: " + string.Join(", ", stuck.ToArray()));
        }

        [Test]
        public void SurgeryQuest_RequiresTheAdvancedWorkstationEvenAtTierThree()
        {
            var save = new GameSave
            {
                Player = new PlayerState { EmployerNpcId = "DR_CHOI", Level = 10 },
            };
            save.NpcTrust["HWANG"] = 30;
            save.NpcTrust["DR_CHOI"] = 30;
            QuestDef quest = null;
            foreach (var candidate in _data.GetQuestPool("DQP_DR_CHOI"))
                if (candidate.Id == "DQ_CHOI_SURGERY_01") quest = candidate;
            Assert.IsNotNull(quest);
            save.Factory.StationLevel = 4;
            Assert.IsFalse(AfterSeoul.Quest.QuestReach.IsAchievable(save, _data, quest),
                "Player level must not bypass the trauma-kit workstation requirement.");
            save.Factory.StationLevel = 5;
            Assert.IsTrue(AfterSeoul.Quest.QuestReach.IsAchievable(save, _data, quest),
                "Opening the trauma-kit recipe must make this quest reachable.");
        }

        /// <summary>
        /// 파견이 남는 장사인가 — <b>실제로 보내 보고 판단한다.</b>
        ///
        /// <para>예전 이 테스트는 전리품 표의 가중평균에 <c>expeditionCostRatio</c> 를 맞춰 보는
        /// 식이었다. 장비가 생기기 전에는 그게 곧 기대 회수가치였지만, 지금은 운(좋은 전리품
        /// 쪽에서 뽑을 확률)·추가 회수 횟수·탐색 스탯이 붙어서 <b>같은 지역도 맨손과 풀장비가
        /// 3배 넘게 차이난다.</b> 숫자 하나로는 표현할 수 없다.</para>
        ///
        /// <para>그래서 공식을 테스트 안에 다시 쓰지 않는다 — 그게 이 테스트가 규칙과 따로 놀게 된
        /// 원인이다. 진짜 <see cref="ExpeditionSystem"/> 으로 여러 번 보내서 실제 회수액을 재고,
        /// 두 가지만 본다: <b>맨손으로도 밑지지 않는가</b>(안 그러면 시작할 수가 없다),
        /// <b>장비를 다 갖춰도 공짜는 아닌가</b>(안 그러면 무뇌 반복이 된다).</para>
        /// </summary>
        [Test]
        public void ExpeditionPayoff_StaysWorthIt_BareHandedAndGeared()
        {
            var problems = new List<string>();

            foreach (var map in _data.Maps)
            {
                double bare = MeasurePayoffRatio(map, geared: false);
                double geared = MeasurePayoffRatio(map, geared: true);

                // 입구 지역은 맨손으로도 남아야 한다. 여기서 밑지면 빈손 플레이어가 시작할 수가 없다.
                // 상위 지역은 맨손이 밑져도 된다 — 그게 장비를 사게 만드는 압력이다.
                if (map.Tier <= 1 && bare > EntryBareCeiling)
                    problems.Add($"{map.Id}(입구): 맨손 파견비/회수 {bare:0.00} — 장비 없이는 시작을 못 한다");

                // 풀장비: 비용이 회수의 이 정도는 돼야 파견이 '선택'으로 남는다.
                if (geared < GearedPayoffFloor)
                    problems.Add($"{map.Id}: 풀장비 파견비/회수 {geared:0.00} — 사실상 공짜다");

                // 장비를 갖추면 반드시 나아져야 한다. 안 그러면 장비를 살 이유가 없다.
                if (geared >= bare)
                    problems.Add($"{map.Id}: 장비를 갖춰도 수지가 나아지지 않는다 (맨손 {bare:0.00} / 풀장비 {geared:0.00})");
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>
        /// 입구 지역에서는 장비가 <b>안 나온다</b> — 그래서 초반 장비는 줍는 게 아니라 사는 것이다.
        /// 그 전제가 성립하려면 신뢰도 0 상인이 무기를 뺀 모든 칸을 댈 수 있어야 한다.
        ///
        /// <para>무기만 예외인 건 의도다. 고용하면 지참 무기가 딸려 오므로 막히지 않고,
        /// 무기 칸은 좋은 물건 확률을 가장 크게 흔들어서 — 신뢰도를 쌓을 이유로 남겨 둔다.
        /// 이 테스트는 그 판단을 글이 아니라 코드로 붙잡아 둔다.</para>
        /// </summary>
        [Test]
        public void TrustZeroTrader_CanOutfitEverySlotButTheWeapon()
        {
            var save = new GameSave
            {
                SavedAt = ProbeStart,
                Player = new PlayerState
                {
                    CreatedAt = ProbeStart, Money = 100_000_000,
                    EmployerNpcId = GameSession.DefaultEmployerNpcId,
                },
            };
            // 신뢰도를 일부러 올리지 않는다 — 막 시작한 플레이어 그대로.

            var missing = new List<string>();
            foreach (var slot in EquipSlot.All)
            {
                if (slot == EquipSlot.Weapon) continue;
                if (Shop.OffersFor(save, _data, slot).Count == 0)
                    missing.Add(EquipSlot.LabelOf(slot));
            }

            Assert.IsEmpty(missing,
                "신뢰도 0 에서 살 수 없는 칸: " + string.Join(", ", missing.ToArray()) +
                " — 입문 지역 전리품에도 장비가 없으므로 이 칸은 영영 못 채운다");
        }

        /// <summary>
        /// <c>chainNextId</c> 는 아직 아무도 읽지 않는다 (연쇄 의뢰는 미구현).
        ///
        /// <para>필드를 지우지 않는 이유는 스키마에 남겨 둔 계획이기 때문이고, 테스트를 두는 이유는
        /// <b>값을 채워 넣고 왜 안 이어지는지 모르는 일</b>을 막기 위해서다. 죽은 필드는 조용히
        /// 사람을 속인다 — 이번에 manualSteps 가 그랬다 (JSON 에는 있는데 파서가 안 옮겼다).</para>
        /// </summary>
        [Test]
        public void ChainedQuests_AreNotImplemented_SoTheFieldMustStayEmpty()
        {
            var filled = new List<string>();

            foreach (var poolId in _data.QuestPoolIds)
                foreach (var quest in _data.GetQuestPool(poolId))
                    if (!string.IsNullOrEmpty(quest.ChainNextId))
                        filled.Add($"{quest.Id} → {quest.ChainNextId}");

            Assert.IsEmpty(filled,
                "chainNextId 에 값이 있는데 이걸 읽는 코드가 없다: " + string.Join(", ", filled.ToArray()) +
                "\n연쇄 의뢰를 붙이려면 DailyQuestSystem 이 먼저 이 필드를 써야 한다.");
        }

        /// <summary>
        /// <b>해금한 지역이 갈 이유가 있는가.</b>
        ///
        /// <para>바로 위 <see cref="ExpeditionPayoff_StaysWorthIt_BareHandedAndGeared"/> 는 각 지역이
        /// <b>혼자서</b> 말이 되는지만 봤다 — 맨손으로 밑지는지, 풀장비로 공짜인지. 그래서
        /// <b>다른 지역과 비교하면 갈 이유가 없는</b> 지역은 그냥 통과했다. 실제로 남산(Lv7)이 그랬다:
        /// 구로(Lv5)보다 1회 수익도 낮고(217k vs 347k) 시간당은 5.5배 낮아서 어떤 상황에서도
        /// 정답이 아니었다. <b>레벨을 올려서 연 곳이 벌점이었던 셈이다.</b></para>
        ///
        /// <para>방치형이라 지표가 둘이다. <b>1회 수익</b>은 자리를 비울 때 쓸모가 있고(끝나고 노는
        /// 시간은 그냥 버려지므로 긴 파견의 존재 이유가 이것이다), <b>시간당</b>은 붙어 있을 때
        /// 쓸모가 있다. 둘 중 <b>하나만</b> 이기면 된다 — 그게 그 지역의 역할이 된다.
        /// 둘 다 지면 역할이 없다.</para>
        ///
        /// <para>해금 축이 다르면(레벨 vs 신뢰도) 비교하지 않는다. 서로 다른 방식으로 여는 곳이라
        /// "어느 쪽이 나중"을 말할 수 없다. 기본 개방 지역만 모두의 하한이다.</para>
        /// </summary>
        [Test]
        public void EveryUnlockedRegion_IsTheBestChoiceForSomething()
        {
            var perTrip = new Dictionary<string, double>();
            var perHour = new Dictionary<string, double>();
            var maps = new List<MapDef>();

            foreach (var map in _data.Maps)
            {
                double trip = MeasurePayoffPerTrip(map);
                double hours = map.DurationMinutes / 60.0;

                perTrip[map.Id] = trip;
                perHour[map.Id] = hours > 0 ? trip / hours : trip;
                maps.Add(map);
            }

            var dominated = new List<string>();

            foreach (var later in maps)
            {
                foreach (var earlier in maps)
                {
                    if (ReferenceEquals(later, earlier)) continue;
                    if (!IsEasierGate(earlier, later)) continue;

                    // 근소한 차이로 깜빡이지 않게 5% 여유를 둔다. 잡으려는 것은
                    // 남산처럼 두 지표 모두 배 이상 지는 경우지 박빙이 아니다.
                    bool losesTrip = perTrip[later.Id] < perTrip[earlier.Id] * 0.95;
                    bool losesHour = perHour[later.Id] < perHour[earlier.Id] * 0.95;

                    if (losesTrip && losesHour)
                        dominated.Add(
                            $"{later.Id} 는 더 쉽게 여는 {earlier.Id} 보다 1회 수익도 " +
                            $"({perTrip[later.Id]:N0} < {perTrip[earlier.Id]:N0}) 시간당도 " +
                            $"({perHour[later.Id]:N0} < {perHour[earlier.Id]:N0}) 낮다 — 갈 이유가 없다");
                }
            }

            Assert.IsEmpty(dominated, string.Join("\n", dominated.ToArray()));
        }

        /// <summary>
        /// <paramref name="earlier"/> 가 <paramref name="later"/> 보다 확실히 쉽게 열리는가.
        /// 축이 다르면(레벨 vs 신뢰도) 순서를 말할 수 없으므로 false.
        /// </summary>
        private static bool IsEasierGate(MapDef earlier, MapDef later)
        {
            string a = earlier.Unlock != null ? earlier.Unlock.Type : "default";
            string b = later.Unlock != null ? later.Unlock.Type : "default";

            if (a == "default") return b != "default";
            if (b == "default") return false;
            if (a != b) return false;

            // 같은 사람의 신뢰도끼리만 비교한다. 사람이 다르면 난이도 순서가 없다.
            if (a == "npcTrust" && earlier.Unlock.NpcId != later.Unlock.NpcId) return false;

            return earlier.Unlock.Value < later.Unlock.Value;
        }

        /// <summary>풀장비 1인을 여러 번 보내 잰 <b>파견 1회 순익</b> (회수 가치 − 파견비).</summary>
        private double MeasurePayoffPerTrip(MapDef map)
        {
            const int Runs = 300;
            long lootTotal = 0, costTotal = 0;

            for (int i = 0; i < Runs; i++)
            {
                var clock = new TestClock(ProbeStart);
                var save = PayoffProbeSave(geared: true, (uint)(i + 1));

                costTotal += ExpeditionSystem.CostFor(save, _data, map, new[] { "sc_probe" });
                new ExpeditionSystem().Depart(save, _data, map.Id, new[] { "sc_probe" }, clock.UtcNow);

                clock.Advance(TimeSpan.FromDays(1));
                var report = new OfflineResolver(clock, new List<ITimelineSystem> { new ExpeditionSystem() })
                    .Resolve(save, _data);

                foreach (var result in report.Expeditions)
                    foreach (var stack in result.Loot)
                        lootTotal += (long)_data.GetItem(stack.ItemId).BasePrice * stack.Count;
            }

            return (lootTotal - costTotal) / (double)Runs;
        }

        /// <summary>입구 지역(티어 1) 맨손 파견비/회수 비율의 상한. 넘으면 빈손 플레이어가 첫 파견에서 막힌다.</summary>
        private const double EntryBareCeiling = 0.85;

        /// <summary>풀장비 파견비/회수 비율의 하한. 이보다 낮으면 생각 없이 반복하는 게 최적해가 된다.</summary>
        private const double GearedPayoffFloor = 0.12;

        /// <summary>파견 한 번의 (파견비 / 실제 회수 가치). 시드를 바꿔 가며 여러 번 돌린 평균이다.</summary>
        private double MeasurePayoffRatio(MapDef map, bool geared)
        {
            const int Runs = 120;
            long lootTotal = 0, costTotal = 0;

            for (int i = 0; i < Runs; i++)
            {
                var clock = new TestClock(ProbeStart);
                var save = PayoffProbeSave(geared, (uint)(i + 1));

                long cost = ExpeditionSystem.CostFor(save, _data, map, new[] { "sc_probe" });
                new ExpeditionSystem().Depart(save, _data, map.Id, new[] { "sc_probe" }, clock.UtcNow);

                clock.Advance(TimeSpan.FromDays(1));
                var report = new OfflineResolver(clock, new List<ITimelineSystem> { new ExpeditionSystem() })
                    .Resolve(save, _data);

                foreach (var result in report.Expeditions)
                    foreach (var stack in result.Loot)
                        lootTotal += (long)_data.GetItem(stack.ItemId).BasePrice * stack.Count;

                costTotal += cost;
            }

            return lootTotal == 0 ? double.PositiveInfinity : (double)costTotal / lootTotal;
        }

        private static readonly DateTimeOffset ProbeStart =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// 티어 1 수준의 스캐브 한 명. <paramref name="geared"/> 면 구할 수 있는 장비를 다 채운다.
        ///
        /// <para>돈·레벨·신뢰도는 넉넉히 준다. 여기서 재려는 건 "보낼 수 있느냐"가 아니라
        /// "보내면 남느냐"다 — 해금 조건에 걸려 파견이 반려되면 회수액이 0 이 되어 엉뚱한 실패가 난다.</para>
        /// </summary>
        private GameSave PayoffProbeSave(bool geared, uint rngCounter)
        {
            var save = new GameSave
            {
                SavedAt = ProbeStart,
                RngCounter = rngCounter,
                Player = new PlayerState
                {
                    CreatedAt = ProbeStart, Money = 100_000_000, Level = 20,
                    EmployerNpcId = GameSession.DefaultEmployerNpcId,
                },
            };
            save.Warehouse.Capacity = 9999;
            save.NpcTrust[GameSession.DefaultEmployerNpcId] = 100;

            var scav = new ScavState
            {
                Uid = "sc_probe", Name = "시험체", Search = 4, Combat = 4, Survival = 4,
                Status = ScavStatus.Idle, HiredAt = ProbeStart,
            };
            scav.Equipment[EquipSlot.Weapon] = geared ? BestGear(EquipSlot.Weapon) : "MEL01";

            if (geared)
                foreach (var slot in EquipSlot.All)
                {
                    if (slot == EquipSlot.Weapon) continue;
                    string best = BestGear(slot);
                    if (best != null) scav.Equipment[slot] = best;
                }

            save.Scavs.Add(scav);
            return save;
        }

        /// <summary>
        /// 그 칸에서 제일 좋은 장비. <b>값이 아니라 효과로 고른다</b> — 제일 비싼 무기는
        /// 950,000원짜리 단검(최하 등급)이라, 값으로 고르면 "풀장비"가 맨손이나 다름없어진다.
        /// </summary>
        private string BestGear(string slot)
        {
            string best = null;
            int bestRank = -1;

            foreach (var item in _data.Items)
            {
                if (!item.Equippable || item.EquipSlot != slot) continue;

                int rank = Equipment.EffectRank(item);
                if (rank > bestRank) { bestRank = rank; best = item.Id; }
            }
            return best;
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

        // ── 레시피가 실제로 만들어지는가 ─────────────────────────

        /// <summary>
        /// 모든 레시피의 재료가 <b>어디선가 구해지는가</b>.
        ///
        /// <para>못 깨는 의뢰(<c>QuestReach</c>)와 같은 종류다. 도면을 열어 줬는데 재료를
        /// 구할 데가 없으면, 플레이어는 작업대를 올려놓고 영영 회색인 줄을 보게 된다.
        /// 실제로 원단 계열(립스탑·아라미드·방탄 원단)은 어느 전리품 표에도 없어서
        /// 처음 짠 고급 레시피를 통째로 버렸다 — 그때 이 검사가 있었으면 바로 알았을 것이다.</para>
        /// </summary>
        [Test]
        public void EveryRecipeInput_IsObtainable()
        {
            var obtainable = ObtainableItemIds();
            var problems = new List<string>();

            foreach (var recipe in _data.Recipes)
                foreach (var input in recipe.Inputs)
                    if (!obtainable.Contains(input.ItemId))
                        problems.Add($"{recipe.Id}: 재료 {input.ItemId} 를 어디서도 구할 수 없다");

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>
        /// 상위 도면은 <b>돈벌이가 아니라 경로</b>여야 한다.
        ///
        /// <para>재료 없이 도는 레시피(폐자재 분류)가 탭당 수익의 천장이다. 상위 레시피가 그걸
        /// 넘으면 아무도 파견을 안 보내고 작업대만 두드리게 된다 — 그러면 이 게임은
        /// 클리커 하나만 남는다. 상위 레시피의 값어치는 '지금 갈 수 없는 지역에서만 나오는 것을
        /// 만든다'이지 돈이 아니다.</para>
        /// </summary>
        [Test]
        public void HigherRecipes_DoNotOutEarnTheStarter()
        {
            double ceiling = 0;
            foreach (var recipe in _data.Recipes)
                if (recipe.Inputs.Length == 0 && recipe.ManualSteps > 0)
                {
                    double perStep = ProfitOf(recipe) / recipe.ManualSteps;
                    if (perStep > ceiling) ceiling = perStep;
                }

            Assert.Greater(ceiling, 0, "재료 없이 도는 레시피가 없다 — 빈손 플레이어의 입구가 없다");

            var problems = new List<string>();
            foreach (var recipe in _data.Recipes)
            {
                if (recipe.Inputs.Length == 0 || recipe.ManualSteps <= 0) continue;

                double perStep = ProfitOf(recipe) / recipe.ManualSteps;
                if (perStep > ceiling)
                    problems.Add($"{recipe.Id}: 탭당 {perStep:N0}원 — 입문 레시피({ceiling:N0}원)보다 벌이가 좋다");
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>'양호' 품질 기준 산출 가치 - 재료 가치.</summary>
        private double ProfitOf(RecipeDef recipe)
        {
            double output = _data.GetItem(recipe.OutputItemId).BasePrice * (double)recipe.OutputCount;

            double inputs = 0;
            foreach (var input in recipe.Inputs)
                inputs += _data.GetItem(input.ItemId).BasePrice * (double)input.Count;

            return output - inputs;
        }

        private static void LoadLocale(string lang)
        {
            string dir = Path.Combine(Application.dataPath, "Resources", "Locales");
            Loc.Load(lang,
                File.ReadAllText(Path.Combine(dir, lang + ".json")), null,
                ReadIfExists(Path.Combine(dir, "mobile", lang + ".json")), null);
        }

        private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

        // ── NPC 대사 ─────────────────────────────────────────────

        /// <summary>
        /// 의뢰마다 고용주의 말이 붙어 있는가.
        ///
        /// <para>품목 목록만 있으면 심부름표지만 한 줄이 붙으면 부탁이 된다 — 이 게임에서
        /// 사람이 말을 거는 몇 안 되는 자리다. 의뢰를 추가하고 대사를 빼먹으면
        /// 그 의뢰만 조용히 목소리가 없어지므로 여기서 잡는다.</para>
        ///
        /// <para>ko 와 en 만 본다. jp·zh·ru 는 en 으로 떨어진다 — 기계번역 대사를 넣느니
        /// 영어로 두는 편이 낫다 (물건 이름과 달리 대사는 어조가 내용이다).</para>
        /// </summary>
        [Test]
        public void EveryQuest_HasEmployerDialogue()
        {
            var missing = new List<string>();

            foreach (var lang in new[] { "ko", "en" })
            {
                LoadLocale(lang);

                foreach (var poolId in _data.QuestPoolIds)
                    foreach (var quest in _data.GetQuestPool(poolId))
                    {
                        if (string.IsNullOrEmpty(Loc.QuestAccept(quest.Id)))
                            missing.Add($"{lang}: {quest.Id}_ACCEPT");
                        if (string.IsNullOrEmpty(Loc.QuestComplete(quest.Id)))
                            missing.Add($"{lang}: {quest.Id}_COMPLETE");
                    }
            }

            Assert.IsEmpty(missing,
                $"대사 없는 의뢰 {missing.Count}건\n" + string.Join("\n", missing.ToArray()));
        }

        /// <summary>
        /// 모바일 덧칠이 본편 문구를 덮어쓰지 않는가.
        ///
        /// <para>덧칠은 같은 키가 있으면 이기도록 만들었다. 편한 대신, 실수로 본편 아이템 이름을
        /// 가려 놓으면 모바일에서만 다른 이름이 뜬다 — 본편과 같은 물건을 다루는 게 이 게임의 전제라
        /// 그건 버그다. 일부러 덮어쓸 일이 생기면 그때 이 테스트를 고치면서 이유를 적을 것.</para>
        /// </summary>
        [Test]
        public void MobileOverlay_AddsOnly_NeverShadowsMainlineText()
        {
            string dir = Path.Combine(Application.dataPath, "Resources", "Locales");
            var shadowed = new List<string>();

            foreach (var lang in Languages)
            {
                // 본편 테이블만 올린다 (덧칠 없이). 여기서 우리 키가 이미 잡히면 그건 충돌이다.
                Loc.Load(lang, File.ReadAllText(Path.Combine(dir, lang + ".json")), null);

                foreach (var key in MobileDialogueKeys())
                    if (Loc.Has(key)) shadowed.Add($"{lang}: {key}");
            }

            Assert.IsEmpty(shadowed,
                "모바일 덧칠이 본편 문구를 가리고 있다: " + string.Join(", ", shadowed.ToArray()));
        }

        /// <summary>
        /// 덧칠 파일이 담고 있는 키. 파일을 파싱하지 않고 데이터에서 만든다 —
        /// 테스트 어셈블리는 JSON 라이브러리를 직접 참조하지 않고(<c>overrideReferences</c>),
        /// 어차피 이 키들은 의뢰 id 에서 기계적으로 나온다.
        /// </summary>
        private IEnumerable<string> MobileDialogueKeys()
        {
            foreach (var poolId in _data.QuestPoolIds)
                foreach (var quest in _data.GetQuestPool(poolId))
                {
                    yield return quest.Id + "_ACCEPT";
                    yield return quest.Id + "_COMPLETE";
                }
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
