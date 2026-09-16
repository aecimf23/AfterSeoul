using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Factory
{
    /// <summary>
    /// 작업대 성장 — GDD §6 의 <c>수동 작업 → 작업대 개선 → 보조 인력 고용 → 반자동 → 자동</c>.
    ///
    /// <para><b>이게 없어서 그 경로 전체가 죽어 있었다.</b> <c>StationLevel</c> 은 1 로 선언만 돼 있고
    /// 어디서도 올라가지 않았고, <c>AutoLevel</c> 은 읽는 코드가 아예 없었다. 레벨업이 없어서
    /// 게임이 1티어에 얼어 있던 것과 같은 종류다 — 값을 쓰는 코드는 다 있는데 그 값을 <b>바꾸는</b>
    /// 코드가 없으면, 테스트는 전부 통과하고 게임만 멈춰 있다.</para>
    ///
    /// <para>레벨이 여는 것은 넷이다: 제작 속도, 큐 슬롯 수, 상위 레시피, 큐 품질.
    /// 넷 다 이미 <see cref="FactorySystem"/> 이 <c>StationLevel</c> 을 읽어 처리하고 있었다.</para>
    /// </summary>
    public static class Station
    {
        public static int MaxLevel(IDataRegistry data) => Tuning(data).MaxLevel;

        public static bool IsMaxLevel(GameSave save, IDataRegistry data) =>
            save.Factory.StationLevel >= MaxLevel(data);

        /// <summary>다음 레벨로 올리는 값. 이미 최고면 0.</summary>
        public static long UpgradeCost(GameSave save, IDataRegistry data)
        {
            if (IsMaxLevel(save, data)) return 0;

            var t = Tuning(data);
            return (long)System.Math.Round(
                t.UpgradeBaseCost * System.Math.Pow(save.Factory.StationLevel, t.UpgradeCostExponent));
        }

        public static string UpgradeBlockReason(GameSave save, IDataRegistry data)
        {
            if (IsMaxLevel(save, data)) return Loc.Text("작업대를 더 손볼 데가 없습니다");

            long cost = UpgradeCost(save, data);
            if (save.Player.Money < cost) return Loc.Text("{0:N0}원이 더 필요합니다" , cost - save.Player.Money);

            return null;
        }

        public static bool TryUpgrade(GameSave save, IDataRegistry data)
        {
            if (UpgradeBlockReason(save, data) != null) return false;

            save.Player.Money -= UpgradeCost(save, data);
            save.Factory.StationLevel++;
            return true;
        }

        /// <summary>
        /// 그 레벨이 무엇을 여는지 한 줄. 값을 보여주면서 무엇을 사는지 안 알려주면
        /// 플레이어는 그냥 숫자가 오르는 걸 사게 된다.
        /// </summary>
        public static string UnlockedAt(int level, IDataRegistry data)
        {
            var parts = new List<string>
            {
                Loc.Text("제작 큐 {0}칸" , level),
                Loc.Text("제작 속도 {0:P0} 단축" , 1.0 - System.Math.Pow(Tuning(data).SpeedPerLevel, level - 1)),
            };

            foreach (var recipe in data.AllRecipes)
                if (recipe.StationLevel == level)
                    parts.Add(Loc.Text("새 도면: {0}" , Loc.ItemName(recipe.OutputItemId)));

            if (level == Assist(data).UnlockStationLevel) parts.Add(Loc.Text("보조 인력 고용"));

            return string.Join("   ·   ", parts.ToArray());
        }

        // ── 보조 인력 ────────────────────────────────────────────

        public static int MaxAssistants(IDataRegistry data) => Assist(data).MaxCount;

        public static bool AssistantsUnlocked(GameSave save, IDataRegistry data) =>
            save.Factory.StationLevel >= Assist(data).UnlockStationLevel;

        /// <summary>한 명 더 쓰는 값. 사람이 늘수록 가파르게 오른다 — 자동이 공짜가 되면 안 된다.</summary>
        public static long AssistantCost(GameSave save, IDataRegistry data)
        {
            var a = Assist(data);
            if (save.Factory.AutoLevel >= a.MaxCount) return 0;

            return (long)System.Math.Round(
                a.HireBaseCost * System.Math.Pow(save.Factory.AutoLevel + 1, a.HireCostExponent));
        }

        public static string HireAssistantBlockReason(GameSave save, IDataRegistry data)
        {
            var a = Assist(data);

            if (!AssistantsUnlocked(save, data))
                return Loc.Text("작업대 {0}단계부터 사람을 쓸 수 있습니다" , a.UnlockStationLevel);

            if (save.Factory.AutoLevel >= a.MaxCount) return Loc.Text("더 쓸 자리가 없습니다");

            long cost = AssistantCost(save, data);
            if (save.Player.Money < cost) return Loc.Text("{0:N0}원이 더 필요합니다" , cost - save.Player.Money);

            return null;
        }

        public static bool TryHireAssistant(GameSave save, IDataRegistry data)
        {
            if (HireAssistantBlockReason(save, data) != null) return false;

            save.Player.Money -= AssistantCost(save, data);
            save.Factory.AutoLevel++;

            // 첫 사람을 쓰는데 만들 것이 안 정해져 있으면 아무 일도 일어나지 않는다.
            // 그건 돈만 나가고 아무 변화가 없는 것으로 보인다 — 만들 수 있는 것 하나를 미리 잡아준다.
            if (string.IsNullOrEmpty(save.Factory.AutoRecipeId))
                foreach (var recipe in Workbench.AvailableRecipes(save, data))
                {
                    save.Factory.AutoRecipeId = recipe.Id;
                    break;
                }

            return true;
        }

        /// <summary>보조 인력이 만들 것을 정한다. 만들 수 없는 레시피는 거절한다.</summary>
        public static bool TrySetAutoRecipe(GameSave save, IDataRegistry data, string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId)) { save.Factory.AutoRecipeId = null; return true; }

            var recipe = data.GetRecipe(recipeId);
            if (recipe == null) return false;
            if (recipe.StationLevel > save.Factory.StationLevel) return false;

            save.Factory.AutoRecipeId = recipeId;
            return true;
        }

        // ── 튜닝 값 ──────────────────────────────────────────────

        private static StationTuning Tuning(IDataRegistry data) =>
            data.Balance.Station ?? new StationTuning();

        private static AssistantTuning Assist(IDataRegistry data) =>
            data.Balance.Assistant ?? new AssistantTuning();
    }
}
