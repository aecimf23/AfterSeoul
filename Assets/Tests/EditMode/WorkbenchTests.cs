using System;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 작업대 — 손으로 물건 만들기.
    ///
    /// <para>미니게임이 "돈 버는 탭"이 아니라 "물건 만드는 탭"이 됐는지 본다.
    /// 두드려도 아무것도 쌓이지 않으면 그건 놀이가 아니라 노동이다.</para>
    /// </summary>
    [TestFixture]
    public class WorkbenchTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave()
        {
            var save = new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
            save.Warehouse.Capacity = 100;
            return save;
        }

        /// <summary>재료 없이 만들 수 있는 레시피. 빈손 플레이어의 입구.</summary>
        private RecipeDef Starter()
        {
            foreach (var r in _data.AllRecipes)
                if (r.ManualSteps > 0 && r.Inputs.Length == 0) return r;

            Assert.Fail("재료 없는 레시피가 없다");
            return null;
        }

        private RecipeDef WithInputs()
        {
            foreach (var r in _data.AllRecipes)
                if (r.ManualSteps > 0 && r.Inputs.Length > 0) return r;

            Assert.Fail("재료가 필요한 레시피가 없다");
            return null;
        }

        // ── 빈손으로 시작할 수 있는가 ─────────────────────────────

        /// <summary>
        /// <b>이게 제일 중요하다.</b> 아무것도 없는 플레이어가 시작할 수 있어야 한다.
        /// 모든 레시피가 재료를 요구하면 게임이 첫 화면에서 끝난다.
        /// </summary>
        [Test]
        public void EmptyHandedPlayer_CanStartSomething()
        {
            var save = NewSave();
            var options = Workbench.AvailableRecipes(save, _data);

            Assert.Greater(options.Count, 0, "작업대에서 만들 수 있는 게 하나도 없다");
            Assert.IsNull(Workbench.StartBlockReason(save, _data, options[0].Id),
                "목록 첫 줄은 빈손으로도 시작할 수 있어야 한다 (재료 없는 것이 먼저 온다)");
        }

        // ── 공정 ─────────────────────────────────────────────────

        [Test]
        public void EachStrike_AdvancesOneStep()
        {
            var save = NewSave();
            var recipe = Starter();
            Assert.IsTrue(Workbench.TryStart(save, _data, recipe.Id));

            for (int i = 1; i < recipe.ManualSteps; i++)
            {
                var step = Workbench.Advance(save, _data, 1.0);
                Assert.IsFalse(step.Completed, $"{i}단계에서 완성되면 안 된다");
                Assert.AreEqual(i, step.StepsDone);
                Assert.AreEqual(recipe.ManualSteps, step.StepsTotal);
                Assert.AreEqual(i, save.Factory.Workbench.StepsDone);
            }

            var last = Workbench.Advance(save, _data, 1.0);
            Assert.IsTrue(last.Completed, "마지막 단계에서 완성돼야 한다");
            Assert.IsTrue(save.Factory.Workbench.IsIdle, "완성했으면 작업대가 비어야 한다");
        }

        [Test]
        public void CompletedItem_GoesIntoTheWarehouse()
        {
            var save = NewSave();
            var recipe = Starter();
            Workbench.TryStart(save, _data, recipe.Id);

            WorkStepResult result = default;
            for (int i = 0; i < recipe.ManualSteps; i++) result = Workbench.Advance(save, _data, 1.0);

            Assert.IsTrue(result.Completed);
            Assert.AreEqual(recipe.OutputItemId, result.Output.ItemId);
            Assert.AreEqual(result.Output.Count,
                Warehouse.CountOf(save.Warehouse, recipe.OutputItemId),
                "만든 것이 창고에 들어가야 한다");
        }

        /// <summary>품질이 산출 개수를 바꾼다 — 보수 배수가 아니라 눈에 보이는 결과다.</summary>
        [Test]
        public void Quality_ChangesTheAmountProduced()
        {
            var recipe = Starter();

            int fail = Workbench.OutputCountFor(_data, recipe, CraftQuality.Failed);
            int normal = Workbench.OutputCountFor(_data, recipe, CraftQuality.Normal);
            int good = Workbench.OutputCountFor(_data, recipe, CraftQuality.Good);
            int best = Workbench.OutputCountFor(_data, recipe, CraftQuality.Excellent);

            Assert.AreEqual(recipe.OutputCount, good, "'양호' 가 레시피 기준이다");
            Assert.LessOrEqual(fail, normal);
            Assert.LessOrEqual(normal, good);
            Assert.Less(good, best, "우수는 확실히 더 나와야 한다");
            Assert.GreaterOrEqual(fail, 1, "다 두드렸는데 0 개면 벌이 아니라 고장으로 읽힌다");
        }

        [Test]
        public void PerfectRun_ProducesMoreThanASloppyOne()
        {
            var recipe = Starter();

            var sloppy = NewSave();
            Workbench.TryStart(sloppy, _data, recipe.Id);
            WorkStepResult low = default;
            for (int i = 0; i < recipe.ManualSteps; i++) low = Workbench.Advance(sloppy, _data, 0.0);

            var sharp = NewSave();
            Workbench.TryStart(sharp, _data, recipe.Id);
            WorkStepResult high = default;
            for (int i = 0; i < recipe.ManualSteps; i++) high = Workbench.Advance(sharp, _data, 1.0);

            Assert.Greater(high.Output.Count, low.Output.Count,
                "잘하면 더 많이 나와야 한다 — 안 그러면 미니게임을 할 이유가 없다");
        }

        // ── 재료 ─────────────────────────────────────────────────

        [Test]
        public void Inputs_AreTakenAtStart()
        {
            var save = NewSave();
            var recipe = WithInputs();
            var input = recipe.Inputs[0];

            Assert.IsNotNull(Workbench.StartBlockReason(save, _data, recipe.Id), "재료가 없으면 막힌다");

            Warehouse.TryAdd(save.Warehouse, _data, input.ItemId, input.Count);
            Assert.IsTrue(Workbench.TryStart(save, _data, recipe.Id));
            Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, input.ItemId), "시작할 때 차감된다");
        }

        /// <summary>취소하면 재료를 돌려준다. 잘못 눌렀다고 재료를 태우면 고르는 것 자체가 무서워진다.</summary>
        [Test]
        public void Cancel_RefundsInputs()
        {
            var save = NewSave();
            var recipe = WithInputs();
            var input = recipe.Inputs[0];
            Warehouse.TryAdd(save.Warehouse, _data, input.ItemId, input.Count);

            Workbench.TryStart(save, _data, recipe.Id);
            Workbench.Advance(save, _data, 1.0);   // 한 단계 해놓고 접는다

            Assert.IsTrue(Workbench.Cancel(save, _data));
            Assert.AreEqual(input.Count, Warehouse.CountOf(save.Warehouse, input.ItemId));
            Assert.IsTrue(save.Factory.Workbench.IsIdle);
        }

        [Test]
        public void CannotStartTwo()
        {
            var save = NewSave();
            var recipe = Starter();

            Assert.IsTrue(Workbench.TryStart(save, _data, recipe.Id));
            Assert.IsFalse(Workbench.TryStart(save, _data, recipe.Id), "이미 작업 중이면 못 시작한다");
        }

        [Test]
        public void AdvanceWithNothingStarted_DoesNothing()
        {
            var save = NewSave();
            var result = Workbench.Advance(save, _data, 1.0);

            Assert.IsFalse(result.Completed);
            Assert.AreEqual(0, result.StepsDone);
            Assert.AreEqual(0, save.Warehouse.Stacks.Count);
        }

        // ── 세션 ─────────────────────────────────────────────────

        /// <summary>두 단계 해놓고 앱을 껐다 켜면 이어서 한다. 처음부터 다시가 제일 억울하다.</summary>
        [Test]
        public void PartialWork_SurvivesRestart()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var codec = new NewtonsoftJsonCodec();
            var recipe = Starter();

            var session = new GameSession(new SaveService(files, codec, clock), _data, clock);
            session.Boot();

            Assert.IsTrue(session.StartWork(recipe.Id));
            session.AdvanceWork(1.0);
            session.AdvanceWork(0.8);
            session.Suspend();

            var reopened = new GameSession(new SaveService(files, codec, clock), _data, clock);
            reopened.Boot();

            var bench = reopened.Save.Factory.Workbench;
            Assert.AreEqual(recipe.Id, bench.RecipeId);
            Assert.AreEqual(2, bench.StepsDone, "해둔 단계가 남아야 한다");
            Assert.AreEqual(2, bench.Scores.Count, "단계별 성적도 남아야 한다");

            // 이어서 끝낸다.
            WorkStepResult result = default;
            for (int i = bench.StepsDone; i < recipe.ManualSteps; i++)
                result = reopened.AdvanceWork(1.0);

            Assert.IsTrue(result.Completed);
        }

        /// <summary>작업대가 만든 것을 팔아서 돈이 된다 — 새 경제의 한 바퀴.</summary>
        [Test]
        public void Session_MakeThenSell()
        {
            var clock = new TestClock(T0);
            var session = new GameSession(
                new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();

            Assert.AreEqual(0, session.Save.Player.Money);

            var recipe = Starter();
            session.StartWork(recipe.Id);

            WorkStepResult result = default;
            for (int i = 0; i < recipe.ManualSteps; i++) result = session.AdvanceWork(1.0);

            Assert.IsTrue(result.Completed);
            Assert.IsTrue(session.Sell(result.Output.ItemId, result.Output.Count));
            Assert.Greater(session.Save.Player.Money, 0, "만든 것을 팔면 돈이 된다");
        }
    }
}
