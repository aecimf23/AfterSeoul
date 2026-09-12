namespace AfterSeoul.Core
{
    /// <summary>
    /// 고용주 한 명 (<c>employers.json</c>, GDD §4).
    ///
    /// <para><b>여기에 "이 지역만 열린다" 같은 필드는 없다.</b> GDD §4 의 절대 규칙이
    /// "NPC 선택으로 지역이나 콘텐츠가 영구적으로 잠기면 안 된다"이기 때문이다.
    /// 차이는 전부 숫자 보너스로만 준다 — 그래야 중후반에 누구를 골랐든 같은 곳에 갈 수 있다.</para>
    /// </summary>
    public sealed class EmployerDef
    {
        public string NpcId;

        /// <summary>이 사람이 내는 일일 의뢰 풀.</summary>
        public string QuestPoolId;

        /// <summary>판매가 배수 가산. 0.08 이면 +8%.</summary>
        public double SellPriceBonus;

        /// <summary>사고 시 치명상 완화 가산 (사망→실종→부상 강등 확률).</summary>
        public double SeverityMitigationBonus;

        /// <summary>이 지역에서 회수를 더 한다. 비어 있으면 없음.</summary>
        public string FavoredMapId;
        public int FavoredMapExtraRolls;
    }

    /// <summary>
    /// 고용주 조회와 보너스 적용.
    ///
    /// <para><b>보너스는 한 사람당 하나만 준다.</b> 둘 이상 주면 '정답 고용주'가 생기고,
    /// 그러면 선택이 아니라 함정이 된다. 수치도 체감되되 뒤집지는 않을 만큼으로 잡는다.</para>
    ///
    /// <para>보너스를 읽는 자리가 전부 이 클래스를 거치게 해 둔다. 각 시스템이 직접
    /// <c>employers.json</c> 을 뒤지기 시작하면, 고용주가 없는 세이브(연동 전·테스트)에서
    /// null 검사를 빠뜨리는 곳이 반드시 생긴다.</para>
    /// </summary>
    public static class Employers
    {
        /// <summary>지금 고용주. 없으면 null — 보너스는 전부 0 으로 취급된다.</summary>
        public static EmployerDef Of(GameSave save, IDataRegistry data)
        {
            if (save == null || data == null) return null;
            return Find(data, save.Player.EmployerNpcId);
        }

        public static EmployerDef Find(IDataRegistry data, string npcId)
        {
            if (data == null || string.IsNullOrEmpty(npcId)) return null;

            foreach (var e in data.AllEmployers)
                if (e.NpcId == npcId) return e;

            return null;
        }

        /// <summary>이 고용주의 의뢰 풀 id. 정의가 없으면 예전 규칙(<c>DQP_{npcId}</c>)을 따른다.</summary>
        public static string QuestPoolId(IDataRegistry data, string npcId)
        {
            var def = Find(data, npcId);
            return def != null && !string.IsNullOrEmpty(def.QuestPoolId)
                ? def.QuestPoolId
                : "DQP_" + npcId;
        }

        public static double SellPriceBonus(GameSave save, IDataRegistry data)
        {
            var def = Of(save, data);
            return def != null ? def.SellPriceBonus : 0.0;
        }

        public static double SeverityMitigationBonus(GameSave save, IDataRegistry data)
        {
            var def = Of(save, data);
            return def != null ? def.SeverityMitigationBonus : 0.0;
        }

        /// <summary>이 지역에서 고용주가 얹어주는 회수 횟수.</summary>
        public static int ExtraLootRolls(GameSave save, IDataRegistry data, string mapId)
        {
            var def = Of(save, data);
            if (def == null || string.IsNullOrEmpty(def.FavoredMapId)) return 0;
            return def.FavoredMapId == mapId ? def.FavoredMapExtraRolls : 0;
        }

        /// <summary>
        /// 이 고용주가 무엇을 해주는지 한 줄. 고를 때 보여준다 —
        /// 이름과 초상만 보고 고르게 하면 그건 선택이 아니라 제비뽑기다.
        /// </summary>
        public static string SummaryOf(EmployerDef def)
        {
            if (def == null) return "";

            if (def.SellPriceBonus > 0)
                return $"판매가 +{def.SellPriceBonus:P0}";

            if (def.SeverityMitigationBonus > 0)
                return $"사고 피해 완화 +{def.SeverityMitigationBonus:P0} — 사람을 덜 잃습니다";

            if (!string.IsNullOrEmpty(def.FavoredMapId) && def.FavoredMapExtraRolls > 0)
                return $"{Loc.MapName(def.FavoredMapId)}에서 회수 +{def.FavoredMapExtraRolls}회";

            return "특별한 보너스 없음";
        }
    }
}
