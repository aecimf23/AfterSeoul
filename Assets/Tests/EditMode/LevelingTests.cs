using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 레벨.
    ///
    /// <para>레벨이 안 오르면 게임 전체가 1티어에 갇힌다 — 구로·의정부가 안 열리고,
    /// 일일 의뢰도 고용 시장도 티어 1 에서 멈춘다. 실제로 그 상태였다.</para>
    ///
    /// <para>핵심 성질은 <b>레벨이 경험치의 함수</b>라는 것이다. 정산이 두 번 돌아도
    /// 두 번 오르지 않는다.</para>
    /// </summary>
    [TestFixture]
    public class LevelingTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;
        private BalanceDef B => _data.Balance;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave(long money = 10_000_000)
        {
            return new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = money, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
        }

        // ── 곡선 ─────────────────────────────────────────────────

        [Test]
        public void Level1_CostsNothing()
        {
            Assert.AreEqual(0, Leveling.ExpForLevel(1, B));
            Assert.AreEqual(1, Leveling.LevelForExp(0, B));
            Assert.AreEqual(1, Leveling.LevelForExp(-5, B), "음수 경험치에도 안 죽는다");
        }

        [Test]
        public void Curve_IsStrictlyIncreasing()
        {
            long prev = -1;
            for (int n = 1; n <= B.LevelCurve.MaxLevel; n++)
            {
                long need = Leveling.ExpForLevel(n, B);
                Assert.Greater(need, prev, $"Lv{n} 이 Lv{n - 1} 보다 싸면 안 된다");
                prev = need;
            }
        }

        [Test]
        public void LevelForExp_MatchesTheBoundaries()
        {
            for (int n = 2; n <= 12; n++)
            {
                long need = Leveling.ExpForLevel(n, B);
                Assert.AreEqual(n - 1, Leveling.LevelForExp(need - 1, B), $"Lv{n} 경계 직전");
                Assert.AreEqual(n, Leveling.LevelForExp(need, B), $"Lv{n} 경계 정확히");
            }
        }

        [Test]
        public void MaxLevel_IsRespected()
        {
            int max = B.LevelCurve.MaxLevel;
            Assert.AreEqual(max, Leveling.LevelForExp(long.MaxValue / 2, B));
            Assert.AreEqual(0, Leveling.ExpToNextLevel(long.MaxValue / 2, B), "상한이면 남은 경험치가 없다");
            Assert.AreEqual(1.0, Leveling.ProgressInLevel(long.MaxValue / 2, B), 1e-9);
        }

        [Test]
        public void Progress_MovesFromZeroToOneInsideALevel()
        {
            long from = Leveling.ExpForLevel(3, B);
            long to = Leveling.ExpForLevel(4, B);

            Assert.AreEqual(0.0, Leveling.ProgressInLevel(from, B), 1e-9);
            Assert.AreEqual(0.5, Leveling.ProgressInLevel((from + to) / 2, B), 0.01);
            Assert.Less(Leveling.ProgressInLevel(to - 1, B), 1.0);
        }

        // ── 멱등성 ───────────────────────────────────────────────

        /// <summary>같은 경험치로 몇 번을 불러도 레벨이 한 번만 오른다.</summary>
        [Test]
        public void Sync_IsIdempotent()
        {
            var save = NewSave();
            save.Player.Exp = Leveling.ExpForLevel(4, B);

            Assert.AreEqual(3, Leveling.Sync(save, B), "1 → 4 이므로 3 올랐다");
            Assert.AreEqual(4, save.Player.Level);

            Assert.AreEqual(0, Leveling.Sync(save, B), "두 번째부터는 오르지 않는다");
            Assert.AreEqual(0, Leveling.Sync(save, B));
            Assert.AreEqual(4, save.Player.Level);
        }

        /// <summary>레벨이 내려가지 않는다. 이미 열린 지역이 다시 잠기면 버그로만 읽힌다.</summary>
        [Test]
        public void Sync_NeverGoesDown()
        {
            var save = NewSave();
            save.Player.Level = 7;
            save.Player.Exp = 0;

            Assert.AreEqual(0, Leveling.Sync(save, B));
            Assert.AreEqual(7, save.Player.Level);
        }

        // ── 경험치가 실제로 들어오는가 ────────────────────────────

        /// <summary>작업대에서 물건을 완성하면 경험치가 들어온다.</summary>
        [Test]
        public void Workbench_GivesExpOnCompletion()
        {
            var save = NewSave();
            save.Warehouse.Capacity = 50;
            Assert.IsTrue(AfterSeoul.Factory.Workbench.TryStart(save, _data, "RCP_SALVAGE"));

            var recipe = _data.GetRecipe("RCP_SALVAGE");
            for (int i = 0; i < recipe.ManualSteps - 1; i++)
            {
                AfterSeoul.Factory.Workbench.Advance(save, _data, 1.0);
                Assert.AreEqual(0, save.Player.Exp, "완성 전에는 경험치가 없다");
            }

            var result = AfterSeoul.Factory.Workbench.Advance(save, _data, 1.0);
            Assert.IsTrue(result.Completed);
            Assert.Greater(save.Player.Exp, 0, "완성하면 경험치가 들어와야 한다");
        }

        /// <summary>
        /// 파견만으로도 경험치가 들어온다. 의뢰가 유일한 성장 경로면
        /// 의뢰가 안 맞는 날은 아무것도 안 열린다.
        ///
        /// <para>한 번만 보고 판단하지 않는다 — 운 나쁘면 싼 것 한 개만 주워 와서
        /// 경험치가 0 이 될 수 있고, 그러면 가끔 실패하는 테스트가 된다.</para>
        /// </summary>
        [Test]
        public void Expedition_GivesExpFromLootValue()
        {
            long total = 0;

            for (int i = 0; i < 5; i++)
            {
                var clock = new TestClock(T0);
                var save = NewSave();
                save.Warehouse.Capacity = 400;
                save.RngCounter = (uint)(i * 2 + 1);

                var scav = new ScavState
                {
                    Uid = "sc_1", Name = "테스트", Search = 8, Combat = 6, Survival = 9,
                    WagePerHour = 40000, Status = ScavStatus.Idle,
                };
                scav.Equipment[EquipSlot.Weapon] = "MEL01";
                save.Scavs.Add(scav);

                new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { scav.Uid }, T0);
                clock.Advance(TimeSpan.FromHours(6));
                new OfflineResolver(clock, new List<ITimelineSystem> { new ExpeditionSystem() })
                    .Resolve(save, _data);

                total += save.Player.Exp;
            }

            Assert.Greater(total, 0, "다섯 번을 보내도 경험치가 0 이면 경로가 끊긴 것이다");
        }

        // ── 세션 ─────────────────────────────────────────────────

        /// <summary>정산 중에 오른 레벨이 복귀 보고에 실린다.</summary>
        [Test]
        public void Session_ReportsLevelUp()
        {
            var clock = new TestClock(T0);
            var session = new GameSession(
                new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();

            // 정산 직전에 레벨 3 어치 경험치를 넣어 둔다.
            session.Save.Player.Exp = Leveling.ExpForLevel(3, B);

            clock.Advance(TimeSpan.FromMinutes(1));
            var report = session.Tick();

            Assert.AreEqual(2, report.LevelsGained);
            Assert.AreEqual(3, report.NewLevel);
            Assert.AreEqual(3, session.Save.Player.Level);
            Assert.IsFalse(report.IsEmpty, "레벨업만 있어도 보고할 것이 있다");
        }

        /// <summary>조작(노동) 중에 오른 레벨은 화면이 가져갈 수 있게 쌓인다.</summary>
        [Test]
        public void Session_QueuesLevelUpFromAnAction()
        {
            var clock = new TestClock(T0);
            var session = new GameSession(
                new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();
            session.ConsumeLevelUps();   // 부팅 중의 것을 비운다

            session.Save.Player.Exp = Leveling.ExpForLevel(2, B) - 1;

            // 작업대에서 하나 완성하면 경험치가 들어온다.
            Assert.IsTrue(session.StartWork("RCP_SALVAGE"));
            var recipe = _data.GetRecipe("RCP_SALVAGE");
            for (int i = 0; i < recipe.ManualSteps; i++) session.AdvanceWork(1.0);

            Assert.AreEqual(2, session.Save.Player.Level);
            Assert.AreEqual(1, session.ConsumeLevelUps());
            Assert.AreEqual(0, session.ConsumeLevelUps(), "가져가면 비워진다");
        }

        [Test]
        public void Level_SurvivesRestart()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var codec = new NewtonsoftJsonCodec();

            var session = new GameSession(new SaveService(files, codec, clock), _data, clock);
            session.Boot();
            session.Save.Player.Exp = Leveling.ExpForLevel(6, B);
            session.Commit();

            var reopened = new GameSession(new SaveService(files, codec, clock), _data, clock);
            reopened.Boot();

            Assert.AreEqual(6, reopened.Save.Player.Level);
        }

        // ── 레벨이 실제로 문을 여는가 ─────────────────────────────

        /// <summary>
        /// 레벨 경계가 실제로 문을 여는가. 레벨이 막고 있는 것 전부를 한 번에 훑는다.
        /// </summary>
        [Test]
        public void LevelGates_OpenAtTheirBoundaries()
        {
            var save = NewSave();
            var uijeongbu = _data.GetMap("UIJEONGBU");   // playerLevel 9

            save.Player.Level = 4;
            Assert.AreEqual(1, ScavMarket.MaxTierFor(save), "레벨 4 에서는 고용 티어 1");

            save.Player.Level = 5;
            Assert.AreEqual(2, ScavMarket.MaxTierFor(save), "레벨 5 에서 고용 티어 2");

            save.Player.Level = 10;
            Assert.AreEqual(3, ScavMarket.MaxTierFor(save), "레벨 10 에서 고용 티어 3");

            save.Player.Level = 8;
            Assert.IsFalse(MapUnlock.IsUnlocked(save, uijeongbu));
            save.Player.Level = 9;
            Assert.IsTrue(MapUnlock.IsUnlocked(save, uijeongbu), "레벨 9 에서 의정부가 열려야 한다");
        }

        /// <summary>
        /// 경험치를 모으면 실제로 레벨 9 까지 간다 — 곡선이 상한에 갇혀 있지 않은가.
        /// </summary>
        [Test]
        public void EnoughExp_ActuallyReachesTheUnlockLevels()
        {
            var save = NewSave();

            foreach (int target in new[] { 5, 9, 10 })
            {
                save.Player.Exp = Leveling.ExpForLevel(target, B);
                Leveling.Sync(save, B);
                Assert.AreEqual(target, save.Player.Level, $"Lv{target} 에 도달하지 못한다");
            }
        }
    }
}
