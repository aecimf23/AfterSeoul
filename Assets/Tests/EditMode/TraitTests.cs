using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 스캐브 특성 (GDD §14).
    ///
    /// <para><b>왜 이 파일이 생겼나.</b> 특성은 고용 시장에서 뽑혀 세이브에 저장되고 인원 화면에
    /// "특성 겁쟁이"라고 표시까지 됐는데, <c>EffectType</c> 을 읽는 코드가 <b>게임 전체에 한 곳도
    /// 없었다.</b> 화면은 있지도 않은 능력을 광고했고, 플레이어가 특성을 보고 사람을 고를 이유는
    /// 없었다 — 어차피 아무 차이도 없었으니까.</para>
    ///
    /// <para>게다가 <c>penalty</c> 는 JSON 에 있는데 파서가 아예 안 옮기고 있었다. 그래서
    /// 겁쟁이는 <b>장점만 있고 단점이 없는</b> 특성이었다 — manualSteps 와 같은 부류다.</para>
    ///
    /// <para>여기서 보는 것은 수치가 아니라 <b>연결</b>이다: 특성이 붙은 사람과 안 붙은 사람이
    /// 실제로 다르게 굴러가는가.</para>
    /// </summary>
    [TestFixture]
    public class TraitTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private IDataRegistry _data;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
        }

        private static GameSave NewSave()
        {
            var save = new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState
                {
                    CreatedAt = T0, Money = 100_000_000, Level = 20, EmployerNpcId = "HWANG",
                },
            };
            save.Warehouse.Capacity = 9999;
            save.NpcTrust["HWANG"] = 100;
            return save;
        }

        private static ScavState Scav(GameSave save, string uid, params string[] traits)
        {
            var s = new ScavState
            {
                Uid = uid, Name = uid, Tier = 1,
                Search = 4, Combat = 4, Survival = 4,
                Status = ScavStatus.Idle, HiredAt = T0,
            };
            s.Equipment[EquipSlot.Weapon] = "MEL01";
            s.TraitIds.AddRange(traits);
            save.Scavs.Add(s);
            return s;
        }

        // ── 데이터가 실제로 실려 오는가 ──────────────────────────

        /// <summary>
        /// <c>penalty</c> 가 파서에서 통째로 버려지고 있었다. 값이 안 실리면 아래 규칙은
        /// 전부 0 을 가지고 도는 셈이라, 이 검사가 먼저다.
        /// </summary>
        [Test]
        public void PenaltyIsParsed_NotDroppedOnTheFloor()
        {
            var coward = Traits.Find(_data.ScavPool, "TR_COWARD");
            Assert.IsNotNull(coward, "TR_COWARD 가 데이터에 없다");

            Assert.AreEqual("fleeChance", coward.EffectType);
            Assert.Greater(coward.EffectValue, 0.0);

            Assert.AreEqual("lootMultiplier", coward.PenaltyType,
                "penalty 가 안 실렸다 — 겁쟁이가 장점만 있는 특성이 된다");
            Assert.Less(coward.PenaltyValue, 0.0, "대가가 음수가 아니면 대가가 아니다");
        }

        /// <summary>
        /// 데이터에 있는 모든 특성이 <b>읽히는 종류</b>여야 한다.
        /// 오타 하나면 그 특성은 조용히 아무 일도 안 하게 된다 — 이번에 실제로 그랬다.
        /// </summary>
        [Test]
        public void EveryTraitInData_HasAnEffectTheCodeUnderstands()
        {
            var known = new HashSet<string> { "mapBonus", "fleeChance", "injurySurvival" };
            var unknown = new List<string>();

            foreach (var t in _data.ScavPool.Traits)
            {
                if (!known.Contains(t.EffectType))
                    unknown.Add($"{t.Id}: effect '{t.EffectType}'");

                if (!string.IsNullOrEmpty(t.PenaltyType) && t.PenaltyType != "lootMultiplier")
                    unknown.Add($"{t.Id}: penalty '{t.PenaltyType}'");
            }

            Assert.IsEmpty(unknown,
                "Traits 가 모르는 효과 — 붙여도 아무 일도 안 일어난다: " + string.Join(", ", unknown.ToArray()));
        }

        // ── 효과 ────────────────────────────────────────────────

        [Test]
        public void MapBonus_OnlyAppliesInItsOwnDistrict()
        {
            var save = NewSave();
            Scav(save, "sc_a", "TR_YONGSAN_NATIVE");

            double home = Traits.TeamLootScale(save, _data, new[] { "sc_a" }, "YONGSAN_MARKET");
            double away = Traits.TeamLootScale(save, _data, new[] { "sc_a" }, "MYEONGDONG");

            Assert.Greater(home, 1.0, "토박이인데 제 동네에서 아무 차이가 없다");
            Assert.AreEqual(1.0, away, 1e-9, "아무 데서나 붙으면 지역 특성이 아니라 그냥 좋은 특성이다");
        }

        [Test]
        public void Coward_PaysForItsEscapeEverywhere()
        {
            var save = NewSave();
            Scav(save, "sc_a", "TR_COWARD");

            double scale = Traits.TeamLootScale(save, _data, new[] { "sc_a" }, "MYEONGDONG");
            Assert.Less(scale, 1.0, "겁쟁이가 회수 대가를 안 치른다 — 공짜 특성이 된다");

            var e = Traits.EffectsOf(save.Scavs[0], _data, "MYEONGDONG");
            Assert.Greater(e.FleeChance, 0.0);
        }

        [Test]
        public void Medic_AddsToSeverityMitigation()
        {
            var save = NewSave();
            Scav(save, "sc_a", "TR_MEDIC");

            var e = Traits.EffectsOf(save.Scavs[0], _data, "MYEONGDONG");
            Assert.Greater(e.SeverityMitigation, 0.0);
        }

        [Test]
        public void NoTraits_ChangesNothing()
        {
            var save = NewSave();
            Scav(save, "sc_a");

            Assert.AreEqual(1.0, Traits.TeamLootScale(save, _data, new[] { "sc_a" }, "MYEONGDONG"), 1e-9);
        }

        /// <summary>
        /// 팀 효과는 합이 아니라 평균이다. 합이면 토박이 셋을 보내는 게 정답이 되어
        /// 팀 구성이 한 가지로 굳는다.
        /// </summary>
        [Test]
        public void TeamEffect_AveragesInsteadOfStacking()
        {
            var save = NewSave();
            Scav(save, "sc_a", "TR_YONGSAN_NATIVE");
            Scav(save, "sc_b", "TR_YONGSAN_NATIVE");
            Scav(save, "sc_c");

            double one = Traits.TeamLootScale(save, _data, new[] { "sc_a" }, "YONGSAN_MARKET");
            double three = Traits.TeamLootScale(save, _data, new[] { "sc_a", "sc_b", "sc_c" }, "YONGSAN_MARKET");

            Assert.Less(three, one, "셋을 묶었는데 한 명보다 배수가 크면 그건 합산이다");
            Assert.Greater(three, 1.0, "둘이 토박이인데 아무 이득이 없다");
        }

        [Test]
        public void FleeChance_IsCapped()
        {
            // 데이터가 어떻게 바뀌어도 사고를 100% 피하면 안 된다 (GDD §15).
            Assert.Less(Traits.FleeCap, 1.0);
            Assert.Greater(Traits.MinLootScale, 0.0, "회수 배수가 0 이면 나가는 것 자체가 무의미해진다");
        }

        // ── 실제 파견에 닿는가 ──────────────────────────────────

        /// <summary>
        /// <b>여기가 핵심이다.</b> 위의 계산기가 맞아도 파견이 그걸 안 부르면 예전과 똑같다.
        /// 진짜 <see cref="ExpeditionSystem"/> 으로 여러 번 보내서 회수액이 달라지는지 본다.
        /// </summary>
        [Test]
        public void Traits_ActuallyReachTheExpedition()
        {
            long plain = TotalLoot(null);
            long native = TotalLoot("TR_YONGSAN_NATIVE");

            Assert.Greater(native, plain,
                "용산 토박이를 용산에 보냈는데 회수액이 그대로다 — 특성이 파견에 닿지 않는다");
        }

        [Test]
        public void CowardBringsBackLess()
        {
            long plain = TotalLoot(null);
            long coward = TotalLoot("TR_COWARD");

            Assert.Less(coward, plain, "겁쟁이가 남들만큼 가져온다 — 대가가 적용되지 않는다");
        }

        /// <summary>용산에 여러 번 보내 모은 회수 가치. 특성 하나만 바꿔서 비교한다.</summary>
        private long TotalLoot(string traitId)
        {
            const int Runs = 80;
            long total = 0;

            for (int i = 0; i < Runs; i++)
            {
                var save = NewSave();
                save.RngCounter = (uint)(i + 1);

                if (traitId == null) Scav(save, "sc_a");
                else Scav(save, "sc_a", traitId);

                var clock = new TestClock(T0);
                new ExpeditionSystem().Depart(save, _data, "YONGSAN_MARKET", new[] { "sc_a" }, clock.UtcNow);

                clock.Advance(TimeSpan.FromDays(1));
                var report = new OfflineResolver(clock, new List<ITimelineSystem> { new ExpeditionSystem() })
                    .Resolve(save, _data);

                foreach (var result in report.Expeditions)
                    foreach (var stack in result.Loot)
                        total += (long)_data.GetItem(stack.ItemId).BasePrice * stack.Count;
            }

            return total;
        }
    }
}
