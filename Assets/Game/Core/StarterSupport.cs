using System;

namespace AfterSeoul.Core
{
    [Serializable]
    public sealed class StarterSupportState
    {
        public bool PracticeCompleted;
        public bool SaleCompleted;
        // Also set for a paid first hire or a legacy save with recruitment history.
        public bool Closed;
    }

    /// <summary>One practice, one sale, one supported tier-1 contract. Never grants cash.</summary>
    public static class StarterSupport
    {
        public static void Initialize(GameSave save, IDataRegistry data)
        {
            if (save.Starter != null || Employers.Find(data, save.Player.EmployerNpcId) == null) return;
            save.Starter = new StarterSupportState {
                Closed = HasHistory(save) || save.Orientation == null || save.Orientation.Stage != OrientationStage.Pending
            };
        }

        private static bool HasHistory(GameSave save)
        {
            if (save.Scavs.Count > 0 || save.Expeditions.Count > 0 || save.ExploredMapIds.Count > 0) return true;
            foreach (var offer in save.Market.Offers) if (offer.Hired) return true;
            return false;
        }

        public static bool Active(GameSave save) => save.Starter != null && !save.Starter.Closed && !HasHistory(save);
        public static bool Ready(GameSave save) => Active(save) && save.Starter.PracticeCompleted && save.Starter.SaleCompleted;
        public static bool Covers(GameSave save, ScavOffer offer) =>
            offer != null && !offer.Hired && offer.Tier == 1 && Ready(save);

        public static void RecordPractice(GameSave save, string recipeId)
        {
            if (Active(save) && (recipeId == "RCP_SALVAGE" || recipeId == "RCP_VAULT"))
                save.Starter.PracticeCompleted = true;
        }

        public static void RecordProduction(GameSave save)
        {
            if (!Active(save)) return;
            save.Starter.PracticeCompleted = true;
            save.Starter.SaleCompleted = true;
        }

        public static void RecordSale(GameSave save, string itemId, int count)
        {
            if (Active(save) && save.Starter.PracticeCompleted && itemId == "JUNK16" && count > 0)
                save.Starter.SaleCompleted = true;
        }

        public static TutorialStep Next(GameSave save)
        {
            if (Ready(save)) return TutorialStep.HireScav;
            if (save.Starter.PracticeCompleted && Inventory.Warehouse.CountOf(save.Warehouse, "JUNK16") > 0)
                return TutorialStep.SellIt;
            return TutorialStep.MakeSomething;
        }

        public static string Hint(GameSave save)
        {
            switch (Next(save)) {
                case TutorialStep.HireScav: return Loc.Text("3/3 · 첫 동료 선택 — 1티어 1명의 계약금을 고용주가 전액 지원합니다.");
                case TutorialStep.SellIt: return Loc.Text("2/3 · 회수한 고철을 1개 판매하세요. 판매를 마치면 첫 동료의 계약금이 전액 지원됩니다.");
                default: return Loc.Text("1/3 · 무료 회수 실습을 한 판 마치세요. 실패해도 완료되며, 판매 후 첫 동료 1명을 무료로 고용합니다.");
            }
        }
    }
}
