namespace AfterSeoul.Core
{
    /// <summary>
    /// 레벨 (GDD §2 — 성장 축).
    ///
    /// <para><b>레벨은 경험치의 함수다. 올리는 게 아니라 다시 계산한다.</b>
    /// <c>Level++</c> 로 두면 정산이 두 번 돌았을 때 두 번 오르고, 그 버그는
    /// "가끔 레벨이 하나 더 높다"는 형태로 나타나 재현이 안 된다. 이 프로젝트가
    /// 파견·제작·의뢰에서 멱등성을 지키는 것과 같은 이유다 (ARCHITECTURE §3).</para>
    ///
    /// <para>레벨이 막고 있는 것: 지역 해금(구로 5 · 의정부 9), 일일 의뢰 티어,
    /// 고용 시장 티어. 그래서 레벨이 안 오르면 게임 전체가 1티어에 갇힌다.</para>
    /// </summary>
    public static class Leveling
    {
        /// <summary>
        /// <paramref name="level"/> 에 도달하는 데 필요한 누적 경험치.
        /// 레벨 1 은 0 이다.
        /// </summary>
        public static long ExpForLevel(int level, BalanceDef balance)
        {
            if (level <= 1) return 0;

            var c = Curve(balance);
            return (long)System.Math.Round(c.BaseExp * System.Math.Pow(level - 1, c.Exponent));
        }

        /// <summary>경험치로 정해지는 레벨. 상한을 넘지 않는다.</summary>
        public static int LevelForExp(long exp, BalanceDef balance)
        {
            var c = Curve(balance);
            if (exp <= 0) return 1;

            int level = 1;
            for (int n = 2; n <= c.MaxLevel; n++)
            {
                if (ExpForLevel(n, balance) > exp) break;
                level = n;
            }
            return level;
        }

        /// <summary>다음 레벨까지 남은 경험치. 상한이면 0.</summary>
        public static long ExpToNextLevel(long exp, BalanceDef balance)
        {
            int level = LevelForExp(exp, balance);
            if (level >= Curve(balance).MaxLevel) return 0;

            long need = ExpForLevel(level + 1, balance);
            return need > exp ? need - exp : 0;
        }

        /// <summary>이번 레벨 구간의 진행도 0~1. 화면의 막대가 이걸 쓴다.</summary>
        public static double ProgressInLevel(long exp, BalanceDef balance)
        {
            int level = LevelForExp(exp, balance);
            if (level >= Curve(balance).MaxLevel) return 1.0;

            long from = ExpForLevel(level, balance);
            long to = ExpForLevel(level + 1, balance);
            if (to <= from) return 1.0;

            double p = (double)(exp - from) / (to - from);
            return p < 0 ? 0 : p > 1 ? 1 : p;
        }

        /// <summary>
        /// 세이브의 레벨을 경험치에 맞춘다. <b>오른 레벨 수를 돌려준다.</b>
        ///
        /// <para>몇 번을 불러도 결과가 같다 — 두 번째부터는 0 을 돌려준다.
        /// 정산이 중복 실행돼도 레벨이 두 번 오르지 않는 건 이 성질 덕이다.</para>
        ///
        /// <para>레벨이 내려가는 일은 없다. 데이터에서 곡선을 올려 잡으면 기존 세이브의
        /// 레벨이 깎일 수 있는데, 이미 열린 지역이 다시 잠기는 건 버그로만 읽힌다.</para>
        /// </summary>
        public static int Sync(GameSave save, BalanceDef balance)
        {
            if (save == null) return 0;

            int target = LevelForExp(save.Player.Exp, balance);
            if (target <= save.Player.Level) return 0;

            int gained = target - save.Player.Level;
            save.Player.Level = target;
            return gained;
        }

        /// <summary>회수 가치에서 나오는 경험치. 파견을 보내는 것만으로도 성장한다.</summary>
        public static long ExpFromLootValue(long lootValue, BalanceDef balance)
        {
            long per = balance != null && balance.ExpPerLootValue > 0 ? balance.ExpPerLootValue : 5000;
            return lootValue > 0 ? lootValue / per : 0;
        }

        /// <summary>직접 노동 1회의 경험치. 인덱스 = <see cref="CraftQuality"/>.</summary>
        public static long ExpFromLabor(CraftQuality quality, BalanceDef balance)
        {
            var table = balance != null ? balance.ExpByLaborQuality : null;
            int i = (int)quality;
            if (table == null || i < 0 || i >= table.Length) return 0;
            return table[i];
        }

        private static LevelCurveDef Curve(BalanceDef balance)
        {
            var c = balance != null ? balance.LevelCurve : null;
            return c ?? new LevelCurveDef();
        }
    }
}
