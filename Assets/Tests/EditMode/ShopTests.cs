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
    /// 첫 무기와 상점.
    ///
    /// <para>무기를 필수로 만든 순간 <b>막다른 길</b>이 생겼다 — 입문 지역 전리품 표에
    /// 장비가 없고, 상점은 신뢰도가 필요하고, 고용한 사람은 맨손이었다. 돈을 아무리 벌어도
    /// 파견을 못 간다. 여기 있는 테스트는 그 길이 다시 막히지 않는지 본다.</para>
    /// </summary>
    [TestFixture]
    public class ShopTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave(long money = 0)
        {
            return new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = money, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
        }

        private static OfflineResolver NewResolver(TestClock clock, ScavMarket market) =>
            new OfflineResolver(clock, new List<ITimelineSystem> { market });

        // ── 첫 무기 ──────────────────────────────────────────────

        /// <summary>
        /// <b>이 프로젝트에서 가장 중요한 테스트다.</b> 고용하자마자 파견할 수 있어야 한다.
        /// 못 하면 게임이 거기서 끝난다.
        /// </summary>
        [Test]
        public void HiredScav_CanBeDispatchedImmediately()
        {
            var clock = new TestClock(T0);
            var save = NewSave(money: 5_000_000);
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            var scav = market.TryHire(save, save.Market.Offers[0].OfferId, clock.UtcNow);
            Assert.IsNotNull(scav);

            Assert.IsNull(
                ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", new[] { scav.Uid }),
                "고용 직후 바로 파견할 수 있어야 한다 — 못 하면 게임이 진행되지 않는다");
        }

        [Test]
        public void HiredScav_BringsTheirOwnWeapon()
        {
            var clock = new TestClock(T0);
            var save = NewSave(money: 5_000_000);
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            var offer = save.Market.Offers[0];
            Assert.IsFalse(string.IsNullOrEmpty(offer.StarterWeapon), "후보에 지참 무기가 적혀 있어야 한다");

            var scav = market.TryHire(save, offer.OfferId, clock.UtcNow);
            Assert.AreEqual(offer.StarterWeapon, scav.Equipment[EquipSlot.Weapon]);
            Assert.IsTrue(Equipment.EffectsOf(scav, _data).HasWeapon);
        }

        /// <summary>지참 무기는 창고를 거치지 않는다. 창고가 꽉 차도 무기는 들고 온다.</summary>
        [Test]
        public void StarterWeapon_DoesNotDependOnWarehouseSpace()
        {
            var clock = new TestClock(T0);
            var save = NewSave(money: 5_000_000);
            save.Warehouse.Capacity = 0;
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            var scav = market.TryHire(save, save.Market.Offers[0].OfferId, clock.UtcNow);
            Assert.IsTrue(Equipment.EffectsOf(scav, _data).HasWeapon,
                "창고가 꽉 찼다고 무기 없는 스캐브가 오면 고용이 헛돈이 된다");
        }

        /// <summary>지참 무기도 잃는다. 예외를 두면 "죽어도 무기는 남는다"가 되어 저울이 무너진다.</summary>
        [Test]
        public void StarterWeapon_IsLostWithTheScav()
        {
            for (int i = 0; i < 400; i++)
            {
                var clock = new TestClock(T0);
                var save = NewSave(money: 5_000_000);
                save.RngCounter = (uint)(i * 3 + 1);

                var scav = new ScavState
                {
                    Uid = "sc_1", Name = "테스트", Survival = 1,
                    Search = 5, Combat = 4, WagePerHour = 40000, Status = ScavStatus.Idle,
                };
                scav.Equipment[EquipSlot.Weapon] = "MEL01";
                save.Scavs.Add(scav);

                new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { scav.Uid }, T0);
                clock.Advance(TimeSpan.FromHours(6));
                var r = new OfflineResolver(clock, new List<ITimelineSystem> { new ExpeditionSystem() })
                    .Resolve(save, _data).Expeditions[0];

                if (r.LostScavUids.Count == 0) continue;

                CollectionAssert.Contains(r.LostGear, "MEL01");
                Assert.AreEqual(0, scav.Equipment.Count);
                return;
            }
            Assert.Fail("400회 안에 상실이 없었다");
        }

        // ── 상점 ─────────────────────────────────────────────────

        [Test]
        public void Shop_AtZeroTrust_OffersOnlyTheFirstTrader()
        {
            var save = NewSave();
            var offers = Shop.OffersFor(save, _data);

            Assert.Greater(offers.Count, 0, "신뢰도 0 에서도 살 것은 있어야 한다");
            foreach (var o in offers)
                Assert.AreEqual("DONGDAEMUN_CHOI", o.NpcId, "잠긴 상인의 물건이 섞이면 안 된다");
        }

        [Test]
        public void Shop_TrustUnlocksBetterTraders()
        {
            var save = NewSave();
            int before = Shop.OffersFor(save, _data).Count;

            save.NpcTrust["HWANG"] = 5;
            var after = Shop.OffersFor(save, _data);

            Assert.Greater(after.Count, before, "신뢰도가 오르면 품목이 늘어야 한다");

            bool hasWeapon = false;
            foreach (var o in after)
                if (_data.GetItem(o.ItemId).EquipSlot == EquipSlot.Weapon) hasWeapon = true;
            Assert.IsTrue(hasWeapon, "용산 김씨가 열리면 무기를 살 수 있어야 한다");
        }

        /// <summary>다른 고용주의 신뢰도로는 열리지 않는다.</summary>
        [Test]
        public void Shop_UsesEmployerTrustOnly()
        {
            var save = NewSave();
            save.NpcTrust["SOMEONE_ELSE"] = 999;

            foreach (var o in Shop.OffersFor(save, _data))
                Assert.AreEqual("DONGDAEMUN_CHOI", o.NpcId);
        }

        [Test]
        public void Shop_FiltersBySlot()
        {
            var save = NewSave();
            save.NpcTrust["HWANG"] = 5;

            foreach (var o in Shop.OffersFor(save, _data, EquipSlot.Weapon))
                Assert.AreEqual(EquipSlot.Weapon, _data.GetItem(o.ItemId).EquipSlot);
        }

        /// <summary>사서 되팔면 손해다. 창고를 상점으로 쓰는 무한 루프를 막는다.</summary>
        [Test]
        public void Shop_BuyingCostsMoreThanSelling()
        {
            var save = NewSave();
            var def = _data.GetItem("AMR10");

            long buy = Shop.PriceOf(def, _data.Shop);
            long sell = (long)(def.BasePrice * _data.Balance.SellPriceRatio);

            Assert.Greater(buy, sell, "구매가가 판매가보다 높아야 한다");
        }

        [Test]
        public void Buy_MovesMoneyIntoTheWarehouse()
        {
            var save = NewSave(money: 1_000_000);
            long price = Shop.PriceOf(_data.GetItem("AMR10"), _data.Shop);

            Assert.IsTrue(Shop.TryBuy(save, _data, "AMR10"));
            Assert.AreEqual(1_000_000 - price, save.Player.Money);
            Assert.AreEqual(1, Warehouse.CountOf(save.Warehouse, "AMR10"));
        }

        [Test]
        public void Buy_RefusedWhenLockedOrPoorOrFull()
        {
            // 잠김
            var locked = NewSave(money: 10_000_000);
            Assert.IsNotNull(Shop.BuyBlockReason(locked, _data, "WPN22"), "신뢰도 0 에서 총을 살 수 없다");
            Assert.IsFalse(Shop.TryBuy(locked, _data, "WPN22"));
            Assert.AreEqual(10_000_000, locked.Player.Money, "실패했으면 돈이 빠지면 안 된다");

            // 자금 부족
            var poor = NewSave(money: 10);
            var reason = Shop.BuyBlockReason(poor, _data, "AMR10");
            Assert.IsNotNull(reason);
            StringAssert.Contains("자금", reason);

            // 창고 꽉 참
            var full = NewSave(money: 10_000_000);
            full.Warehouse.Capacity = 0;
            Assert.IsFalse(Shop.TryBuy(full, _data, "AMR10"));
            Assert.AreEqual(10_000_000, full.Player.Money, "못 넣었으면 돈도 그대로여야 한다");
        }

        [Test]
        public void Buy_RefusesNonEquipment()
        {
            var save = NewSave(money: 10_000_000);
            Assert.IsNotNull(Shop.BuyBlockReason(save, _data, "JUNK03"));
            Assert.IsFalse(Shop.TryBuy(save, _data, "JUNK03"));
        }

        [Test]
        public void NextLockedTrader_PointsAtTheNearestOne()
        {
            var save = NewSave();
            var next = Shop.NextLockedTrader(save, _data);
            Assert.IsNotNull(next);
            Assert.AreEqual("YONGSAN_KIM", next.NpcId);

            save.NpcTrust["HWANG"] = 5;
            Assert.IsNull(Shop.NextLockedTrader(save, _data), "전부 열렸으면 null");
        }

        // ── 세션 경유 ────────────────────────────────────────────

        /// <summary>
        /// 고용 → 구매 → 지급 → 파견이 세션 하나로 이어지는가.
        ///
        /// <para>파견 <i>결과</i>는 일부러 보지 않는다. 복귀까지 돌리면 사고 판정이 끼어들어
        /// 드물게 실패하는 테스트가 되고, 그런 테스트는 진짜 고장을 가린다.
        /// 복귀 쪽은 <c>EquipmentTests</c> 가 통계로 본다.</para>
        /// </summary>
        [Test]
        public void Session_HireBuyEquipDepart()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var session = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();
            session.Save.Player.Money = 5_000_000;

            clock.Advance(TimeSpan.FromMinutes(1));
            session.Tick();

            var scav = session.Hire(session.Save.Market.Offers[0].OfferId);
            Assert.IsNotNull(scav);

            // 방어구를 사서 입힌다 (신뢰도 0 상인 품목).
            Assert.IsTrue(session.Buy("AMR10"));
            Assert.IsTrue(session.Equip(scav.Uid, "AMR10"));

            Assert.AreEqual("AMR10", session.Save.Scavs[0].Equipment[EquipSlot.BodyArmor]);
            Assert.AreEqual(0, Warehouse.CountOf(session.Save.Warehouse, "AMR10"), "지급했으면 창고에서 빠진다");

            Assert.IsNotNull(session.Depart("MYEONGDONG", new[] { scav.Uid }),
                "고용해서 장비까지 채운 사람은 나갈 수 있어야 한다");
        }

        // ── 진열 순서 ────────────────────────────────────────────

        /// <summary>
        /// 목록은 <b>효과 순</b>이어야 한다 — 값 순이 아니라.
        ///
        /// <para>값으로 세우면 "맨 아래가 제일 좋은 것"이라는 인상을 주는데, 무기 칸에서는
        /// 그게 거짓이다. 본편 가격을 그대로 쓰고 근접무기는 전부 최하 등급이라
        /// 제일 비싼 무기가 제일 약한 무기다 — 950,000원짜리 단검(등급 1)이 260,000원짜리
        /// 소총(등급 5)보다 아래다. 돈이 생긴 플레이어가 맨 아래를 사면 손해를 본다.</para>
        /// </summary>
        [Test]
        public void Offers_AreOrderedByEffect_NotPrice()
        {
            var save = NewSave();
            save.NpcTrust["HWANG"] = 100;   // 상인을 다 열어놓고 본다

            foreach (var slot in EquipSlot.All)
            {
                var offers = Shop.OffersFor(save, _data, slot);

                for (int i = 1; i < offers.Count; i++)
                {
                    int previous = Equipment.EffectRank(_data.GetItem(offers[i - 1].ItemId));
                    int current = Equipment.EffectRank(_data.GetItem(offers[i].ItemId));

                    Assert.GreaterOrEqual(current, previous,
                        $"{EquipSlot.LabelOf(slot)} 목록이 효과 순이 아니다: " +
                        $"{offers[i - 1].ItemId}(효과 {previous}) 뒤에 {offers[i].ItemId}(효과 {current})");
                }
            }
        }

        /// <summary>칸이 섞인 목록도 칸끼리 뭉쳐 있어야 한다 — 방어 6 과 64칸 가방은 비교할 수 없다.</summary>
        [Test]
        public void MixedOffers_StayGroupedBySlot()
        {
            var save = NewSave();
            save.NpcTrust["HWANG"] = 100;

            var offers = Shop.OffersFor(save, _data);
            var seenSlots = new List<string>();

            foreach (var offer in offers)
            {
                string slot = _data.GetItem(offer.ItemId).EquipSlot;
                if (seenSlots.Count == 0 || seenSlots[seenSlots.Count - 1] != slot)
                {
                    Assert.IsFalse(seenSlots.Contains(slot),
                        $"{EquipSlot.LabelOf(slot)} 이 목록에서 흩어져 있다");
                    seenSlots.Add(slot);
                }
            }
        }

        /// <summary>
        /// 무기가 필수가 되기 전에 만든 세이브를 열면, 맨손인 스캐브에게 칼 한 자루를 쥐여 준다.
        /// 이게 없으면 그 세이브는 파견을 영영 못 간다.
        /// </summary>
        [Test]
        public void OldSave_GetsAWeaponSoItIsNotStuck()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var codec = new NewtonsoftJsonCodec();

            // v1 세이브를 손으로 만든다 — 스캐브는 있고 장비는 없다.
            var old = NewSave(money: 100_000);
            old.SchemaVersion = 1;
            old.Scavs.Add(new ScavState
            {
                Uid = "sc_old", Name = "정대만", Search = 1, Combat = 6, Survival = 4,
                WagePerHour = 40000, Status = ScavStatus.Idle, HiredAt = T0,
            });
            files.WriteAllText(SaveService.FileName, codec.Serialize(old));

            var session = new GameSession(new SaveService(files, codec, clock), _data, clock);
            session.Boot();

            var scav = session.Save.Scavs[0];
            Assert.AreEqual(SaveService.CurrentSchemaVersion, session.Save.SchemaVersion);
            Assert.IsTrue(Equipment.EffectsOf(scav, _data).HasWeapon, "맨손이면 이 세이브는 끝난 것이다");
            Assert.IsNull(
                ExpeditionSystem.DepartBlockReason(session.Save, _data, "MYEONGDONG", new[] { scav.Uid }));
        }

        /// <summary>이미 무기를 든 사람의 것을 바꾸지 않는다.</summary>
        [Test]
        public void Migration_DoesNotOverwriteAnExistingWeapon()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var codec = new NewtonsoftJsonCodec();

            var old = NewSave();
            old.SchemaVersion = 1;
            var scav = new ScavState { Uid = "sc_old", Name = "테스트", Status = ScavStatus.Idle };
            scav.Equipment[EquipSlot.Weapon] = "WPN22";
            old.Scavs.Add(scav);
            files.WriteAllText(SaveService.FileName, codec.Serialize(old));

            var session = new GameSession(new SaveService(files, codec, clock), _data, clock);
            session.Boot();

            Assert.AreEqual("WPN22", session.Save.Scavs[0].Equipment[EquipSlot.Weapon]);
        }

        /// <summary>앱을 껐다 켜도 장비가 남는다.</summary>
        [Test]
        public void Session_EquipmentSurvivesRestart()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var codec = new NewtonsoftJsonCodec();

            var session = new GameSession(new SaveService(files, codec, clock), _data, clock);
            session.Boot();
            session.Save.Player.Money = 5_000_000;
            clock.Advance(TimeSpan.FromMinutes(1));
            session.Tick();

            var scav = session.Hire(session.Save.Market.Offers[0].OfferId);
            session.Buy("AMR10");
            session.Equip(scav.Uid, "AMR10");
            session.Suspend();

            var reopened = new GameSession(new SaveService(files, codec, clock), _data, clock);
            reopened.Boot();

            var loaded = reopened.Save.Scavs[0];
            Assert.AreEqual("AMR10", loaded.Equipment[EquipSlot.BodyArmor]);
            Assert.AreEqual(scav.Equipment[EquipSlot.Weapon], loaded.Equipment[EquipSlot.Weapon],
                "지참 무기도 남아야 한다");
        }
    }
}
