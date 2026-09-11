using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Quest;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 코어 정산 엔진 테스트.
    ///
    /// <para>여기 있는 시나리오는 <c>Tools/sim_model.py</c> 의 것과 1:1로 대응한다.
    /// 파이썬 쪽에서 먼저 설계를 검증했고, 이쪽은 C# 구현이 같은 답을 내는지 본다.
    /// <b>둘 중 하나를 고치면 반드시 다른 쪽도 고친다.</b></para>
    ///
    /// <para>이 테스트들은 전부 UnityEngine 을 쓰지 않는다. Game.Core 가
    /// Unity 비의존이라서 가능한 것이고, 그래서 빠르다.</para>
    /// </summary>
    [TestFixture]
    public class OfflineResolverTests
    {
        // KST 2026-09-11 10:00
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave(long money = 1_000_000, int scavCount = 2)
        {
            var save = new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = money, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
            for (int i = 0; i < scavCount; i++)
                save.Scavs.Add(new ScavState
                {
                    Uid = $"sc_{i:0000}", Name = $"스캐브{i}",
                    Search = 8, Combat = 6, Survival = 9, HiredAt = T0,
                });
            return save;
        }

        private static OfflineResolver NewResolver(TestClock clock) =>
            // 등록 순서는 GameSession 과 같아야 한다. tie-break 에 영향을 준다.
            new OfflineResolver(clock, new List<ITimelineSystem>
            {
                new ExpeditionSystem(), new FactorySystem(), new DailyQuestSystem()
            });

        // ── 1. 결정론 ──────────────────────────────────────────

        [Test]
        public void SameSeed_ProducesSameResult()
        {
            var results = new List<string>();
            for (int run = 0; run < 2; run++)
            {
                var clock = new TestClock(T0);
                var save = NewSave();
                new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY",
                    new[] { "sc_0000", "sc_0001" }, T0);

                clock.Advance(TimeSpan.FromHours(1));
                var report = NewResolver(clock).Resolve(save, _data);
                results.Add(Fingerprint(save) + "|" + DescribeGains(report));
            }
            Assert.AreEqual(results[0], results[1], "같은 입력은 항상 같은 결과여야 한다");
        }

        // ── 2. 멱등성 ──────────────────────────────────────────

        [Test]
        public void ResolvingTwice_DoesNotDuplicateRewards()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { "sc_0000" }, T0);
            var resolver = NewResolver(clock);

            clock.Advance(TimeSpan.FromHours(1));
            var first = resolver.Resolve(save, _data);
            string after = Fingerprint(save);

            var second = resolver.Resolve(save, _data);   // 같은 시각에 한 번 더

            Assert.AreEqual(1, first.Expeditions.Count, "1회차에 파견이 정산돼야 한다");
            Assert.AreEqual(0, second.Expeditions.Count, "2회차에 재정산되면 안 된다");
            Assert.AreEqual(after, Fingerprint(save), "2회차 정산은 상태를 바꾸면 안 된다");
        }

        // ── 3. 시계 되돌림 ──────────────────────────────────────

        [Test]
        public void ClockRewind_StopsProgressButDoesNotPunish()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { "sc_0000" }, T0);
            var resolver = NewResolver(clock);

            clock.Advance(TimeSpan.FromMinutes(10));
            resolver.Resolve(save, _data);
            string before = Fingerprint(save);
            var savedAt = save.SavedAt;

            clock.Rewind(TimeSpan.FromDays(1));
            var report = resolver.Resolve(save, _data);

            Assert.IsTrue(report.ClockWentBackwards);
            Assert.AreEqual(before, Fingerprint(save), "되돌림 시 상태 불변");
            Assert.AreEqual(1, save.ClockAnomalyCount, "기록은 남긴다");
            Assert.AreEqual(savedAt, save.SavedAt, "세이브 시각이 과거로 밀리면 안 된다");
        }

        // ── 4. 시계 앞당김 ──────────────────────────────────────

        [Test]
        public void ClockFastForward_GivesNoAdvantage()
        {
            string baseline = null;
            foreach (var skip in new[] {
                TimeSpan.FromHours(1), TimeSpan.FromDays(7), TimeSpan.FromDays(200) })
            {
                var clock = new TestClock(T0);
                var save = NewSave();
                new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { "sc_0000" }, T0);

                clock.Advance(skip);
                var report = NewResolver(clock).Resolve(save, _data);
                string desc = DescribeExpedition(report.Expeditions[0]);

                if (baseline == null) baseline = desc;
                else Assert.AreEqual(baseline, desc,
                    $"{skip} 만큼 건너뛰어도 전리품이 같아야 한다 — 리세마라 방지");
            }
        }

        // ── 5. 시간 순 정산 ─────────────────────────────────────

        [Test]
        public void Events_AreAppliedInChronologicalOrder()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            save.Factory.StationLevel = 2;
            Warehouse.TryAdd(save.Warehouse, _data, "JUNK16", 4);

            new FactorySystem().Enqueue(save, _data, "RCP_BOLT", T0);              // 1시간 뒤
            new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY",
                new[] { "sc_0000" }, T0);                                          // 35분 뒤

            clock.Advance(TimeSpan.FromDays(3));
            var report = NewResolver(clock).Resolve(save, _data);

            // 파견(35분)이 공장(1시간)보다 먼저 적용돼야 한다.
            int expIndex = report.Expeditions.Count > 0 ? 0 : -1;
            Assert.AreEqual(1, report.Expeditions.Count);
            Assert.AreEqual(1, report.Crafts.Count);
            Assert.Less(report.Expeditions[0].ReturnedAt, report.Crafts[0].CompletedAt,
                "파견 복귀가 공장 완료보다 먼저여야 한다");
            Assert.AreNotEqual(-1, expIndex);

            // KST 09-11 10:00 → 09-14 10:00: 경계 3회 + 최초 발급 1회
            Assert.AreEqual(4, report.DayRollovers);
        }

        // ── 6. 동시각 tie-break ─────────────────────────────────

        [Test]
        public void ExpeditionReturningExactlyAtDayBoundary_LandsBeforeQuestExpiry()
        {
            var boundary = GameTime.StartOfGameDate(new GameDate(2026, 9, 12));
            var depart = boundary.AddMinutes(-35);   // GURO 파견시간 = 35분

            var clock = new TestClock(depart.AddHours(-1));
            var save = NewSave();
            save.SavedAt = depart.AddHours(-1);

            var exp = new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY",
                new[] { "sc_0000" }, depart);
            Assert.AreEqual(boundary, exp.ReturnsAt, "복귀가 경계와 정확히 일치해야 하는 설정");

            clock.UtcNow = boundary.AddHours(1);
            var report = NewResolver(clock).Resolve(save, _data);

            Assert.AreEqual(1, report.Expeditions.Count,
                "경계와 같은 시각에 복귀한 파견도 정산돼야 한다");
            Assert.Less(EventOrder.ExpeditionReturn, EventOrder.DayRollover,
                "파견 복귀가 날짜 갱신보다 앞 순서여야 한다");
        }

        // ── 7. 날짜 경계 ────────────────────────────────────────

        [Test]
        public void DayBoundary_IsFiveAmKst()
        {
            var night = new DateTimeOffset(2026, 9, 11, 23, 0, 0, GameTime.GameZoneOffset);
            var predawn = new DateTimeOffset(2026, 9, 12, 4, 59, 0, GameTime.GameZoneOffset);
            var after = new DateTimeOffset(2026, 9, 12, 5, 1, 0, GameTime.GameZoneOffset);

            Assert.AreEqual(new GameDate(2026, 9, 11), GameTime.GameDateOf(night));
            Assert.AreEqual(new GameDate(2026, 9, 11), GameTime.GameDateOf(predawn));
            Assert.AreEqual(new GameDate(2026, 9, 12), GameTime.GameDateOf(after));

            Assert.AreEqual(0, Count(GameTime.DayBoundariesBetween(night, predawn)),
                "밤 플레이 중에 의뢰가 바뀌면 안 된다");
            Assert.AreEqual(1, Count(GameTime.DayBoundariesBetween(night, after)));
        }

        // ── 8. 일일 의뢰 ────────────────────────────────────────

        [Test]
        public void DailyQuests_StableWithinDay_RollOverNextDay()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            var resolver = NewResolver(clock);

            clock.Advance(TimeSpan.FromMinutes(1));
            resolver.Resolve(save, _data);
            var day1 = string.Join(",", save.Quests.Active.ConvertAll(q => q.QuestId));

            clock.Advance(TimeSpan.FromHours(3));
            resolver.Resolve(save, _data);
            Assert.AreEqual(day1, string.Join(",", save.Quests.Active.ConvertAll(q => q.QuestId)),
                "같은 날 재정산해도 의뢰가 바뀌면 안 된다");

            clock.Advance(TimeSpan.FromDays(1));
            resolver.Resolve(save, _data);
            Assert.AreEqual("2026-09-12", save.Quests.ActiveGameDate);
            Assert.AreEqual(3, save.Quests.Active.Count);
        }

        // ── 9. 납품 ─────────────────────────────────────────────

        [Test]
        public void Deliver_IsAllOrNothing()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            var quests = new DailyQuestSystem();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock).Resolve(save, _data);
            save.Quests.Active.Clear();
            save.Quests.Active.Add(new ActiveQuest { QuestId = "DQ_HWANG_BOLT_01" });

            Warehouse.TryAdd(save.Warehouse, _data, "JUNK03", 3);   // 5개 필요
            Assert.IsFalse(quests.TryDeliver(save, _data, "DQ_HWANG_BOLT_01"));
            Assert.AreEqual(3, Warehouse.CountOf(save.Warehouse, "JUNK03"),
                "실패했으면 아무것도 차감하면 안 된다");

            Warehouse.TryAdd(save.Warehouse, _data, "JUNK03", 2);
            long before = save.Player.Money;
            Assert.IsTrue(quests.TryDeliver(save, _data, "DQ_HWANG_BOLT_01"));
            Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, "JUNK03"));
            Assert.AreEqual(before + 95000, save.Player.Money);
            Assert.AreEqual(3, save.NpcTrust["HWANG"]);
            Assert.IsFalse(quests.TryDeliver(save, _data, "DQ_HWANG_BOLT_01"),
                "재납품은 막아야 한다");
        }

        // ── 10. 창고 ────────────────────────────────────────────

        [Test]
        public void Warehouse_OverflowIsPartialAndReported()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            save.Warehouse.Capacity = 2;

            new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { "sc_0000" }, T0);
            clock.Advance(TimeSpan.FromHours(1));
            var report = NewResolver(clock).Resolve(save, _data);

            Assert.LessOrEqual(save.Warehouse.Stacks.Count, 2, "용량을 넘으면 안 된다");
            Assert.AreEqual(1, report.Expeditions.Count, "넘쳐도 파견은 정산된다");
        }

        [Test]
        public void Warehouse_RemoveIsAllOrNothing()
        {
            var save = NewSave();
            Warehouse.TryAdd(save.Warehouse, _data, "JUNK03", 3);
            Assert.IsFalse(Warehouse.TryRemove(save.Warehouse, "JUNK03", 5));
            Assert.AreEqual(3, Warehouse.CountOf(save.Warehouse, "JUNK03"));
            Assert.IsTrue(Warehouse.TryRemove(save.Warehouse, "JUNK03", 3));
            Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, "JUNK03"));
        }

        // ── 11. 오래 비운 경우 ──────────────────────────────────

        [Test]
        public void LongAbsence_DoesNotLoseCompletedExpeditions()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { "sc_0000" }, T0);

            clock.Advance(TimeSpan.FromDays(120));
            var report = NewResolver(clock).Resolve(save, _data);

            Assert.AreEqual(1, report.Expeditions.Count, "120일 뒤에 켜도 파견은 정산돼야 한다");
            Assert.AreNotEqual(ScavStatus.OnExpedition, save.Scavs[0].Status,
                "스캐브가 영원히 파견 중으로 남으면 안 된다");
            Assert.LessOrEqual(report.DayRollovers, 32, "날짜 갱신은 MaxCatchUp 으로 제한된다");
        }

        // ── 12. 난수 ────────────────────────────────────────────

        [Test]
        public void Rng_StreamsAreIsolated()
        {
            const uint seed = 123456;

            var a = Rng.For(seed, "loot");
            var firstA = new[] { a.NextInt(100), a.NextInt(100), a.NextInt(100) };

            var combat = Rng.For(seed, "combat");
            for (int i = 0; i < 50; i++) combat.NextUInt();

            var b = Rng.For(seed, "loot");
            var firstB = new[] { b.NextInt(100), b.NextInt(100), b.NextInt(100) };

            CollectionAssert.AreEqual(firstA, firstB,
                "다른 용도의 난수를 더 소비해도 loot 결과는 그대로여야 한다");

            Assert.AreNotEqual(Rng.For(seed, "loot").NextUInt(),
                               Rng.For(seed, "accident").NextUInt());
        }

        [Test]
        public void Rng_ZeroSeedIsSafe()
        {
            var rng = new Rng(0);
            Assert.AreNotEqual(0u, rng.NextUInt(), "시드 0 이 흡수 상태가 되면 안 된다");
        }

        // ── 13. 데이터 규칙 ─────────────────────────────────────

        [Test]
        public void EveryDailyQuest_TouchesAtLeastTwoSystems()
        {
            // GDD §5: 의뢰는 창고에 있는 걸 내기만 하면 되는 체크리스트여선 안 된다.
            foreach (var quest in _data.GetQuestPool("DQP_HWANG"))
                Assert.GreaterOrEqual(quest.Touches.Length, 2,
                    $"{quest.Id} 가 경유하는 시스템이 2개 미만이다");
        }

        // ── 헬퍼 ────────────────────────────────────────────────

        private static int Count(System.Collections.Generic.IEnumerable<DateTimeOffset> seq)
        {
            int n = 0;
            foreach (var _ in seq) n++;
            return n;
        }

        private static string Fingerprint(GameSave s)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(s.Player.Money).Append('|').Append(s.Player.Exp).Append('|')
              .Append(s.RngCounter).Append('|');
            var stacks = new List<string>();
            foreach (var st in s.Warehouse.Stacks) stacks.Add(st.ItemId + "x" + st.Count);
            stacks.Sort(StringComparer.Ordinal);
            sb.Append(string.Join(",", stacks)).Append('|');
            foreach (var e in s.Expeditions) sb.Append(e.Uid).Append(e.Resolved).Append(';');
            foreach (var sc in s.Scavs) sb.Append(sc.Uid).Append(sc.Status).Append(';');
            sb.Append(s.Quests.ActiveGameDate);
            return sb.ToString();
        }

        private static string DescribeGains(ResolveReport r)
        {
            var parts = new List<string>();
            foreach (var g in r.ItemsGained) parts.Add(g.ItemId + "x" + g.Count);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(",", parts);
        }

        private static string DescribeExpedition(ExpeditionResult e)
        {
            var parts = new List<string>();
            foreach (var l in e.Loot) parts.Add(l.ItemId + "x" + l.Count);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(",", parts) + "|" + e.HadCombat + "|" + e.HadAccident;
        }
    }

    /// <summary>테스트용 최소 데이터. 실제 458종 JSON 을 읽지 않는다 — 느리고 원인 파악이 어렵다.</summary>
    internal sealed class FakeRegistry : IDataRegistry
    {
        private readonly Dictionary<string, ItemDef> _items = new Dictionary<string, ItemDef>();
        private readonly Dictionary<string, MapDef> _maps = new Dictionary<string, MapDef>();
        private readonly Dictionary<string, RecipeDef> _recipes = new Dictionary<string, RecipeDef>();
        private readonly Dictionary<string, LootTableDef> _loot = new Dictionary<string, LootTableDef>();
        private readonly Dictionary<string, List<QuestDef>> _pools = new Dictionary<string, List<QuestDef>>();

        public static FakeRegistry Build()
        {
            var r = new FakeRegistry();

            r._items["JUNK03"] = new ItemDef { Id = "JUNK03", Category = "Junk", BasePrice = 15000, MaxStack = 10, Tags = new[] { "부품", "볼트", "금속" } };
            r._items["JUNK16"] = new ItemDef { Id = "JUNK16", Category = "Junk", BasePrice = 15000, MaxStack = 1, Tags = new[] { "부품", "금속" } };
            r._items["MED16"] = new ItemDef { Id = "MED16", Category = "Medical", BasePrice = 4500, MaxStack = 1, Tags = new[] { "의료", "소모품" } };
            r._items["AMO01"] = new ItemDef { Id = "AMO01", Category = "Ammo", BasePrice = 1500, MaxStack = 60, Tags = new[] { "탄약" } };
            r._items["FOOD01"] = new ItemDef { Id = "FOOD01", Category = "Food", BasePrice = 5000, MaxStack = 1, Tags = new[] { "식량", "소모품" } };

            r._loot["LT_GURO"] = new LootTableDef
            {
                Id = "LT_GURO", RollsMin = 3, RollsMax = 6,
                Entries = new[]
                {
                    new LootEntry { ItemId = "JUNK03", Weight = 40, CountMin = 1, CountMax = 2 },
                    new LootEntry { ItemId = "JUNK16", Weight = 30, CountMin = 1, CountMax = 2 },
                    new LootEntry { ItemId = "MED16",  Weight = 20, CountMin = 1, CountMax = 2 },
                    new LootEntry { ItemId = "AMO01",  Weight = 10, CountMin = 5, CountMax = 20 },
                }
            };

            r._maps["GURO_FACTORY"] = new MapDef { Id = "GURO_FACTORY", Tier = 2, DurationMinutes = 35, BaseCostWage = 60000, BaseCostSupply = 25000, RiskLevel = 3, CombatChance = 0.32, LootTableId = "LT_GURO" };
            r._maps["MYEONGDONG"] = new MapDef { Id = "MYEONGDONG", Tier = 1, DurationMinutes = 20, BaseCostWage = 20000, BaseCostSupply = 7000, RiskLevel = 1, CombatChance = 0.10, LootTableId = "LT_GURO" };

            r._recipes["RCP_BOLT"] = new RecipeDef
            {
                Id = "RCP_BOLT", StationLevel = 1, OutputItemId = "JUNK03", OutputCount = 1,
                WorkSeconds = 3600, Inputs = new[] { new ItemStack("JUNK16", 2) },
            };

            r._pools["DQP_HWANG"] = new List<QuestDef>
            {
                Q("DQ_HWANG_BOLT_01",  1, "JUNK03", null, 5,  95000, 3, 120),
                Q("DQ_HWANG_MED_01",   1, "MED16",  null, 3,  18000, 2,  90),
                Q("DQ_HWANG_AMMO_01",  1, "AMO01",  null, 30, 60000, 2, 100),
                Q("DQ_HWANG_METAL_01", 2, null,     "금속", 4, 70000, 3, 200),
                Q("DQ_HWANG_FOOD_01",  1, "FOOD01", null, 2,  12000, 1,  60),
            };
            return r;
        }

        private static QuestDef Q(string id, int tier, string itemId, string tag, int count,
                                  long money, int trust, long exp) => new QuestDef
        {
            Id = id, Tier = tier, RewardMoney = money, RewardTrust = trust, RewardExp = exp,
            Requires = new[] { new QuestRequirement { ItemId = itemId, Tag = tag, Count = count } },
            Touches = tag != null
                ? new[] { "craft", "warehouse" }
                : new[] { "expedition", "warehouse" },
        };

        public BalanceDef Balance { get; } = new BalanceDef();

        public ItemDef GetItem(string id) => _items.TryGetValue(id, out var v) ? v : null;
        public MapDef GetMap(string id) => _maps.TryGetValue(id, out var v) ? v : null;
        public RecipeDef GetRecipe(string id) => _recipes.TryGetValue(id, out var v) ? v : null;
        public LootTableDef GetLootTable(string id) => _loot.TryGetValue(id, out var v) ? v : null;
        public IReadOnlyList<QuestDef> GetQuestPool(string id) =>
            _pools.TryGetValue(id, out var v) ? v : null;
    }
}
