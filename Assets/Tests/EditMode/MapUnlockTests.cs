using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 지역 해금과 파견 차단.
    ///
    /// <para>이 파일이 지키려는 것 하나: <b>화면이 "보낼 수 있다"고 그리는 근거와
    /// <see cref="ExpeditionSystem.Depart"/> 가 실제로 허용하는 조건이 같은 함수다.</b>
    /// 둘이 갈라지면 눌리는데 아무 일도 안 일어나는 버튼이 생기고, 그건 로그도 안 남는다.</para>
    /// </summary>
    [TestFixture]
    public class MapUnlockTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

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

        private static ScavState AddIdleScav(GameSave save, string uid = "sc_test")
        {
            var s = new ScavState
            {
                Uid = uid, Name = "테스트", Level = 1, Tier = 1,
                Search = 5, Combat = 4, Survival = 4,
                Status = ScavStatus.Idle, HiredAt = T0,
            };
            s.Equipment[EquipSlot.Weapon] = "MEL01";
            save.Scavs.Add(s);
            return s;
        }

        // ── 해금 판정 ───────────────────────────────────────────

        [Test]
        public void DefaultCondition_IsOpenFromTheStart()
        {
            var save = NewSave();
            Assert.IsTrue(MapUnlock.IsUnlocked(save, _data.GetMap("MYEONGDONG")),
                "조건이 없는 지역은 처음부터 열려 있어야 한다");
            Assert.IsNull(MapUnlock.LockReason(save, _data.GetMap("MYEONGDONG")));
        }

        [Test]
        public void PlayerLevel_LocksUntilLevelReached()
        {
            var save = NewSave();
            var map = _data.GetMap("UIJEONGBU");   // playerLevel 9

            save.Player.Level = 8;
            Assert.IsFalse(MapUnlock.IsUnlocked(save, map));
            StringAssert.Contains("9", MapUnlock.LockReason(save, map),
                "사유에 필요한 레벨이 적혀 있어야 한다");

            save.Player.Level = 9;
            Assert.IsTrue(MapUnlock.IsUnlocked(save, map), "경계값에서 열려야 한다");
        }

        [Test]
        public void NpcTrust_LocksUntilTrustReached()
        {
            var save = NewSave();
            var map = _data.GetMap("YONGSAN_MARKET");   // HWANG 신뢰도 10

            Assert.IsFalse(MapUnlock.IsUnlocked(save, map),
                "신뢰도 기록이 아예 없으면 0 으로 보고 잠겨야 한다");

            save.NpcTrust["HWANG"] = 9;
            Assert.IsFalse(MapUnlock.IsUnlocked(save, map));

            save.NpcTrust["HWANG"] = 10;
            Assert.IsTrue(MapUnlock.IsUnlocked(save, map));
        }

        [Test]
        public void NpcTrust_IgnoresOtherNpcs()
        {
            var save = NewSave();
            save.NpcTrust["SOMEONE_ELSE"] = 999;

            Assert.IsFalse(MapUnlock.IsUnlocked(save, _data.GetMap("YONGSAN_MARKET")),
                "다른 NPC 의 신뢰도로 열리면 안 된다");
        }

        [Test]
        public void UnknownConditionType_StaysLocked()
        {
            var save = NewSave();
            var map = _data.GetMap("BROKEN_UNLOCK");

            Assert.IsFalse(MapUnlock.IsUnlocked(save, map),
                "모르는 조건 타입은 잠긴 것으로 봐야 한다 — 조용히 열리는 쪽이 더 나쁘다");
            Assert.IsNotNull(MapUnlock.LockReason(save, map));
        }

        [Test]
        public void NullMap_IsLockedNotCrash()
        {
            Assert.IsFalse(MapUnlock.IsUnlocked(NewSave(), null));
        }

        // ── 파견 차단 사유 ───────────────────────────────────────

        [Test]
        public void BlockReason_NullWhenEverythingIsFine()
        {
            var save = NewSave();
            var scav = AddIdleScav(save);

            Assert.IsNull(
                ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", new[] { scav.Uid }));
        }

        [Test]
        public void BlockReason_NoTeam()
        {
            var save = NewSave();
            AddIdleScav(save);

            Assert.IsNotNull(
                ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", new string[0]));
            Assert.IsNotNull(
                ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", null));
        }

        [Test]
        public void BlockReason_UnknownMap()
        {
            var save = NewSave();
            var scav = AddIdleScav(save);

            Assert.IsNotNull(
                ExpeditionSystem.DepartBlockReason(save, _data, "NO_SUCH_MAP", new[] { scav.Uid }));
        }

        [Test]
        public void BlockReason_NotEnoughMoney()
        {
            var save = NewSave(money: 1000);
            var scav = AddIdleScav(save);

            var reason = ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", new[] { scav.Uid });
            Assert.IsNotNull(reason);
            StringAssert.Contains("자금", reason);
        }

        [Test]
        public void BlockReason_ScavAlreadyBusy()
        {
            var save = NewSave();
            var scav = AddIdleScav(save);
            scav.Status = ScavStatus.OnExpedition;

            var reason = ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", new[] { scav.Uid });
            Assert.IsNotNull(reason);
            StringAssert.Contains(scav.Name, reason, "누가 문제인지 이름이 나와야 한다");
        }

        [Test]
        public void BlockReason_LockedMap()
        {
            var save = NewSave();
            var scav = AddIdleScav(save);
            save.Player.Level = 1;

            Assert.IsNotNull(
                ExpeditionSystem.DepartBlockReason(save, _data, "UIJEONGBU", new[] { scav.Uid }),
                "잠긴 지역은 돈이 넉넉해도 막혀야 한다");
        }

        // ── 판정과 실행이 같은 근거를 쓰는가 ──────────────────────

        [Test]
        public void Depart_RefusesLockedMap_AndChangesNothing()
        {
            var save = NewSave();
            var scav = AddIdleScav(save);
            long moneyBefore = save.Player.Money;
            uint counterBefore = save.RngCounter;

            var exp = new ExpeditionSystem().Depart(save, _data, "UIJEONGBU", new[] { scav.Uid }, T0);

            Assert.IsNull(exp);
            Assert.AreEqual(moneyBefore, save.Player.Money, "막힌 파견이 돈을 가져가면 안 된다");
            Assert.AreEqual(counterBefore, save.RngCounter, "시드도 소비하면 안 된다");
            Assert.AreEqual(ScavStatus.Idle, scav.Status);
            Assert.AreEqual(0, save.Expeditions.Count);
            Assert.AreEqual(0, scav.ExpeditionCount);
        }

        [Test]
        public void Depart_SucceedsOnceUnlocked()
        {
            var save = NewSave();
            var scav = AddIdleScav(save);
            save.Player.Level = 9;

            var exp = new ExpeditionSystem().Depart(save, _data, "UIJEONGBU", new[] { scav.Uid }, T0);

            Assert.IsNotNull(exp);
            Assert.AreEqual(ScavStatus.OnExpedition, scav.Status);
            Assert.AreEqual(150_000, exp.CostPaid);
        }

        /// <summary>
        /// 화면이 버튼을 살리는 조건(<c>DepartBlockReason == null</c>)과
        /// 실제 출발이 허용되는 조건이 어긋나지 않는지 — 여러 상황을 한 번에 훑는다.
        /// </summary>
        [Test]
        public void BlockReason_AgreesWithDepart()
        {
            string[] maps = { "MYEONGDONG", "GURO_FACTORY", "UIJEONGBU", "YONGSAN_MARKET", "BROKEN_UNLOCK" };
            long[] monies = { 0, 30_000, 200_000, 10_000_000 };
            int[] levels = { 1, 9 };

            foreach (var mapId in maps)
            foreach (var money in monies)
            foreach (var level in levels)
            {
                var save = NewSave(money);
                save.Player.Level = level;
                var scav = AddIdleScav(save);
                var team = new List<string> { scav.Uid };

                bool allowed = ExpeditionSystem.DepartBlockReason(save, _data, mapId, team) == null;
                var exp = new ExpeditionSystem().Depart(save, _data, mapId, team, T0);

                Assert.AreEqual(allowed, exp != null,
                    $"판정과 실행이 달랐다: map={mapId} money={money} level={level}");
            }
        }
    }
}
