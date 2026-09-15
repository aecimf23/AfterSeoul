using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Expedition;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 탐색 이벤트.
    ///
    /// <para>여기서 지키려는 건 하나다 — <b>보낸 팀이 결과를 바꾼다</b>. 같은 지역에 같은 시각에
    /// 보내도 누구를 보냈느냐에 따라 다른 이야기가 나와야, "누구를 보낼까"가 시급 계산이 아니라
    /// 선택이 된다. 그게 무너지면 이벤트는 그냥 무작위 가감이고, 무작위 가감은 재미가 아니다.</para>
    /// </summary>
    [TestFixture]
    public class ExpeditionEventTests
    {
        private JsonDataRegistry _data;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));

            string locales = Path.Combine(Application.dataPath, "Resources", "Locales");
            Loc.Load("ko",
                File.ReadAllText(Path.Combine(locales, "ko.json")), null,
                File.ReadAllText(Path.Combine(locales, "mobile", "ko.json")), null);
        }

        // Use departure seeds: adjacent raw xorshift seeds have correlated first draws.
        private static Rng EventRng(uint counter) =>
            Rng.For(new GameSave { RngCounter = counter }.TakeSeed(), "event");

        private MapDef EntryMap()
        {
            foreach (var map in _data.Maps) if (map.Tier <= 1) return map;
            Assert.Fail("티어 1 지역이 없다");
            return null;
        }

        // ── 데이터 ───────────────────────────────────────────────

        [Test]
        public void EveryEvent_HasBothOutcomesInBothLanguages()
        {
            string locales = Path.Combine(Application.dataPath, "Resources", "Locales");
            var missing = new List<string>();

            foreach (var lang in new[] { "ko", "en" })
            {
                Loc.Load(lang,
                    File.ReadAllText(Path.Combine(locales, lang + ".json")), null,
                    File.ReadAllText(Path.Combine(locales, "mobile", lang + ".json")), null);

                foreach (var def in _data.ExpeditionEvents)
                {
                    if (!Loc.Has(def.Id + "_PASS")) missing.Add($"{lang}: {def.Id}_PASS");
                    if (!Loc.Has(def.Id + "_FAIL")) missing.Add($"{lang}: {def.Id}_FAIL");
                }
            }

            Assert.IsEmpty(missing,
                "보고 문구 없는 이벤트 — 일어나도 화면에 아무 말이 안 뜬다\n"
                + string.Join("\n", missing.ToArray()));
        }

        /// <summary>
        /// 실패가 성공보다 좋으면 요구 능력치를 맞출 이유가 없다.
        /// 값 하나를 잘못 넣으면 그 이벤트는 "못 할수록 이득"이 된다.
        /// </summary>
        [Test]
        public void PassingIsNeverWorseThanFailing()
        {
            var problems = new List<string>();

            foreach (var def in _data.ExpeditionEvents)
            {
                if (def.PassLootRolls < def.FailLootRolls)
                    problems.Add($"{def.Id}: 성공했는데 회수가 더 적다");
                if (def.PassMoney < def.FailMoney)
                    problems.Add($"{def.Id}: 성공했는데 돈을 더 잃는다");
                if (def.PassTrust < def.FailTrust)
                    problems.Add($"{def.Id}: 성공했는데 신뢰도가 더 낮다");
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>요구 능력치가 하나도 없으면 그건 사건이 아니라 그냥 공짜다.</summary>
        [Test]
        public void EveryEvent_AsksSomethingOfTheTeam()
        {
            foreach (var def in _data.ExpeditionEvents)
                Assert.Greater(
                    def.RequiresSearch + def.RequiresCombat + def.RequiresSurvival, 0,
                    $"{def.Id}: 아무 조건도 없다 — 누구를 보내든 결과가 같다");
        }

        /// <summary>입문 지역에도 사건이 있어야 한다. 첫 파견이 목록 한 장이면 첫인상이 그걸로 끝난다.</summary>
        [Test]
        public void EntryTier_HasEventsToo()
        {
            int count = 0;
            foreach (var def in _data.ExpeditionEvents) if (def.MinTier <= 1) count++;

            Assert.Greater(count, 2, "티어 1 에서 나올 수 있는 사건이 너무 적다");
        }

        // ── 팀이 결과를 바꾸는가 ─────────────────────────────────

        /// <summary><b>이 테스트가 이 기능의 존재 이유다.</b></summary>
        [Test]
        public void TheSameRoll_ReadsDifferentlyForDifferentTeams()
        {
            var map = EntryMap();
            int weakPasses = 0, strongPasses = 0, rolled = 0;

            for (uint seed = 1; seed <= 400; seed++)
            {
                var weakRng = EventRng(seed);
                var strongRng = EventRng(seed);

                var weak = ExpeditionEvents.Roll(_data, map, ref weakRng, 3, 3, 3);
                var strong = ExpeditionEvents.Roll(_data, map, ref strongRng, 30, 30, 30);

                if (weak.IsNone) continue;

                rolled++;
                Assert.AreEqual(weak.Id, strong.Id, "같은 시드면 같은 사건이 나와야 한다");
                if (weak.Passed) weakPasses++;
                if (strong.Passed) strongPasses++;
            }

            Assert.Greater(rolled, 30, "400번을 굴렸는데 사건이 거의 안 났다");
            Assert.Greater(strongPasses, weakPasses,
                "약한 팀과 강한 팀의 결과가 같다 — 그러면 누구를 보내든 상관이 없다");
            Assert.Less(weakPasses, rolled, "약한 팀이 전부 넘기면 요구 능력치가 무의미하다");
        }

        /// <summary>혼자 보낸 티어1 스캐브가 전부 실패하면, 초반 내내 나쁜 소식만 듣는다.</summary>
        [Test]
        public void ALoneStarterScav_SometimesSucceeds()
        {
            var map = EntryMap();
            int passes = 0, rolled = 0;

            for (uint seed = 1; seed <= 400; seed++)
            {
                var rng = EventRng(seed);
                var ev = ExpeditionEvents.Roll(_data, map, ref rng, 4, 4, 4);   // 티어1 한 명 수준
                if (ev.IsNone) continue;

                rolled++;
                if (ev.Passed) passes++;
            }

            Assert.Greater(rolled, 0);
            Assert.Greater(passes, 0, "혼자 보낸 사람이 한 번도 못 넘기면 초반이 나쁜 소식뿐이다");
        }

        /// <summary>같은 시드는 같은 결과. 껐다 켜서 사건을 다시 굴릴 수 없어야 한다.</summary>
        [Test]
        public void SameSeed_SameEvent()
        {
            var map = EntryMap();

            for (uint seed = 1; seed <= 50; seed++)
            {
                var a = EventRng(seed);
                var b = EventRng(seed);

                var first = ExpeditionEvents.Roll(_data, map, ref a, 10, 10, 10);
                var second = ExpeditionEvents.Roll(_data, map, ref b, 10, 10, 10);

                Assert.AreEqual(first.Id, second.Id);
                Assert.AreEqual(first.Passed, second.Passed);
            }
        }

        /// <summary>사건이 매번 나면 그건 사건이 아니라 절차다.</summary>
        [Test]
        public void NothingHappens_MoreOftenThanNot_OnEntryMaps()
        {
            var map = EntryMap();
            int quiet = 0;

            for (uint seed = 1; seed <= 400; seed++)
            {
                var rng = EventRng(seed);
                if (ExpeditionEvents.Roll(_data, map, ref rng, 8, 8, 8).IsNone) quiet++;
            }

            Assert.Greater(quiet, 200, "입문 지역에서 사건이 너무 자주 난다");
        }

        [Test]
        public void HigherTierMaps_AreMoreEventful()
        {
            MapDef low = EntryMap(), high = null;
            foreach (var map in _data.Maps) if (high == null || map.Tier > high.Tier) high = map;
            if (high == null || high.Tier <= low.Tier) Assert.Ignore("티어가 갈리는 지역이 없다");

            Assert.Greater(EventRate(high), EventRate(low), "위험한 곳일수록 무슨 일이 나야 한다");
        }

        private double EventRate(MapDef map)
        {
            int hits = 0;
            for (uint seed = 1; seed <= 600; seed++)
            {
                var rng = EventRng(seed);
                if (!ExpeditionEvents.Roll(_data, map, ref rng, 8, 8, 8).IsNone) hits++;
            }
            return hits / 600.0;
        }

        // ── 실제로 보고서에 닿는가 ───────────────────────────────

        /// <summary>
        /// 규칙이 맞아도 배선이 빠지면 아무 소용이 없다 — 파견을 실제로 보내서
        /// 사건이 복귀 보고까지 오는지 본다. (manualSteps 가 파서에 연결 안 돼 있던 것과 같은 부류)
        /// </summary>
        [Test]
        public void Events_ReachTheReturnReport()
        {
            var map = EntryMap();
            var clock = new TestClock(new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero));
            int withEvent = 0;

            for (uint i = 1; i <= 60; i++)
            {
                var save = ProbeSave(clock.UtcNow, i);
                new ExpeditionSystem().Depart(save, _data, map.Id, new[] { "sc_probe" }, clock.UtcNow);

                var probeClock = new TestClock(clock.UtcNow);
                probeClock.Advance(TimeSpan.FromDays(1));

                var report = new OfflineResolver(probeClock,
                    new List<ITimelineSystem> { new ExpeditionSystem() }).Resolve(save, _data);

                foreach (var result in report.Expeditions)
                    if (!string.IsNullOrEmpty(result.EventId)) withEvent++;
            }

            Assert.Greater(withEvent, 0,
                "60번을 보냈는데 보고서에 사건이 한 번도 안 실렸다 — 배선이 빠졌다");
        }

        private GameSave ProbeSave(DateTimeOffset now, uint counter)
        {
            var save = new GameSave
            {
                SavedAt = now,
                RngCounter = counter,
                Player = new PlayerState
                {
                    CreatedAt = now, Money = 10_000_000, Level = 20,
                    EmployerNpcId = GameSession.DefaultEmployerNpcId,
                },
            };
            save.Warehouse.Capacity = 999;
            save.NpcTrust[GameSession.DefaultEmployerNpcId] = 100;

            var scav = new ScavState
            {
                Uid = "sc_probe", Name = "시험체", Search = 5, Combat = 5, Survival = 5,
                Status = ScavStatus.Idle, HiredAt = now,
            };
            scav.Equipment["Weapon"] = "MEL01";
            save.Scavs.Add(scav);
            return save;
        }

        [Test]
        public void UnknownEvent_IsHarmless()
        {
            var unknown = new RolledEvent { Id = "EV_DOES_NOT_EXIST", Passed = true };

            Assert.AreEqual(0, ExpeditionEvents.LootRollDelta(_data, unknown));
            Assert.AreEqual(0, ExpeditionEvents.MoneyDelta(_data, unknown));
            Assert.IsFalse(ExpeditionEvents.CausesAccident(_data, unknown));
        }
    }
}
