namespace AfterSeoul.Core
{
    /// <summary>첫 30분에 한 번씩 거쳐야 하는 자리 (GDD §16).</summary>
    public enum TutorialStep
    {
        /// <summary>빈손. 재료 없이 도는 레시피가 유일한 입구다.</summary>
        MakeSomething,

        /// <summary>물건은 있는데 돈이 없다. 만든 건 팔아야 돈이 된다.</summary>
        SellIt,

        /// <summary>돈이 모였다. 사람을 써야 파견이 열린다.</summary>
        HireScav,

        /// <summary>사람이 놀고 있다. 내보내야 물자가 들어온다.</summary>
        Depart,

        /// <summary>나가 있다. 이 게임은 기다리는 게임이라는 걸 여기서 처음 배운다.</summary>
        Wait,

        /// <summary>납품할 수 있다. 루프의 마지막 칸.</summary>
        Deliver,

        // ── 루프 밖 (GDD §15) ──
        //
        // 아래 둘은 순서대로 거치는 칸이 아니라 <b>사고가 났을 때만</b> 뜬다.
        // 그래서 진행 표시("3/6")에서 빠진다 — IndexOf 가 0 을 돌려준다.

        /// <summary>
        /// 다쳐서 나갈 수 없다. 치료해야 한다.
        ///
        /// <para>치료가 생기기 전에는 이 자리가 없었고, 안내는 "다친 사람뿐이면 기다릴 일"이라며
        /// <see cref="Done"/> 으로 빠졌다. 그런데 <b>기다려도 낫지 않았다</b> — 부상에서 나오는
        /// 길이 게임에 없었기 때문이다. 초보자는 안내가 사라진 채로 막혔다.</para>
        /// </summary>
        Treat,

        /// <summary>치료 중이라 할 수 있는 게 없다. 기다리는 것도 한 수라는 걸 여기서 배운다.</summary>
        Recovering,

        /// <summary>한 바퀴 돌았다. 더 안내하지 않는다.</summary>
        Done,
    }

    /// <summary>
    /// 첫 30분 안내 (GDD §16).
    ///
    /// <para><b>대본을 재생하지 않는다.</b> 단계를 세이브에 저장해 두고 하나씩 넘기는 방식이면,
    /// 순서를 벗어난 플레이어(먼저 팔아버렸다든가, 앱을 껐다 켰다든가)에게서 반드시 어긋난다.
    /// 대신 <b>지금 상태를 보고 다음에 할 일을 되묻는다</b> — 파견 결과를 시각 순으로 다시 계산하는
    /// 것과 같은 원리다. 어긋날 상태를 아예 갖지 않으면 어긋나지 않는다.</para>
    ///
    /// <para>그래서 플레이어가 안내를 무시하고 딴 짓을 해도 화면이 조용히 따라온다.
    /// "튜토리얼을 건너뛸 수 있는가"를 따로 만들 필요도 없다 — 한 바퀴를 돌면 저절로 사라진다.</para>
    /// </summary>
    public static class Tutorial
    {
        /// <summary>
        /// 지금 무엇을 할 차례인가.
        ///
        /// <para>판정 순서가 곧 우선순위다. 사람이 나가 있으면 그게 제일 위다 —
        /// 기다리는 중에 "사람을 고용하세요"가 떠 있으면 안내가 아니라 잔소리가 된다.</para>
        /// </summary>
        public static TutorialStep Current(GameSave save, IDataRegistry data)
        {
            if (save == null) return TutorialStep.Done;

            // 한 바퀴 돈 사람에게는 아무것도 띄우지 않는다. 납품이 루프의 마지막 칸이다.
            if (save.Quests.CompletedIds.Count > 0) return TutorialStep.Done;

            foreach (var exp in save.Expeditions)
                if (!exp.Resolved) return TutorialStep.Wait;

            if (CanDeliverSomething(save, data)) return TutorialStep.Deliver;

            foreach (var scav in save.Scavs)
                if (scav.Status == ScavStatus.Idle) return TutorialStep.Depart;

            // 사람이 하나도 없을 때만 고용을 권한다.
            if (save.Scavs.Count == 0)
            {
                if (save.Player.Money >= HireHint(data)) return TutorialStep.HireScav;
                if (HasAnythingToSell(save, data)) return TutorialStep.SellIt;
                return TutorialStep.MakeSomething;
            }

            // 다친 사람뿐이다.
            //
            // 예전에는 여기서 "기다릴 일"이라며 Done 으로 빠졌다. 그런데 <b>기다려도 낫지 않았다</b> —
            // 부상에서 나오는 길이 게임에 없었기 때문이다. 안내가 사라진 자리에서 초보자는
            // 무엇을 해야 할지 알 수 없었다. 치료가 생긴 지금은 그게 다음에 할 일이다.
            bool treating = false;
            foreach (var scav in save.Scavs)
            {
                if (scav.Status == ScavStatus.Injured) return TutorialStep.Treat;
                if (scav.Status == ScavStatus.Treating) treating = true;
            }
            if (treating) return TutorialStep.Recovering;

            return TutorialStep.Done;
        }

        public static bool IsDone(GameSave save, IDataRegistry data) =>
            Current(save, data) == TutorialStep.Done;

        /// <summary>안내 한 줄. 무엇을 하라는 것인지와 <b>왜</b>인지를 같이 적는다.</summary>
        public static string HintOf(TutorialStep step)
        {
            switch (step)
            {
                case TutorialStep.MakeSomething:
                    return "공장에서 폐자재를 분류하세요. 재료 없이 만들 수 있는 유일한 것이고, 여기서 첫 밑천이 나옵니다.";
                case TutorialStep.SellIt:
                    return "만든 것을 창고에서 파세요. 물건은 팔아야 돈이 됩니다.";
                case TutorialStep.HireScav:
                    return "인원에서 사람을 쓰세요. 파견은 사람이 있어야 보냅니다.";
                case TutorialStep.Depart:
                    return "탐색에서 명동으로 내보내세요. 물자는 저기서 들어옵니다.";
                case TutorialStep.Wait:
                    return "나가 있습니다. 돌아올 때까지 공장에서 다른 걸 만드세요 — 기다리는 동안이 이 게임의 절반입니다.";
                case TutorialStep.Deliver:
                    return "황 상사에게 납품하세요. 신뢰도가 올라야 새 지역과 상인이 열립니다.";
                case TutorialStep.Treat:
                    return "다쳐서 나갈 수 없습니다. 인원에서 치료하세요 — 그냥 두면 낫지 않습니다.";
                case TutorialStep.Recovering:
                    return "치료 중입니다. 자는 동안에도 회복하니, 그 사이 공장에서 만들어 두세요.";
                default:
                    return "";
            }
        }

        /// <summary>안내를 누르면 갈 곳. 탭 이름과 같아야 한다.</summary>
        public static string TabOf(TutorialStep step)
        {
            switch (step)
            {
                case TutorialStep.MakeSomething:
                case TutorialStep.Wait: return "공장";
                case TutorialStep.SellIt: return "창고";
                case TutorialStep.HireScav: return "인원";
                case TutorialStep.Depart: return "탐색";
                case TutorialStep.Deliver: return "기지";
                case TutorialStep.Treat: return "인원";
                case TutorialStep.Recovering: return "공장";
                default: return null;
            }
        }

        /// <summary>
        /// 진행 표시용. "3/6" 처럼 끝이 보여야 안내가 끝난다는 것도 보인다.
        ///
        /// <para><b>루프 밖의 칸은 0 을 돌려준다</b> (치료·회복). 사고는 순서대로 거치는 단계가
        /// 아니라 옆길이라, 거기에 번호를 붙이면 "5/8 까지 왔다"처럼 읽혀서 진행도가 거짓이 된다.
        /// 화면은 0 이면 진행 표시를 그리지 않는다.</para>
        /// </summary>
        public static int IndexOf(TutorialStep step) =>
            step < TutorialStep.Treat ? (int)step + 1 : 0;

        /// <summary>한 바퀴를 이루는 칸 수. 루프 밖(치료·회복)은 세지 않는다.</summary>
        public const int TotalSteps = (int)TutorialStep.Treat;

        // ── 내부 ─────────────────────────────────────────────────

        /// <summary>제일 싼 고용가. 데이터에서 가져온다 — 화면과 안내가 다른 숫자를 말하면 안 된다.</summary>
        private static long HireHint(IDataRegistry data)
        {
            var pool = data != null ? data.ScavPool : null;
            if (pool == null || pool.Tiers == null || pool.Tiers.Length == 0) return 250_000;

            long cheapest = long.MaxValue;
            foreach (var tier in pool.Tiers)
                if (tier.HireCost < cheapest) cheapest = tier.HireCost;

            return cheapest == long.MaxValue ? 250_000 : cheapest;
        }

        private static bool HasAnythingToSell(GameSave save, IDataRegistry data)
        {
            foreach (var stack in save.Warehouse.Stacks)
            {
                var def = data.GetItem(stack.ItemId);
                if (def != null && def.BasePrice > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// 지금 낼 수 있는 의뢰가 있는가. 진행도 계산은 <c>Quests</c> 가 하는 것과 같아야 하므로
        /// 창고 수량만 직접 보지 않고 요구 조건을 그대로 훑는다.
        /// </summary>
        private static bool CanDeliverSomething(GameSave save, IDataRegistry data)
        {
            var pool = data.GetQuestPool(Employers.QuestPoolId(data, save.Player.EmployerNpcId));
            if (pool == null) return false;

            foreach (var active in save.Quests.Active)
            {
                if (active.Delivered) continue;

                QuestDef def = null;
                foreach (var q in pool) if (q.Id == active.QuestId) { def = q; break; }
                if (def == null) continue;

                bool all = true;
                foreach (var req in def.Requires)
                {
                    int have = !string.IsNullOrEmpty(req.ItemId)
                        ? Inventory.Warehouse.CountOf(save.Warehouse, req.ItemId)
                        : Inventory.Warehouse.CountByTag(save.Warehouse, data, req.Tag);

                    if (have < req.Count) { all = false; break; }
                }
                if (all) return true;
            }
            return false;
        }
    }
}
