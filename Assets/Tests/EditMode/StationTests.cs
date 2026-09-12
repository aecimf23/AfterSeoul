using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 작업대 성장 — GDD §6 의 <c>수동 → 작업대 개선 → 보조 인력 → 반자동 → 자동</c>.
    ///
    /// <para><b>이 경로 전체가 죽어 있었다.</b> <c>StationLevel</c> 은 선언만 돼 있고 어디서도
    /// 올라가지 않았고, <c>AutoLevel</c> 은 읽는 코드가 아예 없었다. 레벨업이 없어서 지역 해금과
    /// 의뢰 티어가 전부 얼어 있던 것과 같은 종류다 — <b>값을 읽는 코드는 다 있는데 그 값을 바꾸는
    /// 코드가 없으면, 테스트는 전부 통과하고 게임만 멈춰 있다.</b></para>
    /// </summary>
    [TestFixture]
    public class StationTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave(long money = 0)
        {
            var save = new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = money, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
            save.Warehouse.Capacity = 500;
            return save;
        }

        // ── 업그레이드 ───────────────────────────────────────────

        /// <summary><b>제일 중요하다.</b> 돈을 모으면 작업대가 실제로 올라가야 한다.</summary>
        [Test]
        public void Upgrade_RaisesTheLevel_AndTakesTheMoney()
        {
            var save = NewSave(10_000_000);
            long cost = Station.UpgradeCost(save, _data);

            Assert.IsNull(Station.UpgradeBlockReason(save, _data));
            Assert.IsTrue(Station.TryUpgrade(save, _data));

            Assert.AreEqual(2, save.Factory.StationLevel);
            Assert.AreEqual(10_000_000 - cost, save.Player.Money);
        }

        [Test]
        public void Upgrade_NeedsMoney()
        {
            var save = NewSave(0);

            Assert.IsNotNull(Station.UpgradeBlockReason(save, _data));
            Assert.IsFalse(Station.TryUpgrade(save, _data));
            Assert.AreEqual(1, save.Factory.StationLevel);
        }

        [Test]
        public void Upgrade_StopsAtMaxLevel()
        {
            var save = NewSave(long.MaxValue / 2);

            while (!Station.IsMaxLevel(save, _data))
                Assert.IsTrue(Station.TryUpgrade(save, _data), "최고 단계 전에는 계속 올라가야 한다");

            Assert.AreEqual(Station.MaxLevel(_data), save.Factory.StationLevel);
            Assert.IsFalse(Station.TryUpgrade(save, _data));
            Assert.AreEqual(0, Station.UpgradeCost(save, _data));
        }

        /// <summary>값이 오르지 않으면 마지막 단계가 제일 싸진다. 성장이 아니라 할인이 된다.</summary>
        [Test]
        public void UpgradeCost_RisesWithLevel()
        {
            var save = NewSave(long.MaxValue / 2);
            long previous = 0;

            while (!Station.IsMaxLevel(save, _data))
            {
                long cost = Station.UpgradeCost(save, _data);
                Assert.Greater(cost, previous, $"{save.Factory.StationLevel}단계 값이 이전보다 싸다");
                previous = cost;
                Station.TryUpgrade(save, _data);
            }
        }

        // ── 레벨이 여는 것 ───────────────────────────────────────

        [Test]
        public void Level_OpensQueueSlots()
        {
            var save = NewSave(long.MaxValue / 2);
            Assert.AreEqual(1, FactorySystem.QueueCapacity(save, T0));

            Station.TryUpgrade(save, _data);
            Assert.AreEqual(2, FactorySystem.QueueCapacity(save, T0));
        }

        [Test]
        public void Level_MakesCraftingFaster()
        {
            var save = NewSave(long.MaxValue / 2);
            double before = FactorySystem.SpeedDivisor(save, _data);

            Station.TryUpgrade(save, _data);

            Assert.Less(FactorySystem.SpeedDivisor(save, _data), before, "올렸는데 더 느려지거나 그대로다");
        }

        /// <summary>
        /// 완료됐지만 아직 정리되지 않은 작업이 칸을 잡고 있으면, 레벨을 올려도 칸이 안 늘어난 것처럼 보인다.
        /// </summary>
        [Test]
        public void CollectedJobs_DoNotHoldASlot()
        {
            var save = NewSave();
            save.Factory.Queue.Add(new CraftJob { RecipeId = "RCP_BOLT", Collected = true });

            Assert.AreEqual(0, FactorySystem.ActiveJobs(save));
            Assert.AreEqual(1, save.Factory.Queue.Count, "정리 전이라 목록에는 남아 있다");
        }

        // ── 보조 인력 ────────────────────────────────────────────

        [Test]
        public void Assistants_AreLockedUntilTheStationIsGoodEnough()
        {
            var save = NewSave(long.MaxValue / 2);

            Assert.IsFalse(Station.AssistantsUnlocked(save, _data));
            Assert.IsNotNull(Station.HireAssistantBlockReason(save, _data));
            Assert.IsFalse(Station.TryHireAssistant(save, _data));

            while (!Station.AssistantsUnlocked(save, _data)) Station.TryUpgrade(save, _data);

            Assert.IsNull(Station.HireAssistantBlockReason(save, _data));
            Assert.IsTrue(Station.TryHireAssistant(save, _data));
            Assert.AreEqual(1, save.Factory.AutoLevel);
        }

        /// <summary>사람을 썼는데 만들 것이 없으면 돈만 나가고 아무 변화가 없다.</summary>
        [Test]
        public void HiringTheFirstAssistant_PicksSomethingToMake()
        {
            var save = UnlockedStation();

            Assert.IsTrue(Station.TryHireAssistant(save, _data));
            Assert.IsNotEmpty(save.Factory.AutoRecipeId ?? "",
                "첫 사람을 썼는데 만들 것이 비어 있으면 아무 일도 일어나지 않는다");
        }

        [Test]
        public void Assistants_AreCappedAndGetMoreExpensive()
        {
            var save = UnlockedStation();
            long previous = 0;

            for (int i = 0; i < Station.MaxAssistants(_data); i++)
            {
                long cost = Station.AssistantCost(save, _data);
                Assert.Greater(cost, previous, "사람이 늘수록 비싸져야 한다");
                previous = cost;
                Assert.IsTrue(Station.TryHireAssistant(save, _data));
            }

            Assert.IsFalse(Station.TryHireAssistant(save, _data), "정원을 넘겨서 쓸 수 없어야 한다");
        }

        [Test]
        public void AutoRecipe_RejectsWhatTheStationCannotMake()
        {
            var save = NewSave();

            foreach (var recipe in _data.AllRecipes)
                if (recipe.StationLevel > save.Factory.StationLevel)
                {
                    Assert.IsFalse(Station.TrySetAutoRecipe(save, _data, recipe.Id),
                        "작업대가 못 만드는 걸 시킬 수는 없다");
                    return;
                }
        }

        // ── 없는 동안의 생산 ─────────────────────────────────────

        /// <summary>사람을 썼으면 앱을 꺼 둔 동안에도 물건이 쌓여 있어야 한다.</summary>
        [Test]
        public void Assistants_ProduceWhileYouAreAway()
        {
            var save = UnlockedStation();
            Station.TryHireAssistant(save, _data);
            var recipe = StarterRecipe();
            Station.TrySetAutoRecipe(save, _data, recipe.Id);

            var clock = new TestClock(T0);
            clock.Advance(TimeSpan.FromHours(4));

            var report = Resolve(save, clock);

            Assert.Greater(report.Crafts.Count, 0, "네 시간을 비웠는데 아무것도 안 만들었다");
            Assert.Greater(Warehouse.CountOf(save.Warehouse, recipe.OutputItemId), 0);
        }

        /// <summary>
        /// 오프라인 상한. 무제한이면 "일주일 뒤에 한 번 접속"이 최적 전략이 되어
        /// 매일 접속할 이유가 사라지고, 그러면 일일 의뢰가 만드는 접속 동기를 공장이 무력화한다.
        /// </summary>
        [Test]
        public void OfflineProduction_IsCapped()
        {
            var recipe = StarterRecipe();

            int halfDay = ProducedOver(TimeSpan.FromHours(12), recipe);
            int aWeek = ProducedOver(TimeSpan.FromDays(7), recipe);

            Assert.Greater(halfDay, 0);
            // 주기 경계가 어디에 걸리느냐에 따라 한 번 차이는 날 수 있다. 잡으려는 건
            // 열네 배가 쌓이는 경우다 — 그러면 매일 켤 이유가 없어진다.
            Assert.LessOrEqual(aWeek, halfDay + 1,
                $"일주일({aWeek}) 이 반나절({halfDay}) 보다 많이 쌓였다 — 상한이 안 걸렸다");
        }

        /// <summary>같은 구간을 두 번 정산해도 두 배로 만들어지지 않는다.</summary>
        [Test]
        public void ResolvingTwice_DoesNotDoubleTheWork()
        {
            var save = UnlockedStation();
            Station.TryHireAssistant(save, _data);
            Station.TrySetAutoRecipe(save, _data, StarterRecipe().Id);

            var clock = new TestClock(T0);
            clock.Advance(TimeSpan.FromHours(4));

            int first = Resolve(save, clock).Crafts.Count;
            int second = Resolve(save, clock).Crafts.Count;

            Assert.Greater(first, 0);
            Assert.AreEqual(0, second, "저장 없이 한 번 더 정산하면 같은 시간을 두 번 일한 것이 된다");
        }

        /// <summary>같은 입력은 같은 결과. 껐다 켜서 품질을 다시 굴리는 짓이 성립하면 안 된다.</summary>
        [Test]
        public void SameWindow_ProducesTheSameResult()
        {
            string Fingerprint()
            {
                var save = UnlockedStation();
                Station.TryHireAssistant(save, _data);
                Station.TrySetAutoRecipe(save, _data, StarterRecipe().Id);

                var clock = new TestClock(T0);
                clock.Advance(TimeSpan.FromHours(6));

                var report = Resolve(save, clock);

                var parts = new List<string>();
                foreach (var c in report.Crafts) parts.Add($"{c.RecipeId}:{c.Quality}:{c.Count}");
                return string.Join("|", parts.ToArray());
            }

            Assert.AreEqual(Fingerprint(), Fingerprint());
        }

        /// <summary>재료가 필요한 레시피는 재료가 떨어지면 멈춘다 — 사람을 쓴다고 없는 재료가 생기지 않는다.</summary>
        [Test]
        public void Assistants_StopWhenMaterialsRunOut()
        {
            var save = UnlockedStation();
            Station.TryHireAssistant(save, _data);

            var recipe = RecipeWithInputs();
            Station.TrySetAutoRecipe(save, _data, recipe.Id);

            // 딱 한 번 만들 재료만 준다.
            foreach (var input in recipe.Inputs)
                Warehouse.TryAdd(save.Warehouse, _data, input.ItemId, input.Count);

            var clock = new TestClock(T0);
            clock.Advance(TimeSpan.FromHours(12));

            var report = Resolve(save, clock);

            Assert.AreEqual(1, report.Crafts.Count, "재료 한 벌로 한 번만 만들어야 한다");
            foreach (var input in recipe.Inputs)
                Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, input.ItemId));
        }

        [Test]
        public void NoAssistants_MeansNothingHappens()
        {
            var save = UnlockedStation();
            Station.TrySetAutoRecipe(save, _data, StarterRecipe().Id);   // 시켜만 두고 사람은 없다

            var clock = new TestClock(T0);
            clock.Advance(TimeSpan.FromHours(8));

            Assert.AreEqual(0, Resolve(save, clock).Crafts.Count);
        }

        /// <summary>
        /// 보조 인력의 품질은 사람 손보다 아래여야 한다. 자동이 직접 하는 것보다 잘하면
        /// 미니게임이 장식이 되고, 이 게임에서 유일하게 손으로 하는 일이 사라진다.
        /// </summary>
        [Test]
        public void AssistantQuality_StaysBelowASteadyHand()
        {
            var weights = _data.Balance.Assistant.QualityWeights;

            int total = 0, weighted = 0;
            for (int i = 0; i < weights.Length; i++) { total += weights[i]; weighted += weights[i] * i; }
            double assistantAverage = (double)weighted / total;

            Assert.Less(assistantAverage, (double)(int)CraftQuality.Good,
                "보조 인력 평균이 '양호' 이상이면 손으로 할 이유가 없다");
        }

        // ── 도우미 ───────────────────────────────────────────────

        private GameSave UnlockedStation()
        {
            var save = NewSave(long.MaxValue / 2);
            while (!Station.AssistantsUnlocked(save, _data)) Station.TryUpgrade(save, _data);
            return save;
        }

        private RecipeDef StarterRecipe()
        {
            foreach (var r in _data.AllRecipes)
                if (r.ManualSteps > 0 && r.Inputs.Length == 0 && r.StationLevel == 1) return r;

            Assert.Fail("재료 없는 레시피가 없다");
            return null;
        }

        private RecipeDef RecipeWithInputs()
        {
            foreach (var r in _data.AllRecipes)
                if (r.Inputs.Length > 0 && r.StationLevel == 1) return r;

            Assert.Fail("재료가 필요한 레시피가 없다");
            return null;
        }

        private ResolveReport Resolve(GameSave save, TestClock clock) =>
            new OfflineResolver(clock, new List<ITimelineSystem> { new AssistantSystem() })
                .Resolve(save, _data);

        private int ProducedOver(TimeSpan away, RecipeDef recipe)
        {
            var save = UnlockedStation();
            Station.TryHireAssistant(save, _data);
            Station.TrySetAutoRecipe(save, _data, recipe.Id);

            var clock = new TestClock(T0);
            clock.Advance(away);

            return Resolve(save, clock).Crafts.Count;
        }
    }
}
