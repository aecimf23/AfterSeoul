using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 부상 치료 (GDD §15).
    ///
    /// <para><b>왜 이 파일이 생겼나.</b> <c>ScavStatus.Injured</c> 는 파견 사고와 구조 성공이
    /// 설정하고, 화면과 팀 편성이 "못 나간다"고 읽었다. 그런데 <b>그 상태에서 빠져나오는 코드가
    /// 게임 전체에 한 줄도 없었고</b>, <c>ScavStatus.Treating</c> 은 열거형에 이름만 있었다.</para>
    ///
    /// <para>즉 부상 = 영구 퇴출이었다. 명단은 시간이 갈수록 줄기만 하고, 사고를 피하는 유일한
    /// 방법이 "파견을 안 보내기"가 된다 — 방치형에서 최적 전략이 아무것도 안 하기가 되는 건
    /// 설계 실패다. 레벨업이 없어서 지역이 얼어 있던 것, 작업대 레벨이 안 오르던 것과
    /// <b>정확히 같은 부류</b>다: 값을 읽는 코드가 있다고 그 값이 움직이는 건 아니다.</para>
    /// </summary>
    [TestFixture]
    public class TreatmentTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        // IDataRegistry 로 받는다 — AllItems 는 명시적 인터페이스 구현이라
        // 구체 타입(JsonDataRegistry)으로는 보이지 않는다.
        private IDataRegistry _data;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
        }

        private GameSave NewSave(long money = 10_000_000)
        {
            var save = new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = money, EmployerNpcId = "HWANG" },
            };
            save.Warehouse.Capacity = 500;
            return save;
        }

        private static ScavState Hurt(GameSave save, int tier = 1)
        {
            var scav = new ScavState
            {
                Uid = "s" + save.Scavs.Count,
                Name = "부상자" + save.Scavs.Count,
                Tier = tier,
                Search = 4, Combat = 4, Survival = 4,
                Status = ScavStatus.Injured,
                HiredAt = T0,
            };
            save.Scavs.Add(scav);
            return scav;
        }

        // ── 핵심: 나올 길이 있는가 ────────────────────────────────

        [Test]
        public void Injured_HasAWayOut()
        {
            var save = NewSave();
            var scav = Hurt(save);

            Assert.IsTrue(Treatment.TryTreat(save, _data, scav, T0, useSupplies: false),
                "치료를 시작할 수 없다 — 부상이 영구 퇴출이 된다");
            Assert.AreEqual(ScavStatus.Treating, scav.Status);

            Assert.IsTrue(Treatment.Recover(scav, scav.RecoversAt),
                "시간이 됐는데도 회복하지 않는다");
            Assert.AreEqual(ScavStatus.Idle, scav.Status, "회복하면 다시 나갈 수 있어야 한다");
        }

        /// <summary>
        /// <b>자는 동안에도 나아야 한다.</b> 접속 중에만 회복하면 "밤새 놔뒀는데 아침에도 부상"이
        /// 되고, 그건 방치형에서 있을 수 없는 일이다. 그래서 타임라인에 얹었다.
        /// </summary>
        [Test]
        public void Recovery_HappensWhileOffline()
        {
            var save = NewSave();
            var scav = Hurt(save);
            Treatment.TryTreat(save, _data, scav, T0, useSupplies: false);

            // 진짜 정산기로 돌린다. 사건을 손으로 꺼내 적용하면 정산 순서·시각 처리를
            // 테스트가 흉내 내게 되고, 그러면 정작 그 부분이 틀려도 통과한다.
            var clock = new TestClock(T0);
            var resolver = new OfflineResolver(
                clock, new System.Collections.Generic.List<ITimelineSystem> { new TreatmentSystem() });

            // 앱이 꺼져 있던 하룻밤.
            clock.Advance(TimeSpan.FromDays(1));
            resolver.Resolve(save, _data);

            Assert.AreEqual(ScavStatus.Idle, scav.Status,
                "오프라인 정산에서 회복하지 않으면 '밤새 놔뒀는데 아침에도 부상'이 된다");
        }

        /// <summary>
        /// 정산이 두 번 돌아도 결과가 같아야 한다 (ARCHITECTURE §4).
        /// 여기서는 이미 나은 사람을 또 낫게 해도 아무 일이 없으면 된다.
        /// </summary>
        [Test]
        public void Recovery_IsIdempotent()
        {
            var save = NewSave();
            var scav = Hurt(save);
            Treatment.TryTreat(save, _data, scav, T0, useSupplies: false);

            Assert.IsTrue(Treatment.Recover(scav, scav.RecoversAt));
            Assert.IsFalse(Treatment.Recover(scav, T0.AddDays(2)), "두 번째 회복은 아무 일도 없어야 한다");
            Assert.AreEqual(ScavStatus.Idle, scav.Status);
        }

        [Test]
        public void Recovery_DoesNotHappenEarly()
        {
            var save = NewSave();
            var scav = Hurt(save);
            Treatment.TryTreat(save, _data, scav, T0, useSupplies: false);

            Assert.IsFalse(Treatment.Recover(scav, scav.RecoversAt.AddSeconds(-1)));
            Assert.AreEqual(ScavStatus.Treating, scav.Status);
        }

        // ── 대가 ────────────────────────────────────────────────

        /// <summary>
        /// 공짜로 낫게 두면 사고에 아무 대가가 없어지고, 그러면 장비를 챙길 이유가 사라진다.
        /// </summary>
        [Test]
        public void Treatment_CostsMoneyAndTime()
        {
            var save = NewSave(1_000_000);
            var scav = Hurt(save);

            long before = save.Player.Money;
            Treatment.TryTreat(save, _data, scav, T0, useSupplies: false);

            Assert.Less(save.Player.Money, before, "치료가 공짜면 사고가 사건이 되지 못한다");
            Assert.Greater(scav.RecoversAt, T0, "즉시 회복이면 돈만 있으면 사고가 없던 일이 된다");
        }

        [Test]
        public void Treatment_IsBlockedWithoutMoney()
        {
            var save = NewSave(0);
            var scav = Hurt(save);

            Assert.IsNotNull(Treatment.BlockReason(save, _data, scav), "막힌 이유를 말해줘야 한다");
            Assert.IsFalse(Treatment.TryTreat(save, _data, scav, T0, useSupplies: false));
            Assert.AreEqual(ScavStatus.Injured, scav.Status, "실패했는데 상태가 바뀌면 안 된다");
        }

        [Test]
        public void Treatment_OnlyAppliesToTheInjured()
        {
            var save = NewSave();
            var scav = Hurt(save);
            scav.Status = ScavStatus.OnExpedition;

            Assert.IsFalse(Treatment.TryTreat(save, _data, scav, T0, useSupplies: false),
                "나가 있는 사람을 치료대에 올리면 파견이 사라진다");
        }

        // ── 의료품 ──────────────────────────────────────────────

        /// <summary>
        /// 의료 아이템은 그동안 팔아치우는 것 말고 쓸 데가 없었다.
        /// 치료를 줄이는 데 쓰이면 "팔까 남길까"가 생기고, 그게 창고를 보는 이유가 된다.
        /// </summary>
        [Test]
        public void Supplies_ShortenTheStayAndAreConsumed()
        {
            string medical = FindMedicalItemId();
            Assert.IsNotNull(medical, "의료 아이템이 데이터에 하나도 없다 — 치료 단축이 닿을 수 없다");

            var bare = NewSave();
            var bareScav = Hurt(bare);
            Treatment.TryTreat(bare, _data, bareScav, T0, useSupplies: true);

            var stocked = NewSave();
            var stockedScav = Hurt(stocked);
            Warehouse.TryAdd(stocked.Warehouse, _data, medical, 2);
            Treatment.TryTreat(stocked, _data, stockedScav, T0, useSupplies: true);

            Assert.Less(stockedScav.RecoversAt, bareScav.RecoversAt,
                "의료품을 썼는데 시간이 그대로다");
            Assert.AreEqual(1, Warehouse.CountOf(stocked.Warehouse, medical),
                "의료품이 소모되지 않았다 — 시간만 줄면 공짜 단축이 된다");
        }

        [Test]
        public void Supplies_AreNotRequired()
        {
            var save = NewSave();
            var scav = Hurt(save);

            // 창고가 비어 있어도 치료 자체는 되어야 한다. 의료품이 없다고 못 고치면
            // 초반에 한 번 다치는 순간 그대로 끝난다.
            Assert.IsTrue(Treatment.TryTreat(save, _data, scav, T0, useSupplies: true));
        }

        /// <summary>비싼 것을 조용히 태워버리면 그건 손해를 숨기는 것이다.</summary>
        [Test]
        public void Supplies_UseTheCheapestFirst()
        {
            var save = NewSave();

            string cheapest = null, other = null;
            long cheapestPrice = long.MaxValue;

            foreach (var def in _data.AllItems)
            {
                if (ItemGroups.Of(def) != ItemGroup.Medical) continue;
                if (def.BasePrice < cheapestPrice) { cheapestPrice = def.BasePrice; cheapest = def.Id; }
            }
            foreach (var def in _data.AllItems)
            {
                if (ItemGroups.Of(def) != ItemGroup.Medical) continue;
                if (def.Id != cheapest && def.BasePrice > cheapestPrice) { other = def.Id; break; }
            }

            if (cheapest == null || other == null)
                Assert.Ignore("값이 다른 의료 아이템이 둘 이상 필요한 테스트다");

            Warehouse.TryAdd(save.Warehouse, _data, cheapest, 1);
            Warehouse.TryAdd(save.Warehouse, _data, other, 1);

            Assert.AreEqual(cheapest, Treatment.FindSupplies(save, _data));
        }

        // ── 앞당기기 (보상 광고) ─────────────────────────────────

        [Test]
        public void SpeedUp_PullsRecoveryForwardButNotIntoThePast()
        {
            var save = NewSave();
            var scav = Hurt(save, tier: 3);
            Treatment.TryTreat(save, _data, scav, T0, useSupplies: false);

            var before = scav.RecoversAt;
            Assert.IsTrue(Treatment.SpeedUp(scav, T0, TimeSpan.FromHours(2)));
            Assert.AreEqual(before.AddHours(-2), scav.RecoversAt);

            // 과거로 당기면 정산이 지난 시각에 회복한 것처럼 기록한다.
            Treatment.SpeedUp(scav, T0, TimeSpan.FromDays(30));
            Assert.AreEqual(T0, scav.RecoversAt);
        }

        [Test]
        public void SpeedUp_DoesNothingToSomeoneNotBeingTreated()
        {
            var save = NewSave();
            var scav = Hurt(save);

            Assert.IsFalse(Treatment.SpeedUp(scav, T0, TimeSpan.FromHours(2)));
            Assert.AreEqual(ScavStatus.Injured, scav.Status);
        }

        [Test]
        public void Progress_RunsFromZeroToOne()
        {
            var save = NewSave();
            var scav = Hurt(save);
            Treatment.TryTreat(save, _data, scav, T0, useSupplies: false);

            Assert.AreEqual(0.0, Treatment.Progress(scav, T0), 1e-6);
            Assert.AreEqual(1.0, Treatment.Progress(scav, scav.RecoversAt), 1e-6);

            var mid = scav.TreatedAt + TimeSpan.FromTicks((scav.RecoversAt - scav.TreatedAt).Ticks / 2);
            Assert.AreEqual(0.5, Treatment.Progress(scav, mid), 0.01);
        }

        private string FindMedicalItemId()
        {
            foreach (var def in _data.AllItems)
                if (ItemGroups.Of(def) == ItemGroup.Medical) return def.Id;
            return null;
        }
    }
}
