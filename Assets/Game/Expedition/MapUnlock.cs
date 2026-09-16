using AfterSeoul.Core;

namespace AfterSeoul.Expedition
{
    /// <summary>
    /// 지역 해금 판정 (GDD §31, DATA_SCHEMA §1-4 <c>unlockCondition</c>).
    ///
    /// <para><b>판정이 여기 한 곳에만 있다.</b> 화면이 "잠김"이라고 그리는 근거와
    /// <see cref="ExpeditionSystem.Depart"/> 가 출발을 막는 근거가 같은 함수여야 한다.
    /// 둘로 나뉘면 "버튼은 눌리는데 아무 일도 안 일어나는" 상태가 생기고,
    /// 그건 버그 리포트가 안 들어오는 종류의 버그다.</para>
    ///
    /// <para>모르는 조건 타입은 잠긴 것으로 본다. 데이터에 오타가 났을 때
    /// 3티어 지역이 조용히 열려버리는 쪽이 훨씬 나쁘다.</para>
    /// </summary>
    public static class MapUnlock
    {
        public static bool IsUnlocked(GameSave save, MapDef map)
        {
            return LockReason(save, map) == null;
        }

        /// <summary>잠긴 이유. 열려 있으면 null. 화면에 그대로 띄울 수 있는 문장이다.</summary>
        public static string LockReason(GameSave save, MapDef map)
        {
            if (map == null) return Loc.Text("지역 정보를 찾을 수 없습니다");

            var unlock = map.Unlock ?? UnlockDef.Default;
            switch (unlock.Type)
            {
                case "default":
                    return null;

                case "playerLevel":
                    return save.Player.Level >= unlock.Value
                        ? null
                        : Loc.Text("레벨 {0} 필요 (현재 {1})" , unlock.Value, save.Player.Level);

                case "npcTrust":
                {
                    int trust = TrustOf(save, unlock.NpcId);
                    return trust >= unlock.Value
                        ? null
                        : Loc.Text("{0} 신뢰도 {1} 필요 (현재 {2})" , Loc.TraderName(unlock.NpcId), unlock.Value, trust);
                }

                default:
                    // 데이터에 모르는 타입이 들어왔다. 열지 않는다.
                    return Loc.Text("해금 조건을 해석할 수 없습니다 ({0})" , unlock.Type);
            }
        }

        public static int TrustOf(GameSave save, string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || save.NpcTrust == null) return 0;
            int value;
            return save.NpcTrust.TryGetValue(npcId, out value) ? value : 0;
        }
    }
}
