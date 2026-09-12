using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Quest;
using AfterSeoul.Scav;

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

        /// <summary>
        /// <b>게임이 실제로 쓰는 시스템 목록을 그대로 빌려 온다.</b>
        ///
        /// <para>예전에는 여기서 목록을 손으로 적었고 "등록 순서는 GameSession 과 같아야 한다"는
        /// 주석까지 달려 있었다. 그런데 시스템이 넷 늘어나는 동안(고용 시장·보조 인력·구조·치료)
        /// 이 목록은 셋에서 멈춰 있었다. 그래서 <b>결정론·멱등성·시계 조작 내성 같은 핵심 보증이
        /// 나중에 붙은 시스템에는 한 번도 걸린 적이 없었다.</b> tie-break 순서를 확인한다던
        /// 그 주석도 그 시점부터 거짓이었다.</para>
        ///
        /// <para>목록을 두 벌 두면 반드시 갈라진다. <see cref="GameSession.Systems"/> 한 벌만 두고
        /// 여기서 빌려 쓴다 — 세션의 세이브는 쓰지 않고 시스템 구성만 가져온다.</para>
        /// </summary>
        private OfflineResolver NewResolver(TestClock clock) =>
            new OfflineResolver(clock, NewSession(clock).Systems);

        private GameSession NewSession(TestClock clock) =>
            new GameSession(
                new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), _data, clock);

        /// <summary>목록이 다시 갈라지면 여기가 먼저 깨진다.</summary>
        [Test]
        public void TheResolverUnderTest_UsesEverySystemTheGameUses()
        {
            var names = new List<string>();
            foreach (var s in NewSession(new TestClock(T0)).Systems) names.Add(s.Name);

            // 나중에 붙은 것들이다. 이 셋이 빠진 채로 "정산 테스트 통과"였던 기간이 있었다.
            Assert.Contains("Treatment", names, "치료가 정산에 없으면 자는 동안 안 낫는다");
            Assert.Contains("Rescue", names);
            Assert.Contains("ScavMarket", names);

            Assert.GreaterOrEqual(names.Count, 7,
                "시스템이 줄었다면 무언가 정산에서 빠진 것이다: " + string.Join(", ", names.ToArray()));
        }

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
            Assert.AreEqual(1, report.ClockAnomalies, "화면이 몇 번째인지 알 수 있어야 한다");

            // 되돌림 보고는 나머지 칸이 전부 비어 있다. 그걸 "알릴 것 없음"으로 치면
            // GameSession 이 Resolved 를 쏘지 않고 홈 화면도 카드를 감춰서,
            // 정작 게임이 멈춘 그 상황에서 멈췄다는 말만 사라진다.
            Assert.IsFalse(report.IsEmpty, "멈췄다는 사실을 화면에 전할 길이 없다");
        }

        /// <summary>
        /// <b>기기 시각이 크게 틀어져도 세이브가 잠기면 안 된다.</b>
        ///
        /// <para>되돌림을 만나면 <c>SavedAt</c> 을 그대로 두고 돌아가는데, <c>SavedAt</c> 은
        /// 정산이 끝나야 갱신된다. 그래서 배터리가 빠져 날짜가 몇 년 전으로 리셋된 기기에서는
        /// 실제 시간이 그 미래를 따라잡을 때까지 — 몇 년간 — 매번 여기서 돌아왔다.
        /// 파견은 복귀하지 않고 제작은 끝나지 않고 날짜도 넘어가지 않는다. 증상은 하나뿐이다:
        /// "게임이 멈췄다". 되돌림을 막으려다 세이브를 잠근 셈이었다.</para>
        /// </summary>
        [Test]
        public void AWildlyWrongClock_DoesNotFreezeTheSaveForever()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            var resolver = NewResolver(clock);

            clock.Advance(TimeSpan.FromMinutes(10));
            resolver.Resolve(save, _data);

            // 기기 날짜가 5년 전으로 리셋됐다.
            clock.Rewind(TimeSpan.FromDays(365 * 5));
            var rewound = resolver.Resolve(save, _data);
            Assert.IsTrue(rewound.ClockWentBackwards);

            // 기준점이 여전히 미래인 것은 맞다 — 되돌려서 정산 구간을 만들어 낼 수는 없다.
            Assert.Greater(save.SavedAt, clock.UtcNow, "되돌림으로 구간이 생기면 안 된다");

            // 하지만 그 거리는 잘려 있어야 한다. 며칠 뒤에는 다시 굴러가야 한다.
            Assert.LessOrEqual(save.SavedAt - clock.UtcNow, resolver.MaxFutureSkew,
                "기준 시각이 먼 미래에 박혀 있으면 세이브가 잠긴다");

            // 이틀 뒤 — 예전 코드라면 5년 내내 여기서 되돌아왔다.
            clock.Advance(TimeSpan.FromDays(2));
            var resumed = resolver.Resolve(save, _data);
            Assert.IsFalse(resumed.ClockWentBackwards, "시각이 틀어진 기기에서 게임이 영영 멈춘다");
            Assert.AreEqual(clock.UtcNow, save.SavedAt, "정산이 정상으로 돌아와야 한다");
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

            // 장비. 슬롯마다 싼 것 하나 · 비싼 것 하나면 "좋은 장비가 실제로 더 좋은가"를 볼 수 있다.
            // 값은 본편 실제 아이템의 것을 그대로 옮겼다.
            r._items["MEL01"] = Gear("MEL01", EquipSlot.Weapon, 5000, weaponGrade: 1);      // 단검
            r._items["WPN01"] = Gear("WPN01", EquipSlot.Weapon, 45000, weaponGrade: 2);     // AV-74M
            r._items["WPN22"] = Gear("WPN22", EquipSlot.Weapon, 260000, weaponGrade: 5);    // MXMR
            r._items["HDW09"] = Gear("HDW09", EquipSlot.Headwear, 12000, armorClass: 1);    // TSh-4M
            r._items["HDW07"] = Gear("HDW07", EquipSlot.Headwear, 640000, armorClass: 6);   // 모듈 M-6
            r._items["AMR10"] = Gear("AMR10", EquipSlot.BodyArmor, 16000, armorClass: 1);   // 3M
            r._items["AMR04"] = Gear("AMR04", EquipSlot.BodyArmor, 560000, armorClass: 6);  // 헥스그리드
            r._items["EAR01"] = Gear("EAR01", EquipSlot.Earpiece, 18000, hearingRange: 12); // GSSH-01
            r._items["EAR03"] = Gear("EAR03", EquipSlot.Earpiece, 165000, hearingRange: 18);// Sordin
            r._items["RIG01"] = Gear("RIG01", EquipSlot.TacticalRig, 8000, gridSlots: 6);   // 뱅크로버
            r._items["RIG03"] = Gear("RIG03", EquipSlot.TacticalRig, 80000, gridSlots: 12); // TV-110
            r._items["BPK01"] = Gear("BPK01", EquipSlot.Backpack, 12000, gridSlots: 16);    // MBSS
            r._items["BPK06"] = Gear("BPK06", EquipSlot.Backpack, 250000, gridSlots: 64);   // 비드라-8

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

            // 해금 조건이 붙은 지역. 위 둘은 조건 없이(=처음부터 열림) 두어
            // 기존 파견 테스트가 해금 판정의 영향을 받지 않게 한다.
            // 잠긴 지역에만 있는 전리품. "레벨 1 에게 걸리면 절대 못 깨는 의뢰"를
            // 재현할 수 있어야 QuestReach 를 제대로 검증한다.
            r._loot["LT_UIJ"] = new LootTableDef
            {
                Id = "LT_UIJ", RollsMin = 3, RollsMax = 6,
                Entries = new[]
                {
                    new LootEntry { ItemId = "FOOD01", Weight = 50, CountMin = 1, CountMax = 2 },
                    new LootEntry { ItemId = "JUNK03", Weight = 50, CountMin = 1, CountMax = 2 },
                }
            };

            r._maps["UIJEONGBU"] = new MapDef
            {
                Id = "UIJEONGBU", Tier = 3, DurationMinutes = 180, BaseCostWage = 110000, BaseCostSupply = 40000,
                RiskLevel = 2, CombatChance = 0.22, LootTableId = "LT_UIJ",
                Unlock = new UnlockDef { Type = "playerLevel", Value = 9 },
            };
            r._maps["YONGSAN_MARKET"] = new MapDef
            {
                Id = "YONGSAN_MARKET", Tier = 2, DurationMinutes = 45, BaseCostWage = 48000, BaseCostSupply = 16000,
                RiskLevel = 3, CombatChance = 0.28, LootTableId = "LT_GURO",
                Unlock = new UnlockDef { Type = "npcTrust", NpcId = "HWANG", Value = 10 },
            };
            // 데이터에 오타가 난 경우. 열리면 안 된다.
            r._maps["BROKEN_UNLOCK"] = new MapDef
            {
                Id = "BROKEN_UNLOCK", Tier = 1, DurationMinutes = 10, BaseCostWage = 1000, BaseCostSupply = 0,
                RiskLevel = 1, CombatChance = 0.0, LootTableId = "LT_GURO",
                Unlock = new UnlockDef { Type = "phaseOfTheMoon", Value = 3 },
            };

            // 재료 없는 기초 제작. 빈손 플레이어의 입구라 반드시 하나는 있어야 한다.
            r._recipes["RCP_SALVAGE"] = new RecipeDef
            {
                Id = "RCP_SALVAGE", StationLevel = 1, OutputItemId = "JUNK16", OutputCount = 3,
                WorkSeconds = 300, ManualSteps = 3, Inputs = System.Array.Empty<ItemStack>(),
                StepNames = new[] { "폐자재 분류", "부품 뜯어내기", "다듬기" },
                StepGames = new[] { "inspect", "hold", "timing" },
            };

            r._recipes["RCP_BOLT"] = new RecipeDef
            {
                // StepGames 를 일부러 비워 둔다 — 지정이 없을 때 Minigames 가 돌려가며
                // 배정하는 쪽(기본 경로)도 테스트가 지나가야 한다.
                Id = "RCP_BOLT", StationLevel = 1, OutputItemId = "JUNK03", OutputCount = 2,
                WorkSeconds = 3600, ManualSteps = 3, Inputs = new[] { new ItemStack("JUNK16", 1) },
                StepNames = new[] { "소재 물리기", "나사산 가공", "검수" },
            };

            // 작업대 단계로 잠긴 레시피 하나. 이게 없으면 "레벨이 도면을 연다"를 확인할 수가 없다.
            r._recipes["RCP_AMMO"] = new RecipeDef
            {
                Id = "RCP_AMMO", StationLevel = 2, OutputItemId = "AMO01", OutputCount = 20,
                WorkSeconds = 900, ManualSteps = 4, Inputs = new[] { new ItemStack("JUNK16", 2) },
                StepNames = new[] { "탄피 선별", "뇌관 교체", "장약", "탄두 압착" },
                StepGames = new[] { "inspect", "timing", "hold", "timing" },
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

        private static ItemDef Gear(string id, string equipSlot, long price,
            int weaponGrade = 0, int armorClass = 0, int hearingRange = 0, int gridSlots = 0)
            => new ItemDef
            {
                Id = id, Category = "Equipment", BasePrice = price, MaxStack = 1,
                Tags = new[] { "장비" },
                Equippable = true, EquipSlot = equipSlot,
                WeaponGrade = weaponGrade, ArmorClass = armorClass,
                HearingRange = hearingRange, GridSlots = gridSlots,
            };

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

        /// <summary>상점. 신뢰도 0 상인 하나 · 신뢰도 5 상인 하나면 해금 동작을 볼 수 있다.</summary>
        public ShopDef Shop { get; } = new ShopDef
        {
            PriceMultiplier = 1.35,
            Traders = new[]
            {
                new ShopTraderDef { NpcId = "DONGDAEMUN_CHOI", RequiresTrust = 0 },
                new ShopTraderDef { NpcId = "YONGSAN_KIM", RequiresTrust = 5 },
            },
        };

        private readonly Dictionary<string, NpcDef> _npcs = new Dictionary<string, NpcDef>
        {
            // 저가 방어구만. 무기가 없다 — 신뢰도 0 에서 무기를 못 사는 상황을 재현한다.
            { "DONGDAEMUN_CHOI", new NpcDef { Id = "DONGDAEMUN_CHOI", Currency = "Won",
                InventoryItemIds = new[] { "HDW09", "AMR10", "RIG01", "BPK01", "EAR01" } } },
            { "YONGSAN_KIM", new NpcDef { Id = "YONGSAN_KIM", Currency = "Dollar",
                InventoryItemIds = new[] { "WPN01", "WPN22" } } },
        };

        public NpcDef GetNpc(string id) => _npcs.TryGetValue(id, out var v) ? v : null;

        /// <summary>전송 정책. 실제 파일과 같은 모양으로, 값만 작게 잡는다.</summary>
        public TransferPolicyDef Transfer { get; } = new TransferPolicyDef
        {
            UnitPriceCeiling = 15000,
            DenyItemIds = new[] { "JUNK16" },   // 카테고리는 열려 있지만 개별 차단
            Categories = new Dictionary<string, TransferCategoryDef>
            {
                { "JUNK", new TransferCategoryDef { Allowed = true, MaxPerShipment = 4, MaxPerDay = 8 } },
                { "MED",  new TransferCategoryDef { Allowed = true, MaxPerShipment = 3, MaxPerDay = 6 } },
                { "AMO",  new TransferCategoryDef { Allowed = true, MaxPerShipment = 60, MaxPerDay = 120 } },
                { "FOOD", new TransferCategoryDef { Allowed = true, MaxPerShipment = 5, MaxPerDay = 10 } },
                { "WPN",  new TransferCategoryDef { Allowed = false } },
                { "AMR",  new TransferCategoryDef { Allowed = false } },
                { "MEL",  new TransferCategoryDef { Allowed = false } },
                { "HDW",  new TransferCategoryDef { Allowed = false } },
                { "BPK",  new TransferCategoryDef { Allowed = false } },
                { "RIG",  new TransferCategoryDef { Allowed = false } },
                { "EAR",  new TransferCategoryDef { Allowed = false } },
            },
            Limits = new TransferLimitsDef
            {
                MaxShipmentsPerDay = 2,
                MaxItemStacksPerShipment = 4,
                MaxShipmentValue = 25000,
                MaxDailyValue = 50000,
            },
        };

        public IEnumerable<MapDef> AllMaps => _maps.Values;
        public IEnumerable<RecipeDef> AllRecipes => _recipes.Values;

        /// <summary>태그 조건을 만족하는 아이템을 찾을 때 쓴다 (VerticalSliceTests).</summary>
        public IEnumerable<ItemDef> Items => _items.Values;
        public IEnumerable<ItemDef> AllItems => _items.Values;

        /// <summary>
        /// 탐색 이벤트는 <b>일부러 비워 둔다.</b>
        ///
        /// <para>사건은 회수 횟수와 사고 여부를 흔든다. 가짜 데이터에 넣어 두면 장비 효과나
        /// 사고율을 통계로 재는 기존 테스트들이 사건 때문에 흔들려서, 무엇이 원인인지 알 수 없게 된다.
        /// 사건 자체는 실제 데이터로 <c>ExpeditionEventTests</c> 가 본다.</para>
        /// </summary>
        public IEnumerable<ExpeditionEventDef> AllExpeditionEvents
            => System.Array.Empty<ExpeditionEventDef>();

        /// <summary>고용주 하나. 보너스는 0 으로 둬서 기존 수치 테스트가 흔들리지 않게 한다.</summary>
        public IEnumerable<EmployerDef> AllEmployers => new[]
        {
            new EmployerDef { NpcId = "HWANG", QuestPoolId = "DQP_HWANG" },
        };

        /// <summary>고용 시장 템플릿. 이름 6개 / 특성 3개 / 티어 2개면 시장 테스트에 충분하다.</summary>
        public ScavPoolDef ScavPool { get; } = new ScavPoolDef
        {
            Names = new[] { "김철수", "박영호", "이순자", "정대만", "최기웅", "한미숙" },
            Traits = new[]
            {
                new ScavTraitDef { Id = "TR_YONGSAN_NATIVE" },
                new ScavTraitDef { Id = "TR_COWARD" },
                new ScavTraitDef { Id = "TR_MEDIC" },
            },
            Tiers = new[]
            {
                new ScavTierDef { Tier = 1, StatTotalMin = 9,  StatTotalMax = 14, HireCost = 250_000, WagePerHour = 40_000,  TraitCount = 1, StarterWeapon = "MEL01" },
                new ScavTierDef { Tier = 2, StatTotalMin = 15, StatTotalMax = 21, HireCost = 900_000, WagePerHour = 72_000,  TraitCount = 2, StarterWeapon = "WPN01" },
            },
        };

        public ItemDef GetItem(string id) => _items.TryGetValue(id, out var v) ? v : null;
        public MapDef GetMap(string id) => _maps.TryGetValue(id, out var v) ? v : null;
        public RecipeDef GetRecipe(string id) => _recipes.TryGetValue(id, out var v) ? v : null;
        public LootTableDef GetLootTable(string id) => _loot.TryGetValue(id, out var v) ? v : null;
        public IReadOnlyList<QuestDef> GetQuestPool(string id) =>
            _pools.TryGetValue(id, out var v) ? v : null;
    }
}
