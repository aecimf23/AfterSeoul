using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Factory
{
    /// <summary>
    /// 보조 인력 — 앱을 꺼 둔 동안에도 정해 둔 것을 계속 만든다 (GDD §6 의 반자동/자동).
    ///
    /// <para><b>큐와 다른 점</b>: 제작 큐는 플레이어가 하나씩 넣어 둔 것이고, 여기는 넣어 둘 필요가
    /// 없다. 사람을 쓴다는 건 "지시를 안 해도 돌아간다"는 뜻이라서, 재료가 있는 한 주기마다
    /// 한 번씩 스스로 만든다.</para>
    ///
    /// <para><b>결정론</b>: 사건마다 시드를 시각에서 만든다 (<see cref="CycleSeed"/>).
    /// 큐 작업은 넣는 순간 <c>TakeSeed</c> 로 시드를 확정하지만, 여기는 정산 중에 생겨나는 일이라
    /// 그 방법을 쓸 수 없다 — 정산이 두 번 돌면 시드가 달라져 결과가 바뀐다. 그래서 "몇 번째 주기인가"
    /// 라는, 다시 계산해도 같은 값이 나오는 것에서 시드를 뽑는다.</para>
    ///
    /// <para><b>오프라인 상한</b>: 아무리 오래 비워도 <c>station.offlineCapHours</c> 치만 쌓인다.
    /// 무제한이면 "일주일 뒤에 한 번 접속"이 최적 전략이 되어 매일 접속할 이유가 사라지고,
    /// 그러면 일일 의뢰(GDD §5)가 만드는 접속 동기를 공장이 무력화한다.</para>
    /// </summary>
    public sealed class AssistantSystem : ITimelineSystem
    {
        public string Name => "Assistant";

        /// <summary>한 번의 정산에서 낼 수 있는 사건 수의 안전장치. 시계가 튀어도 폭주하지 않게.</summary>
        private const int MaxCyclesPerResolve = 500;

        public IEnumerable<TimedEvent> CollectEvents(ResolveWindow window, ResolveContext ctx)
        {
            var save = ctx.Save;
            var factory = save.Factory;

            if (factory.AutoLevel <= 0) yield break;
            if (string.IsNullOrEmpty(factory.AutoRecipeId)) yield break;

            var recipe = ctx.Data.GetRecipe(factory.AutoRecipeId);
            if (recipe == null || recipe.StationLevel > factory.StationLevel) yield break;

            double seconds = recipe.WorkSeconds * SpeedDivisor(factory.StationLevel, ctx.Data);
            if (seconds < 1) seconds = 1;
            var period = TimeSpan.FromSeconds(seconds);

            // 상한 바깥은 없었던 셈 친다.
            // 상한은 공장 큐와 같은 것을 쓴다 (지원계약 포함). 둘이 다르면
            // "큐는 12시간인데 보조 인력은 24시간" 같은 설명 못 할 상태가 생긴다.
            var cap = Support.OfflineCap(save, ctx.Data, window.To);
            var from = window.To - window.From > cap ? window.To - cap : window.From;

            // 이미 정산한 구간은 다시 내보내지 않는다. 다른 시스템은 사건마다 소비 표시가
            // 붙어 있지만(Resolved / Collected / ActiveGameDate) 보조 인력의 작업은 정산 중에
            // 생겨나는 것이라 붙일 데가 없다 — 그래서 "어디까지 했는지"를 시각으로 들고 있는다.
            // 이게 없으면 저장 없이 정산이 두 번 돌 때 같은 시간을 두 번 일한 것이 된다.
            if (factory.LastCollectedAt > from) from = factory.LastCollectedAt;
            if (from >= window.To) yield break;

            // 주기를 절대 시각으로 자른다. 그래야 "몇 번째 주기"가 정산 시점과 무관하게 같다.
            long periodTicks = period.Ticks;
            long firstCycle = from.UtcTicks / periodTicks + 1;
            long lastCycle = window.To.UtcTicks / periodTicks;

            int emitted = 0;
            for (long cycle = firstCycle; cycle <= lastCycle && emitted < MaxCyclesPerResolve; cycle++)
            {
                var at = new DateTimeOffset(cycle * periodTicks, TimeSpan.Zero);

                // 사람 수만큼 동시에 만든다.
                for (int worker = 0; worker < factory.AutoLevel; worker++)
                {
                    long c = cycle;
                    int w = worker;
                    string recipeId = recipe.Id;

                    yield return new TimedEvent(at, EventOrder.FactoryOutput,
                        "assist:" + recipeId + "@" + c.ToString("x") + ":" + w,
                        context => Work(context, recipeId, CycleSeed(c, w)));

                    emitted++;
                }
            }
        }

        /// <summary>
        /// 한 사람이 한 주기 동안 한 일. 재료가 없으면 <b>아무 일도 없다</b> —
        /// 사람을 썼다고 없는 재료가 생기지는 않는다.
        /// </summary>
        private static void Work(ResolveContext ctx, string recipeId, uint seed)
        {
            var recipe = ctx.Data.GetRecipe(recipeId);
            if (recipe == null) return;

            // 재료가 없어 공쳤더라도 시각은 민다 — 그 주기를 다음 정산에서 다시 시도하면
            // "재료를 넣어두자 과거의 시간이 되살아나는" 일이 생긴다.
            if (ctx.EventTime > ctx.Save.Factory.LastCollectedAt)
                ctx.Save.Factory.LastCollectedAt = ctx.EventTime;

            foreach (var input in recipe.Inputs)
                if (Warehouse.CountOf(ctx.Save.Warehouse, input.ItemId) < input.Count) return;
            foreach (var input in recipe.Inputs)
                Warehouse.TryRemove(ctx.Save.Warehouse, input.ItemId, input.Count);

            var rng = Rng.For(seed, "assist");
            var quality = RollQuality(ref rng, ctx.Data);

            int count = Workbench.OutputCountFor(ctx.Data, recipe, quality);
            if (quality == CraftQuality.Failed) count = 0;

            int overflow = count > 0
                ? Warehouse.TryAdd(ctx.Save.Warehouse, ctx.Data, recipe.OutputItemId, count)
                : 0;

            int stored = count - overflow;
            if (stored > 0) ctx.Report.AddGain(recipe.OutputItemId, stored);
            if (overflow > 0) ctx.Report.Overflowed.Add(new ItemStack(recipe.OutputItemId, overflow));

            ctx.Report.Crafts.Add(new CraftResult
            {
                RecipeId = recipeId,
                OutputItemId = recipe.OutputItemId,
                Count = stored,
                Quality = quality,
                CompletedAt = ctx.EventTime,
            });
        }

        /// <summary>
        /// 보조 인력의 품질. <b>사람 손보다 한 수 아래로 고정한다</b> —
        /// 자동이 직접 하는 것보다 잘하면 미니게임이 장식이 되고,
        /// 그러면 이 게임에서 유일하게 손으로 하는 일이 사라진다.
        /// </summary>
        private static CraftQuality RollQuality(ref Rng rng, IDataRegistry data)
        {
            var weights = data.Balance.Assistant.QualityWeights;

            int total = 0;
            foreach (int w in weights) total += w;

            int pick = rng.NextInt(total);
            for (int i = 0; i < weights.Length; i++)
            {
                pick -= weights[i];
                if (pick < 0) return (CraftQuality)i;
            }
            return CraftQuality.Normal;
        }

        /// <summary>
        /// 주기 번호와 사람 번호로 시드를 만든다. 같은 구간을 몇 번을 다시 정산해도 같은 값이다 —
        /// 앱을 껐다 켜서 품질을 다시 굴리는 짓이 성립하지 않는다.
        /// </summary>
        private static uint CycleSeed(long cycle, int worker) =>
            Rng.Derive((uint)(cycle ^ (cycle >> 32)), "assist:" + worker);

        private static double SpeedDivisor(int stationLevel, IDataRegistry data)
        {
            double per = Tuning(data).SpeedPerLevel;
            double d = 1.0;
            for (int i = 1; i < stationLevel; i++) d *= per;
            return d;
        }

        private static StationTuning Tuning(IDataRegistry data) =>
            data.Balance.Station ?? new StationTuning();
    }
}
