using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Expedition
{
    /// <summary>한 판에서 실제로 일어난 사건.</summary>
    public struct RolledEvent
    {
        public string Id;

        /// <summary>보낸 사람들이 감당했는가. 못 하면 나쁜 쪽으로 간다.</summary>
        public bool Passed;

        public bool IsNone => string.IsNullOrEmpty(Id);
    }

    /// <summary>
    /// 탐색 이벤트 — 매복 · 생존자 발견 · 잠긴 창고 (GDD §12, P6).
    ///
    /// <para><b>왜 필요한가.</b> 파견은 이 게임의 중심인데, 지금까지 돌아온 결과는 전리품 목록
    /// 한 장이었다. WORK_PLAN 부록 A 가 "파견 대기가 재미없다"를 근본 위험으로 적어 둔 이유가
    /// 그것이다 — 기다린 보람이 숫자 하나면 기다림은 그냥 지연이다.</para>
    ///
    /// <para><b>핵심은 판정이 팀을 본다는 것이다.</b> 이벤트마다 요구 능력치가 있고,
    /// 보낸 사람들의 합이 그에 못 미치면 나쁜 쪽으로 간다. 그래서 "누구를 보낼까"가
    /// 시급 계산이 아니라 선택이 된다 — 전투가 약한 팀을 위험한 곳에 보내면 매복에서 다치고,
    /// 탐색이 좋은 팀은 잠긴 창고를 연다.</para>
    ///
    /// <para><b>결정론</b>은 다른 모든 것과 같다. 출발 시점 시드에서 갈라낸 스트림 하나를 쓰므로
    /// 앱을 껐다 켜서 사건을 다시 굴릴 수 없다.</para>
    /// </summary>
    public static class ExpeditionEvents
    {
        /// <summary>
        /// 이번 파견에 붙을 사건 하나. 아무 일도 없을 수 있다 —
        /// 매번 사건이 나면 그건 사건이 아니라 절차다.
        /// </summary>
        public static RolledEvent Roll(
            IDataRegistry data, MapDef map, ref Rng rng,
            int searchTotal, int combatTotal, int survivalTotal)
        {
            if (map == null) return default;

            var candidates = new List<ExpeditionEventDef>();
            int totalWeight = 0;

            foreach (var def in data.AllExpeditionEvents)
            {
                if (def.MinTier > map.Tier || (def.MaxTier > 0 && def.MaxTier < map.Tier)) continue;
                candidates.Add(def);
                totalWeight += def.Weight;
            }

            if (candidates.Count == 0 || totalWeight <= 0) return default;

            // 사건이 안 일어날 몫을 먼저 뗀다. 지역이 위험할수록 무언가 일어나기 쉽다.
            double chance = 0.25 + map.Tier * 0.12;
            if (chance > 0.70) chance = 0.70;
            if (!rng.Chance(chance)) return default;

            int pick = rng.NextInt(totalWeight);
            foreach (var def in candidates)
            {
                pick -= def.Weight;
                if (pick >= 0) continue;

                return new RolledEvent
                {
                    Id = def.Id,
                    Passed = Meets(def, searchTotal, combatTotal, survivalTotal),
                };
            }

            return default;
        }

        /// <summary>
        /// 보낸 팀이 이 사건을 감당하는가. 요구치가 0 이면 그 능력은 보지 않는다.
        /// <b>합계로 본다</b> — 여럿이 가면 한 명의 부족을 메울 수 있어야 팀을 짤 이유가 생긴다.
        /// </summary>
        public static bool Meets(ExpeditionEventDef def, int search, int combat, int survival) =>
            search >= def.RequiresSearch &&
            combat >= def.RequiresCombat &&
            survival >= def.RequiresSurvival;

        /// <summary>사건이 회수 횟수를 얼마나 밀어주거나 깎는가.</summary>
        public static int LootRollDelta(IDataRegistry data, RolledEvent rolled)
        {
            var def = Find(data, rolled.Id);
            if (def == null) return 0;
            return rolled.Passed ? def.PassLootRolls : def.FailLootRolls;
        }

        /// <summary>사건이 주는 돈. 실패하면 0 이거나 음수(수리비·치료비)일 수 있다.</summary>
        public static long MoneyDelta(IDataRegistry data, RolledEvent rolled)
        {
            var def = Find(data, rolled.Id);
            if (def == null) return 0;
            return rolled.Passed ? def.PassMoney : def.FailMoney;
        }

        public static int TrustDelta(IDataRegistry data, RolledEvent rolled)
        {
            var def = Find(data, rolled.Id);
            if (def == null) return 0;
            return rolled.Passed ? def.PassTrust : def.FailTrust;
        }

        /// <summary>실패가 사고로 이어지는가 (매복을 못 막은 경우).</summary>
        public static bool CausesAccident(IDataRegistry data, RolledEvent rolled)
        {
            var def = Find(data, rolled.Id);
            return def != null && !rolled.Passed && def.FailCausesAccident;
        }

        public static ExpeditionEventDef Find(IDataRegistry data, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var def in data.AllExpeditionEvents)
                if (def.Id == id) return def;
            return null;
        }

        /// <summary>보고서에 쓸 로케일 키. 성공과 실패는 다른 이야기다.</summary>
        public static string LocaleKey(RolledEvent rolled) =>
            rolled.IsNone ? null : LocaleKey(rolled.Id, rolled.Passed);

        /// <summary>
        /// 리포트에 남은 사건(<c>ExpeditionResult.EventId</c> + <c>EventPassed</c>)으로 키를 만든다.
        ///
        /// <para>이 조립을 화면이 직접 하고 있었다 — <c>_PASS</c>/<c>_FAIL</c> 규칙이 두 군데
        /// 있었다는 뜻이고, 한쪽만 고치면 조용히 어긋난다. 키를 만드는 곳은 한 곳이어야 한다.</para>
        /// </summary>
        public static string LocaleKey(string eventId, bool passed) =>
            string.IsNullOrEmpty(eventId) ? null : eventId + (passed ? "_PASS" : "_FAIL");
    }
}
