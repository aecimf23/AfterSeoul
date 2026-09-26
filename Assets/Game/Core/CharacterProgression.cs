using System;

namespace AfterSeoul.Core
{
    /// <summary>Personal progression from surviving exploration and completing quests.</summary>
    public static class CharacterProgression
    {
        public static CharacterProgressionTuning Tuning(BalanceDef balance) =>
            balance?.CharacterProgression ?? new CharacterProgressionTuning();

        public static long ExpForLevel(int level, BalanceDef balance)
        {
            var curve = Tuning(balance).Curve;
            return level <= 1 ? 0 : (long)Math.Round(curve.BaseExp * Math.Pow(level - 1, curve.Exponent));
        }

        public static int LevelForExp(long exp, BalanceDef balance)
        {
            int level = 1;
            for (int next = 2; next <= Tuning(balance).Curve.MaxLevel; next++)
            {
                if (ExpForLevel(next, balance) > exp) break;
                level = next;
            }
            return level;
        }

        public static int Sync(GameSave save, BalanceDef balance)
        {
            int before = save.Player.CharacterLevel;
            // Existing levels survive tuning changes; rewards never downgrade a character.
            save.Player.CharacterLevel = Math.Max(Math.Max(1, before), LevelForExp(save.Player.CharacterExp, balance));
            return save.Player.CharacterLevel - before;
        }

        /// <summary>Call only inside a successful, single-settlement reward transaction.</summary>
        public static long Award(GameSave save, long amount, BalanceDef balance)
        {
            if (amount <= 0) return 0;
            long before = Math.Max(0, save.Player.CharacterExp);
            long gained = Math.Min(amount, long.MaxValue - before);
            save.Player.CharacterExp = before + gained;
            Sync(save, balance);
            return gained;
        }

        public static long SurvivalReward(int visitedNodes, BalanceDef balance)
        {
            var tuning = Tuning(balance);
            return tuning.SurvivalExp + Math.Max(0, visitedNodes) * tuning.ExpPerExploredNode;
        }

        public static long FollowupReward(int stage, BalanceDef balance) =>
            stage >= 1 ? Tuning(balance).SecondFollowupExp : Tuning(balance).FirstFollowupExp;

        public static long ExpToNextLevel(PlayerState player, BalanceDef balance)
        {
            int level = Math.Max(player.CharacterLevel, LevelForExp(player.CharacterExp, balance));
            return level >= Tuning(balance).Curve.MaxLevel ? 0
                : Math.Max(0, ExpForLevel(level + 1, balance) - player.CharacterExp);
        }

        public static double ProgressInLevel(PlayerState player, BalanceDef balance)
        {
            int level = Math.Max(player.CharacterLevel, LevelForExp(player.CharacterExp, balance));
            if (level >= Tuning(balance).Curve.MaxLevel) return 1;
            long from = ExpForLevel(level, balance), to = ExpForLevel(level + 1, balance);
            return to <= from ? 1 : Math.Max(0, Math.Min(1, (double)(player.CharacterExp - from) / (to - from)));
        }
    }
}
