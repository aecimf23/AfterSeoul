using System;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Scav
{
    /// <summary>
    /// 부상 치료 (GDD §15).
    ///
    /// <para><b>왜 이제야 있는가.</b> <c>ScavStatus.Injured</c> 는 파견 사고와 구조 성공에서
    /// 설정되고, 화면과 팀 편성이 그걸 읽어 "못 나간다"고 판정했다. 그런데
    /// <b>그 상태에서 빠져나오는 코드가 어디에도 없었다.</b> <c>ScavStatus.Treating</c> 은
    /// 열거형에 이름만 있고 쓰는 쪽이 0 곳이었다.</para>
    ///
    /// <para>결과적으로 부상 = 영구 퇴출이었다. 명단은 시간이 갈수록 줄기만 하고, 사고를 피하는
    /// 유일한 방법은 파견을 안 보내는 것이 된다 — 방치형에서 최적 전략이 "아무것도 안 하기"가
    /// 되는 건 설계 실패다. <b>값을 읽는 코드가 있다고 그 값이 움직이는 건 아니다.</b></para>
    ///
    /// <para><b>공짜로 낫게 하지는 않는다.</b> 시간이 지나면 저절로 낫게 두면 사고에 아무 대가가
    /// 없어서 장비를 챙길 이유가 사라진다. 돈과 시간을 쓰되, 의료품이 있으면 시간을 줄인다 —
    /// 그러면 전리품으로 나온 의료 아이템에 "팔까 남길까"가 생긴다.</para>
    /// </summary>
    public static class Treatment
    {
        /// <summary>치료비. 티어가 높은 사람일수록 비싸다 — 잃을 것이 큰 만큼.</summary>
        public static long CostFor(ScavState scav, IDataRegistry data)
        {
            var t = Tuning(data);
            int tier = scav == null || scav.Tier < 1 ? 1 : scav.Tier;
            return t.CostPerTier * tier;
        }

        public static TimeSpan DurationFor(ScavState scav, IDataRegistry data, bool withSupplies)
        {
            var t = Tuning(data);
            int tier = scav == null || scav.Tier < 1 ? 1 : scav.Tier;

            double hours = t.HoursPerTier * tier;
            if (withSupplies) hours *= t.SuppliesSpeedup;

            // 0 이 되면 "치료"가 버튼 한 번이 되고, 그러면 사고가 사건이 아니라 절차가 된다.
            if (hours < 0.25) hours = 0.25;
            return TimeSpan.FromHours(hours);
        }

        /// <summary>
        /// 치료에 쓸 수 있는 의료품이 창고에 있으면 그 아이템 id, 없으면 null.
        ///
        /// <para>아이템을 지정해서 고르게 하지 않는다. 종류가 여럿이어도 플레이어가 답할 수 있는
        /// 질문이 아니고("붕대와 진통제 중 무엇을 쓰시겠습니까"), 그 선택이 결과를 바꾸지도 않는다.
        /// <b>가장 싼 것부터 쓴다</b> — 비싼 것을 조용히 태워버리면 그건 손해를 숨기는 것이다.</para>
        /// </summary>
        public static string FindSupplies(GameSave save, IDataRegistry data)
        {
            if (save == null || data == null) return null;

            string best = null;
            long bestPrice = long.MaxValue;

            foreach (var stack in save.Warehouse.Stacks)
            {
                if (stack.Count <= 0) continue;

                var def = data.GetItem(stack.ItemId);
                if (def == null || ItemGroups.Of(def) != ItemGroup.Medical) continue;

                if (def.BasePrice >= bestPrice) continue;
                bestPrice = def.BasePrice;
                best = stack.ItemId;
            }

            return best;
        }

        /// <summary>
        /// 지금 이 사람을 치료할 수 있는가. 못 하면 <b>이유를 돌려준다</b> —
        /// 버튼이 조용히 안 먹는 것이 이 게임에서 제일 흔한 불평이었다.
        /// </summary>
        public static string BlockReason(GameSave save, IDataRegistry data, ScavState scav)
        {
            if (scav == null) return Loc.Text("대상이 없습니다");
            if (scav.Status == ScavStatus.Treating) return Loc.Text("이미 치료 중입니다");
            if (scav.Status != ScavStatus.Injured) return Loc.Text("부상자가 아닙니다");

            long cost = CostFor(scav, data);
            if (save.Player.Money < cost) return Loc.Text("치료비가 부족합니다 ({0:N0}원)" , cost);

            return null;
        }

        /// <summary>
        /// 치료를 시작한다. 끝나는 것은 <see cref="TreatmentSystem"/> 이 시간 순서에 맞춰 처리한다.
        ///
        /// <para>여기서 곧바로 Idle 로 돌리지 않는 이유: 즉시 회복이면 돈만 있으면 사고가
        /// 없던 일이 되고, 오프라인 정산과도 어긋난다 (접속 중에만 나으면 자는 동안은 안 낫는다).</para>
        /// </summary>
        public static bool TryTreat(GameSave save, IDataRegistry data, ScavState scav,
            DateTimeOffset now, bool useSupplies)
        {
            if (BlockReason(save, data, scav) != null) return false;

            string supplyId = useSupplies ? FindSupplies(save, data) : null;

            // 창고에서 먼저 빼고 나서 시간을 정한다. 빼기에 실패했는데 시간만 줄여 두면
            // 의료품 없이 빠른 치료가 된다.
            bool used = supplyId != null && Warehouse.TryRemove(save.Warehouse, supplyId, 1);

            save.Player.Money -= CostFor(scav, data);
            scav.Status = ScavStatus.Treating;
            scav.TreatedAt = now;
            scav.RecoversAt = now + DurationFor(scav, data, used);
            return true;
        }

        /// <summary>치료 진행률 0~1. 화면이 막대를 그리는 데 쓴다. 치료 중이 아니면 0.</summary>
        public static double Progress(ScavState scav, DateTimeOffset now)
        {
            if (scav == null || scav.Status != ScavStatus.Treating) return 0.0;

            double total = (scav.RecoversAt - scav.TreatedAt).TotalSeconds;
            if (total <= 0.0) return 1.0;

            double done = (now - scav.TreatedAt).TotalSeconds / total;
            return done < 0.0 ? 0.0 : done > 1.0 ? 1.0 : done;
        }

        /// <summary>
        /// 회복을 앞당긴다 (보상 광고 등). 치료 중이 아니면 아무 일도 없다.
        ///
        /// <para>끝난 시각보다 더 당기지 않는다 — 과거로 당겨두면 <see cref="TreatmentSystem"/>
        /// 이 정산 구간 앞쪽에서 사건을 만들게 되어, 이미 지난 시각에 회복한 것처럼 기록된다.</para>
        /// </summary>
        public static bool SpeedUp(ScavState scav, DateTimeOffset now, TimeSpan by)
        {
            if (scav == null || scav.Status != ScavStatus.Treating) return false;
            if (by <= TimeSpan.Zero) return false;

            var target = scav.RecoversAt - by;
            scav.RecoversAt = target < now ? now : target;
            return true;
        }

        /// <summary>
        /// 회복 처리. <b>멱등하다</b> — 치료 중이 아니거나 아직 시각이 안 됐으면 아무것도 안 한다.
        /// 정산이 두 번 돌아도 결과가 같아야 한다.
        /// </summary>
        public static bool Recover(ScavState scav, DateTimeOffset at)
        {
            if (scav == null || scav.Status != ScavStatus.Treating) return false;
            if (scav.RecoversAt > at) return false;

            scav.Status = ScavStatus.Idle;
            scav.RecoversAt = default;
            scav.TreatedAt = default;
            return true;
        }

        private static TreatmentTuning Tuning(IDataRegistry data)
        {
            var t = data != null && data.Balance != null ? data.Balance.Treatment : null;
            return t ?? new TreatmentTuning();
        }
    }
}
