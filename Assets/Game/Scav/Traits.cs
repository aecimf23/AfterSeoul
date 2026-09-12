using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Scav
{
    /// <summary>
    /// 스캐브 특성의 효과를 모아 읽는다 (GDD §14).
    ///
    /// <para><b>왜 이제야 있는가.</b> 특성은 고용 시장에서 뽑혀 세이브에 저장되고 인원 화면에
    /// "특성 겁쟁이"라고 표시까지 됐다. 그런데 <c>ScavTraitDef.EffectType</c> 을 읽는 코드가
    /// <b>게임 전체에 한 곳도 없었다.</b> 화면은 있지도 않은 능력을 계속 광고하고 있었고,
    /// 플레이어는 특성을 보고 사람을 고를 이유가 없었다 — 어차피 아무 차이도 없었으니까.</para>
    ///
    /// <para>다섯 번을 같은 식으로 만났다: 레벨, 작업대 레벨, 무기 획득, 부상 치료, 그리고 이것.
    /// <b>값을 읽는 코드가 있다고 그 값이 움직이는 건 아니고, 값을 만드는 코드가 있다고
    /// 그 값이 읽히는 것도 아니다.</b></para>
    ///
    /// <para>모양은 <see cref="Equipment"/> 를 따라간다 — 한 사람 것을 모으고, 팀 것은 그걸 합친다.
    /// 파견 판정은 이 구조체 하나만 보면 되고, 특성이 늘어도 판정 코드는 그대로다.</para>
    /// </summary>
    public static class Traits
    {
        /// <summary>특성이 파견에 미치는 것 전부. 특성이 없으면 전부 0 이다.</summary>
        public struct Effects
        {
            /// <summary>회수 가치 배수의 증감. −0.15 면 15% 덜 가져온다.</summary>
            public double LootMultiplier;

            /// <summary>사고에 휘말렸을 때 빠져나갈 확률. 겁쟁이가 사는 방식이다.</summary>
            public double FleeChance;

            /// <summary>피해 강등 확률에 더해진다 (<see cref="Equipment.LoadoutEffects.SeverityMitigation"/> 와 같은 자리).</summary>
            public double SeverityMitigation;

            /// <summary>회수 가치 배수. 1.0 이 기본.</summary>
            public double LootScale => 1.0 + LootMultiplier;
        }

        /// <summary>
        /// 한 사람의 특성 효과. <paramref name="mapId"/> 는 지역 한정 특성 때문에 필요하다 —
        /// 용산 토박이는 용산에서만 토박이다.
        /// </summary>
        public static Effects EffectsOf(ScavState scav, IDataRegistry data, string mapId)
        {
            var e = new Effects();
            if (scav == null || data == null) return e;

            var pool = data.ScavPool;
            if (pool == null || pool.Traits == null) return e;

            foreach (var traitId in scav.TraitIds)
            {
                var def = Find(pool, traitId);
                if (def == null) continue;

                switch (def.EffectType)
                {
                    case "mapBonus":
                        // 그 동네에서만. 아무 데서나 붙으면 지역 특성이 아니라 그냥 좋은 특성이다.
                        if (def.EffectMapId == mapId) e.LootMultiplier += def.EffectValue;
                        break;

                    case "fleeChance":
                        e.FleeChance += def.EffectValue;
                        break;

                    case "injurySurvival":
                        e.SeverityMitigation += def.EffectValue;
                        break;
                }

                // 대가는 조건 없이 항상 붙는다. 겁쟁이는 안전한 동네에서도 겁쟁이다.
                if (def.PenaltyType == "lootMultiplier") e.LootMultiplier += def.PenaltyValue;
            }

            // 도주가 1.0 이면 사고를 아예 안 겪는다. GDD §15 의 상실이 사건이 되지 못한다.
            if (e.FleeChance > FleeCap) e.FleeChance = FleeCap;

            return e;
        }

        /// <summary>
        /// 팀 전체의 회수 배수.
        ///
        /// <para><b>합이 아니라 평균이다.</b> 합으로 두면 용산 토박이 셋을 보내는 것이
        /// 정답이 되어 팀 구성이 한 가지로 굳는다. 장비의 운·회수 횟수를 평균으로 두는 것과
        /// 같은 이유다.</para>
        /// </summary>
        public static double TeamLootScale(
            GameSave save, IDataRegistry data, IReadOnlyList<string> scavUids, string mapId)
        {
            if (save == null || scavUids == null || scavUids.Count == 0) return 1.0;

            double sum = 0;
            int counted = 0;

            foreach (var uid in scavUids)
            {
                var scav = FindScav(save, uid);
                if (scav == null) continue;

                sum += EffectsOf(scav, data, mapId).LootScale;
                counted++;
            }

            if (counted == 0) return 1.0;

            double scale = sum / counted;

            // 0 이하로 내려가면 파견이 아무것도 못 가져오게 된다. 특성이 아무리 나빠도
            // 나가는 것 자체가 무의미해지면 안 된다.
            return scale < MinLootScale ? MinLootScale : scale;
        }

        /// <summary>도주 확률의 상한. 1.0 이면 사고를 겪지 않아 상실이 사건이 되지 못한다.</summary>
        public const double FleeCap = 0.60;

        /// <summary>회수 배수의 하한.</summary>
        public const double MinLootScale = 0.25;

        public static ScavTraitDef Find(ScavPoolDef pool, string traitId)
        {
            if (pool == null || pool.Traits == null || string.IsNullOrEmpty(traitId)) return null;

            foreach (var t in pool.Traits)
                if (t.Id == traitId) return t;
            return null;
        }

        private static ScavState FindScav(GameSave save, string uid)
        {
            foreach (var s in save.Scavs)
                if (s.Uid == uid) return s;
            return null;
        }
    }
}
