using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 테스트 픽스처가 진짜 데이터와 갈라지지 않았는지 본다.
    ///
    /// <para><b>왜 필요한가.</b> <see cref="FakeRegistry"/> 는 일부러 작다 — 아이템 다섯에 지역 넷이면
    /// 정산 규칙을 보는 데 충분하고, 458종을 다 싣는 건 낭비다. 거기까지는 옳다.</para>
    ///
    /// <para>문제는 그 안의 값이 <b>"본편 실제 아이템의 것을 그대로 옮겼다"</b>고 적혀 있다는 것이다.
    /// 옮겨 적은 값은 원본이 바뀌면 갈라진다. 그러면 장비 효과·파견비 테스트가 <b>존재하지 않는
    /// 세계</b>를 검증하면서 통과한다 — 통과하는 테스트가 아무것도 지켜주지 않는 상태다.</para>
    ///
    /// <para>방금 정산 테스트가 정확히 그 꼴이었다: 시스템 목록을 따로 들고 있다가 넷이 빠진 채로
    /// 계속 초록불이었다. 베껴 둔 것은 베낀 사실을 검사해야 한다.</para>
    ///
    /// <para><b>일부러 다른 것은 비교하지 않는다.</b> 픽스처는 시나리오를 만들려고 값을 비트는
    /// 자리가 있고, 그건 드리프트가 아니라 설계다. 아래 각 예외에 이유를 적어 둔다.</para>
    /// </summary>
    [TestFixture]
    public class FixtureDriftTests
    {
        private IDataRegistry _real;
        private FakeRegistry _fake;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _real = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
            _fake = FakeRegistry.Build();
        }

        /// <summary>
        /// 아이템 값은 전부 본편에서 옮겨 온 것이다 — 값, 스택, 장비 수치까지.
        /// 하나라도 다르면 "좋은 장비가 실제로 더 좋은가"를 보는 테스트가 헛것을 보고 있다.
        /// </summary>
        [Test]
        public void FixtureItems_MatchTheRealData()
        {
            var drift = new List<string>();

            foreach (var fake in _fake.AllItems)
            {
                var real = _real.GetItem(fake.Id);
                if (real == null)
                {
                    drift.Add($"{fake.Id}: 픽스처에는 있는데 items.json 에 없다");
                    continue;
                }

                Compare(drift, fake.Id, "basePrice", fake.BasePrice, real.BasePrice);
                Compare(drift, fake.Id, "maxStack", fake.MaxStack, real.MaxStack);

                // null 과 "" 를 같게 본다. 장비가 아닌 물건은 JSON 에서 빠져 있기도 하고
                // 빈 문자열로 있기도 한데, 둘 다 "장비 칸이 없다"는 같은 뜻이다.
                Compare(drift, fake.Id, "equipSlot", Slot(fake.EquipSlot), Slot(real.EquipSlot));
                Compare(drift, fake.Id, "armorClass", fake.ArmorClass, real.ArmorClass);
                Compare(drift, fake.Id, "gridSlots", fake.GridSlots, real.GridSlots);
                Compare(drift, fake.Id, "hearingRange", fake.HearingRange, real.HearingRange);
                Compare(drift, fake.Id, "weaponGrade", fake.WeaponGrade, real.WeaponGrade);
            }

            Assert.IsEmpty(drift,
                "픽스처가 진짜 데이터와 갈라졌다 — 테스트가 없는 세계를 검증하고 있다:\n"
                + string.Join("\n", drift.ToArray()));
        }

        /// <summary>
        /// 지역의 시간·비용·위험도도 옮겨 온 값이다. 파견비 테스트가 이 숫자 위에 서 있다.
        ///
        /// <para><b>해금 조건과 전리품 표는 비교하지 않는다.</b> 픽스처의 구로는 일부러 조건 없이
        /// 열어 두었고(기존 파견 테스트가 해금 판정에 걸리지 않게), 전리품 표도 일부러 작은
        /// 가짜를 가리킨다. 둘 다 드리프트가 아니라 시나리오를 만들기 위한 조정이다.</para>
        /// </summary>
        [Test]
        public void FixtureMaps_MatchTheRealData()
        {
            var drift = new List<string>();

            foreach (var fake in _fake.AllMaps)
            {
                var real = _real.GetMap(fake.Id);

                // 픽스처 전용 지역. BROKEN_UNLOCK 처럼 "데이터에 오타가 난 경우"를 재현하려고
                // 지어낸 것이라 원본이 없는 게 정상이다.
                if (real == null) continue;

                Compare(drift, fake.Id, "tier", fake.Tier, real.Tier);
                Compare(drift, fake.Id, "durationMinutes", fake.DurationMinutes, real.DurationMinutes);
                Compare(drift, fake.Id, "baseCostWage", fake.BaseCostWage, real.BaseCostWage);
                Compare(drift, fake.Id, "baseCostSupply", fake.BaseCostSupply, real.BaseCostSupply);
                Compare(drift, fake.Id, "riskLevel", fake.RiskLevel, real.RiskLevel);

                // 소수는 자릿수까지 같은지 따지지 않는다. 0.32 를 0.320 으로 적어도 같은 뜻이고,
                // 여기서 잡으려는 건 "값이 바뀐 것"이지 표기가 아니다.
                if (System.Math.Abs(fake.CombatChance - real.CombatChance) > 1e-6)
                    drift.Add($"{fake.Id}.combatChance: 픽스처 {fake.CombatChance} vs 진짜 {real.CombatChance}");
            }

            Assert.IsEmpty(drift,
                "픽스처 지역이 expeditions.json 과 갈라졌다:\n" + string.Join("\n", drift.ToArray()));
        }

        /// <summary>
        /// 전송 한도는 픽스처가 손으로 적어 두었다. 이 값 위에서 <c>Outbox</c> 테스트가 도는데,
        /// 그게 본편 경제를 지키는 유일한 선이라(GDD §11) 갈라지면 곤란하다.
        /// </summary>
        [Test]
        public void FixtureTransferLimits_MatchTheRealData()
        {
            var f = _fake.Transfer.Limits;
            var r = _real.Transfer.Limits;

            Assert.AreEqual(r.MaxShipmentsPerDay, f.MaxShipmentsPerDay, "maxShipmentsPerDay");
            Assert.AreEqual(r.MaxItemStacksPerShipment, f.MaxItemStacksPerShipment, "maxItemStacksPerShipment");
            Assert.AreEqual(r.MaxShipmentValue, f.MaxShipmentValue, "maxShipmentValue");
            Assert.AreEqual(r.MaxDailyValue, f.MaxDailyValue, "maxDailyValue");
            Assert.AreEqual(r.MaxPendingShipments, f.MaxPendingShipments, "maxPendingShipments");
        }

        /// <summary>
        /// 레시피는 <b>일부러 다르다</b> — 그래서 비교하지 않는다. 대신 그 사실을 여기 적어 둔다.
        ///
        /// <para>픽스처의 RCP_BOLT 는 1시간(진짜는 10분)이고 RCP_AMMO 는 작업대 2단계(진짜는 1단계)다.
        /// 전자는 "파견 35분이 공장 1시간보다 먼저 적용되는가"를 만들기 위한 것이고,
        /// 후자는 작업대 단계 잠금을 재현하기 위한 것이다.</para>
        ///
        /// <para>이 테스트는 값을 지키는 게 아니라 <b>의도를 기록</b>한다. 나중에 누가 이 차이를
        /// 보고 "드리프트네" 하며 맞춰 버리면 위 두 시나리오가 조용히 사라지기 때문이다.</para>
        /// </summary>
        [Test]
        public void FixtureRecipes_AreDeliberatelyDifferent()
        {
            var bolt = _fake.GetRecipe("RCP_BOLT");
            var realBolt = _real.GetRecipe("RCP_BOLT");

            if (bolt != null && realBolt != null)
                Assert.Greater(bolt.WorkSeconds, realBolt.WorkSeconds,
                    "픽스처의 RCP_BOLT 가 진짜보다 짧아지면 '파견이 공장보다 먼저'를 못 만든다");

            var ammo = _fake.GetRecipe("RCP_AMMO");
            var realAmmo = _real.GetRecipe("RCP_AMMO");

            if (ammo != null && realAmmo != null)
                Assert.Greater(ammo.StationLevel, realAmmo.StationLevel,
                    "픽스처의 RCP_AMMO 가 1단계가 되면 작업대 잠금 시나리오가 사라진다");
        }

        private static void Compare<T>(List<string> drift, string id, string field, T fake, T real)
        {
            if (!EqualityComparer<T>.Default.Equals(fake, real))
                drift.Add($"{id}.{field}: 픽스처 {fake} vs 진짜 {real}");
        }

        private static string Slot(string s) => string.IsNullOrEmpty(s) ? "" : s;
    }
}
