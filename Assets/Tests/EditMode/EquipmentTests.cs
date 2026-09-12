using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Inventory;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 장비 (GDD §7).
    ///
    /// <para>지키려는 것 셋: <b>복제되지 않는다</b>(창고와 스캐브 사이를 오갈 뿐),
    /// <b>무기가 없으면 못 나간다</b>, <b>좋은 장비가 실제로 더 좋다</b>.
    /// 마지막 것은 눈으로 확인할 수 없으니 여러 번 돌려서 통계로 본다.</para>
    /// </summary>
    [TestFixture]
    public class EquipmentTests
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

        private static ScavState Add(GameSave save, string uid = "sc_1", int survival = 4)
        {
            var s = new ScavState
            {
                Uid = uid, Name = uid, Level = 1, Tier = 1,
                Search = 5, Combat = 4, Survival = survival,
                WagePerHour = 40000, Status = ScavStatus.Idle, HiredAt = T0,
            };
            save.Scavs.Add(s);
            return s;
        }

        private void Stock(GameSave save, params string[] itemIds)
        {
            foreach (var id in itemIds)
                Assert.AreEqual(0, Warehouse.TryAdd(save.Warehouse, _data, id, 1), $"{id} 입고 실패");
        }

        private static OfflineResolver NewResolver(TestClock clock) =>
            new OfflineResolver(clock, new List<ITimelineSystem> { new ExpeditionSystem() });

        // ── 지급과 해제 ──────────────────────────────────────────

        [Test]
        public void Equip_MovesItemOutOfWarehouse()
        {
            var save = NewSave();
            var scav = Add(save);
            Stock(save, "WPN01");

            Assert.IsTrue(Equipment.TryEquip(save, _data, scav.Uid, "WPN01"));

            Assert.AreEqual("WPN01", scav.Equipment[EquipSlot.Weapon]);
            Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, "WPN01"),
                "지급했으면 창고에서 빠져야 한다 — 남아 있으면 복제다");
        }

        [Test]
        public void Unequip_ReturnsItemToWarehouse()
        {
            var save = NewSave();
            var scav = Add(save);
            Stock(save, "WPN01");
            Equipment.TryEquip(save, _data, scav.Uid, "WPN01");

            Assert.IsTrue(Equipment.TryUnequip(save, _data, scav.Uid, EquipSlot.Weapon));

            Assert.IsFalse(scav.Equipment.ContainsKey(EquipSlot.Weapon));
            Assert.AreEqual(1, Warehouse.CountOf(save.Warehouse, "WPN01"));
        }

        /// <summary>같은 물건을 두 명에게 입힐 수 없다. 되면 장비를 살 이유가 사라진다.</summary>
        [Test]
        public void Equip_CannotArmTwoScavsWithOneItem()
        {
            var save = NewSave();
            var a = Add(save, "sc_a");
            var b = Add(save, "sc_b");
            Stock(save, "WPN01");

            Assert.IsTrue(Equipment.TryEquip(save, _data, a.Uid, "WPN01"));
            Assert.IsFalse(Equipment.TryEquip(save, _data, b.Uid, "WPN01"),
                "창고에 없는 물건을 지급할 수 없어야 한다");
            Assert.AreEqual(0, b.Equipment.Count);
        }

        /// <summary>같은 칸을 덮어쓰면 쓰던 것이 창고로 돌아온다. 증발하면 손실이다.</summary>
        [Test]
        public void Equip_SwappingReturnsThePreviousItem()
        {
            var save = NewSave();
            var scav = Add(save);
            Stock(save, "WPN01", "WPN22");

            Equipment.TryEquip(save, _data, scav.Uid, "WPN01");
            Assert.IsTrue(Equipment.TryEquip(save, _data, scav.Uid, "WPN22"));

            Assert.AreEqual("WPN22", scav.Equipment[EquipSlot.Weapon]);
            Assert.AreEqual(1, Warehouse.CountOf(save.Warehouse, "WPN01"), "쓰던 무기가 창고로 돌아와야 한다");
            Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, "WPN22"));
        }

        [Test]
        public void Equip_RefusesWrongSlotAndUnknownItem()
        {
            var save = NewSave();
            var scav = Add(save);
            Stock(save, "JUNK03");

            Assert.IsFalse(Equipment.TryEquip(save, _data, scav.Uid, "JUNK03"),
                "볼트는 장비가 아니다");
            Assert.IsFalse(Equipment.TryEquip(save, _data, scav.Uid, "NO_SUCH_ITEM"));
            Assert.AreEqual(1, Warehouse.CountOf(save.Warehouse, "JUNK03"), "실패했으면 창고가 그대로여야 한다");
        }

        [Test]
        public void Equip_RefusedWhileOnExpedition()
        {
            var save = NewSave();
            var scav = Add(save);
            Stock(save, "WPN01");
            scav.Status = ScavStatus.OnExpedition;

            Assert.IsFalse(Equipment.TryEquip(save, _data, scav.Uid, "WPN01"));
            Assert.AreEqual(1, Warehouse.CountOf(save.Warehouse, "WPN01"));
        }

        /// <summary>창고가 꽉 차면 벗기지 않는다. 벗겨 놓고 버리면 그게 더 나쁘다.</summary>
        [Test]
        public void Unequip_RefusedWhenWarehouseIsFull()
        {
            var save = NewSave();
            var scav = Add(save);
            Stock(save, "WPN01");
            Equipment.TryEquip(save, _data, scav.Uid, "WPN01");

            save.Warehouse.Capacity = 0;

            Assert.IsFalse(Equipment.TryUnequip(save, _data, scav.Uid, EquipSlot.Weapon));
            Assert.AreEqual("WPN01", scav.Equipment[EquipSlot.Weapon], "실패했으면 여전히 착용 중이어야 한다");
        }

        // ── 무기 필수 ────────────────────────────────────────────

        [Test]
        public void Depart_BlockedWithoutAWeapon()
        {
            var save = NewSave();
            var scav = Add(save);

            var reason = ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", new[] { scav.Uid });
            Assert.IsNotNull(reason);
            StringAssert.Contains("무기", reason);
            Assert.IsNull(new ExpeditionSystem().Depart(save, _data, "MYEONGDONG", new[] { scav.Uid }, T0));
        }

        /// <summary>단검 하나만 들어도 나갈 수 있다 — 싸게 보내는 선택지가 있어야 한다.</summary>
        [Test]
        public void Depart_AllowedWithOnlyAKnife()
        {
            var save = NewSave();
            var scav = Add(save);
            Stock(save, "MEL01");
            Equipment.TryEquip(save, _data, scav.Uid, "MEL01");

            Assert.IsNull(ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", new[] { scav.Uid }));
            Assert.IsNotNull(new ExpeditionSystem().Depart(save, _data, "MYEONGDONG", new[] { scav.Uid }, T0));
        }

        [Test]
        public void Depart_BlockedWhenOnlySomeOfTheTeamIsArmed()
        {
            var save = NewSave();
            var armed = Add(save, "sc_a");
            var bare = Add(save, "sc_b");
            Stock(save, "WPN01");
            Equipment.TryEquip(save, _data, armed.Uid, "WPN01");

            var reason = ExpeditionSystem.DepartBlockReason(
                save, _data, "MYEONGDONG", new[] { armed.Uid, bare.Uid });
            Assert.IsNotNull(reason);
            StringAssert.Contains(bare.Name, reason, "누가 맨손인지 나와야 한다");
        }

        // ── 효과 ─────────────────────────────────────────────────

        [Test]
        public void Effects_ScaleWithGearQuality()
        {
            var save = NewSave();
            var poor = Add(save, "sc_poor");
            var rich = Add(save, "sc_rich");
            Stock(save, "MEL01", "AMR10", "BPK01", "WPN22", "AMR04", "BPK06", "EAR03", "HDW07", "RIG03");

            foreach (var id in new[] { "MEL01", "AMR10", "BPK01" })
                Assert.IsTrue(Equipment.TryEquip(save, _data, poor.Uid, id), id);
            foreach (var id in new[] { "WPN22", "AMR04", "BPK06", "EAR03", "HDW07", "RIG03" })
                Assert.IsTrue(Equipment.TryEquip(save, _data, rich.Uid, id), id);

            var lo = Equipment.EffectsOf(poor, _data);
            var hi = Equipment.EffectsOf(rich, _data);

            Assert.IsTrue(lo.HasWeapon && hi.HasWeapon);
            Assert.Greater(hi.Luck, lo.Luck, "좋은 무기·헤드셋이 운을 올려야 한다");
            Assert.Greater(hi.SurvivalBonus, lo.SurvivalBonus, "좋은 방탄복이 생존을 올려야 한다");
            Assert.Greater(hi.ExtraLootRolls, lo.ExtraLootRolls, "큰 가방·리그가 회수를 늘려야 한다");
            Assert.Greater(hi.SeverityMitigation, lo.SeverityMitigation, "헬멧이 치명상을 완화해야 한다");
            Assert.AreEqual(0, lo.SeverityMitigation, "헬멧이 없으면 완화도 없다");
        }

        /// <summary>운이 상한을 넘지 않는다. 1.0 이면 싼 물건이 아예 안 나와 전리품 표가 무의미해진다.</summary>
        [Test]
        public void Effects_LuckIsCapped()
        {
            var save = NewSave();
            var scav = Add(save);
            Stock(save, "WPN22", "EAR03");
            Equipment.TryEquip(save, _data, scav.Uid, "WPN22");
            Equipment.TryEquip(save, _data, scav.Uid, "EAR03");

            Assert.LessOrEqual(Equipment.EffectsOf(scav, _data).Luck, _data.Balance.Equipment.LuckCap);
        }

        /// <summary>
        /// 팀 효과: 운과 회수는 평균, 생존은 합.
        /// 평균이어야 한 명에게만 좋은 무기를 쥐여주고 나머지를 맨몸으로 보내는 게 최적이 되지 않는다.
        /// </summary>
        [Test]
        public void TeamEffects_AverageLuckButSumSurvival()
        {
            var save = NewSave();
            var a = Add(save, "sc_a");
            var b = Add(save, "sc_b");
            Stock(save, "WPN22", "MEL01", "AMR04", "AMR10");

            Equipment.TryEquip(save, _data, a.Uid, "WPN22");
            Equipment.TryEquip(save, _data, a.Uid, "AMR04");
            Equipment.TryEquip(save, _data, b.Uid, "MEL01");
            Equipment.TryEquip(save, _data, b.Uid, "AMR10");

            var ea = Equipment.EffectsOf(a, _data);
            var eb = Equipment.EffectsOf(b, _data);
            var team = Equipment.TeamEffectsOf(save, _data, new[] { a.Uid, b.Uid });

            Assert.AreEqual((ea.Luck + eb.Luck) / 2, team.Luck, 1e-9, "운은 평균");
            Assert.AreEqual(ea.SurvivalBonus + eb.SurvivalBonus, team.SurvivalBonus, "생존은 합");
            Assert.Less(team.Luck, ea.Luck, "한 명만 무장해도 팀 운이 그 사람 수준이 되면 안 된다");
        }

        // ── 정산에 실제로 반영되는가 ──────────────────────────────

        private long RunLootValue(string[] gear, int runs)
        {
            long total = 0;
            for (int i = 0; i < runs; i++)
            {
                var clock = new TestClock(T0);
                var save = NewSave();
                save.Warehouse.Capacity = 400;   // 넘침이 통계를 흐리지 않게
                save.RngCounter = (uint)(i * 2 + 1);
                var scav = Add(save, "sc_1", survival: 9);

                foreach (var id in gear)
                {
                    Stock(save, id);
                    Assert.IsTrue(Equipment.TryEquip(save, _data, scav.Uid, id), id);
                }

                var exp = new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { scav.Uid }, T0);
                Assert.IsNotNull(exp);

                clock.Advance(TimeSpan.FromHours(6));
                var report = NewResolver(clock).Resolve(save, _data);

                foreach (var loot in report.Expeditions[0].Loot)
                {
                    var def = _data.GetItem(loot.ItemId);
                    total += (def != null ? def.BasePrice : 0) * loot.Count;
                }
            }
            return total / runs;
        }

        /// <summary>
        /// 좋은 장비가 실제로 더 많이 가져오는가. 이게 안 되면 장비 시스템 전체가 장식이다.
        /// </summary>
        [Test]
        public void Loot_IsBetterWithBetterGear()
        {
            const int runs = 300;
            long knife = RunLootValue(new[] { "MEL01" }, runs);
            long full = RunLootValue(new[] { "WPN22", "EAR03", "BPK06", "RIG03" }, runs);

            Assert.Greater(full, knife * 15 / 10,
                $"완전 무장이 단검보다 최소 1.5배는 가져와야 한다 (단검 {knife:N0} / 완전 {full:N0})");
        }

        /// <summary>실종·사망하면 장비도 같이 잃는다. 창고로 돌아오지 않는다 (GDD §7).</summary>
        [Test]
        public void LostScav_LosesTheirGear()
        {
            int checkedRuns = 0;

            for (int i = 0; i < 400 && checkedRuns < 1; i++)
            {
                var clock = new TestClock(T0);
                var save = NewSave();
                save.RngCounter = (uint)(i * 2 + 1);
                var scav = Add(save, "sc_1", survival: 1);

                Stock(save, "MEL01", "AMR10");
                Equipment.TryEquip(save, _data, scav.Uid, "MEL01");
                Equipment.TryEquip(save, _data, scav.Uid, "AMR10");

                new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { scav.Uid }, T0);
                clock.Advance(TimeSpan.FromHours(6));
                var result = NewResolver(clock).Resolve(save, _data).Expeditions[0];

                if (result.LostScavUids.Count == 0) continue;

                checkedRuns++;
                Assert.AreEqual(0, scav.Equipment.Count, "돌아오지 못했으면 장비가 남아 있으면 안 된다");
                Assert.AreEqual(2, result.LostGear.Count, "잃은 장비가 보고에 적혀야 한다");
                CollectionAssert.Contains(result.LostGear, "MEL01");
                CollectionAssert.Contains(result.LostGear, "AMR10");
                Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, "AMR10"), "창고로 돌아오면 안 된다");
            }

            Assert.AreEqual(1, checkedRuns, "400회 안에 상실이 한 번도 없었다 — 위험이 없는 것이다");
        }

        /// <summary>부상으로 돌아온 사람은 장비를 지킨다.</summary>
        [Test]
        public void InjuredScav_KeepsTheirGear()
        {
            int seen = 0;

            for (int i = 0; i < 400 && seen < 1; i++)
            {
                var clock = new TestClock(T0);
                var save = NewSave();
                save.RngCounter = (uint)(i * 2 + 1);
                var scav = Add(save, "sc_1", survival: 12);

                Stock(save, "MEL01");
                Equipment.TryEquip(save, _data, scav.Uid, "MEL01");

                new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { scav.Uid }, T0);
                clock.Advance(TimeSpan.FromHours(6));
                var result = NewResolver(clock).Resolve(save, _data).Expeditions[0];

                if (result.InjuredScavUids.Count == 0) continue;

                seen++;
                Assert.AreEqual("MEL01", scav.Equipment[EquipSlot.Weapon]);
                Assert.AreEqual(0, result.LostGear.Count);
            }

            Assert.AreEqual(1, seen, "400회 안에 부상이 한 번도 없었다");
        }

        /// <summary>같은 시드는 장비가 같으면 같은 결과. 장비를 넣어도 결정론이 깨지지 않는다.</summary>
        [Test]
        public void SameSeedAndGear_GivesSameResult()
        {
            string Run()
            {
                var clock = new TestClock(T0);
                var save = NewSave();
                save.RngCounter = 77;
                var scav = Add(save, "sc_1", survival: 6);
                Stock(save, "WPN22", "HDW07");
                Equipment.TryEquip(save, _data, scav.Uid, "WPN22");
                Equipment.TryEquip(save, _data, scav.Uid, "HDW07");

                new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { scav.Uid }, T0);
                clock.Advance(TimeSpan.FromHours(6));
                var r = NewResolver(clock).Resolve(save, _data).Expeditions[0];

                var parts = new List<string>();
                foreach (var l in r.Loot) parts.Add($"{l.ItemId}x{l.Count}");
                parts.Sort();
                return string.Join(",", parts.ToArray())
                     + "|" + r.HadCombat + "|" + r.HadAccident
                     + "|" + string.Join("+", r.LostGear.ToArray());
            }

            Assert.AreEqual(Run(), Run());
        }
    }
}
