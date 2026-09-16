using System.Collections.Generic;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 특성 id → 화면에 보일 이름.
    ///
    /// <para><b>임시다.</b> 본편 로케일에는 AFTER SEOUL 전용 특성 키가 없어서
    /// <see cref="Core.Loc"/> 에 넣으면 <c>TRAIT_TR_COWARD_NAME</c> 같은 키가 그대로 화면에 뜬다.
    /// 표 하나로 막아두고, P6 로컬라이즈에서 전용 로케일 파일로 옮긴다.</para>
    ///
    /// <para>모르는 id 는 id 를 그대로 돌려준다 — 조용히 빈칸이 되면
    /// 데이터에 특성을 추가하고 표를 안 고친 걸 알아차릴 수 없다.</para>
    /// </summary>
    public static class TraitNames
    {
        private static readonly Dictionary<string, string> Table = new Dictionary<string, string>
        {
            { "TR_YONGSAN_NATIVE", "용산 토박이" },
            { "TR_COWARD",         "겁쟁이" },
            { "TR_MEDIC",          "위생병" },
        };

        public static string Of(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string name;
            return Table.TryGetValue(id, out name) ? AfterSeoul.Core.Loc.Text(name) : id;
        }

        public static string Join(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0) return "";
            var parts = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) parts[i] = Of(ids[i]);
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// 이름 옆에 붙는 한 줄 설명. <b>데이터에서 만든다</b> — 손으로 적으면
        /// 수치를 조정한 날 화면과 규칙이 갈라진다.
        ///
        /// <para>이게 없는 동안 화면은 "특성 겁쟁이"라고만 적었다. 그게 무슨 뜻인지 알 길이
        /// 없으니 특성을 보고 사람을 고를 수가 없었고, 마침 <b>효과 자체도 적용되지 않고 있었다.</b></para>
        /// </summary>
        public static string DescribeOf(Core.ScavTraitDef def)
        {
            if (def == null) return "";

            string good;
            switch (def.EffectType)
            {
                case "mapBonus":
                    good = AfterSeoul.Core.Loc.Text("{0}에서 회수 {1}", Core.Loc.MapName(def.EffectMapId), Pct(def.EffectValue));
                    break;
                case "fleeChance":
                    good = AfterSeoul.Core.Loc.Text("사고에서 빠져나갈 확률 {0}", Pct(def.EffectValue));
                    break;
                case "injurySurvival":
                    good = AfterSeoul.Core.Loc.Text("피해 강등 확률 {0}", Pct(def.EffectValue));
                    break;
                default:
                    good = "";
                    break;
            }

            string bad = def.PenaltyType == "lootMultiplier"
                ? AfterSeoul.Core.Loc.Text("회수 {0}", Pct(def.PenaltyValue))
                : "";

            if (good.Length == 0) return bad;
            return bad.Length == 0 ? good : good + ", " + bad;
        }

        private static string Pct(double v) =>
            (v >= 0 ? "+" : "−") + System.Math.Abs(v * 100).ToString("0.#") + "%";
    }
}
