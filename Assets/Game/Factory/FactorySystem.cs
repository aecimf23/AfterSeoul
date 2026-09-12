using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Factory
{
    /// <summary>
    /// 공장 제작 큐.
    ///
    /// 파견과 같은 규칙을 쓴다. 큐에 넣는 순간 시드를 확정하고, 품질은 그 시드의 함수다.
    /// 완성 직전에 앱을 껐다 켜서 품질을 다시 굴리는 짓이 성립하지 않는다.
    /// </summary>
    public sealed class FactorySystem : ITimelineSystem
    {
        public string Name => "Factory";

        /// <summary>
        /// 오프라인 생산 상한. 이보다 오래 비워도 그 이상은 쌓이지 않는다.
        ///
        /// 무제한이면 "일주일 뒤에 한 번 접속"이 최적 전략이 되어 매일 접속할 이유가 사라진다.
        /// 일일 의뢰(GDD §5)가 접속 동기를 만드는데 공장이 그걸 무력화하면 안 된다.
        /// </summary>
        public System.TimeSpan OfflineProductionCap { get; set; } = System.TimeSpan.FromHours(12);

        /// <summary>
        /// 오프라인 상한. 데이터 값이 우선이고, 지원계약이 있으면 늘어난다 (GDD §11).
        ///
        /// <para>계약이 상한을 <b>없애지는</b> 않는다. 무제한이면 "일주일 뒤에 한 번 접속"이
        /// 최적 전략이 되는데, 그건 돈 낸 사람에게도 나쁜 게임이다.</para>
        /// </summary>
        private System.TimeSpan CapFor(GameSave save, IDataRegistry data, System.DateTimeOffset now)
        {
            var s = data != null && data.Balance != null ? data.Balance.Station : null;
            if (s == null || s.OfflineCapHours <= 0) return OfflineProductionCap;

            return Support.OfflineCap(save, data, now);
        }

        /// <summary>제작을 큐에 넣는다. 재료를 즉시 차감한다.</summary>
        public CraftJob Enqueue(GameSave save, IDataRegistry data, string recipeId, System.DateTimeOffset now)
        {
            var recipe = data.GetRecipe(recipeId);
            if (recipe == null) return null;
            if (recipe.StationLevel > save.Factory.StationLevel) return null;
            if (ActiveJobs(save) >= QueueCapacity(save, now)) return null;

            // 재료 확인 → 차감. 하나라도 모자라면 아무것도 차감하지 않는다.
            foreach (var input in recipe.Inputs)
                if (Warehouse.CountOf(save.Warehouse, input.ItemId) < input.Count) return null;
            foreach (var input in recipe.Inputs)
                Warehouse.TryRemove(save.Warehouse, input.ItemId, input.Count);

            var job = new CraftJob
            {
                RecipeId = recipeId,
                StartedAt = now,
                CompletesAt = now.AddSeconds(recipe.WorkSeconds * SpeedDivisor(save, data)),
                Seed = save.TakeSeed(),
                Collected = false,
            };
            save.Factory.Queue.Add(job);
            return job;
        }

        /// <summary>
        /// 동시에 걸어둘 수 있는 작업 수 = 작업대 레벨 + 지원계약 보너스 (GDD §11).
        /// 계약은 <b>얹을</b> 뿐 작업대 레벨을 대신하지 않는다.
        ///
        /// <para><b>시각을 받는 판본만 남긴다.</b> 한동안 <c>QueueCapacity(save)</c> 오버로드가
        /// 같이 있었는데, 그건 계약을 안 보는 옛 계산이라 화면이 그걸 부르고 있었다 —
        /// 계약을 끊어둔 사람에게는 값이 같아서 아무도 눈치채지 못했고, 계약이 있으면
        /// 화면은 "큐가 가득 찼다"고 하는데 <see cref="Enqueue"/> 는 받아주는 상태가 됐다.
        /// 같은 값을 두 가지 방법으로 구할 수 있으면 언젠가 반드시 갈라진다.
        /// 편한 오버로드를 없애는 게 주석보다 확실하다.</para>
        /// </summary>
        public static int QueueCapacity(GameSave save, System.DateTimeOffset now) =>
            save.Factory.StationLevel + Support.QueueCapacityBonus(save, now);

        /// <summary>
        /// 아직 수령하지 않은 작업 수. <c>Queue.Count</c> 가 아닌 이유는, 완료됐지만 아직
        /// 정리되지 않은 작업(<c>Collected</c>)이 칸을 잡고 있으면 레벨을 올려도 늘지 않아
        /// "업그레이드했는데 아무 변화가 없다"로 보이기 때문이다.
        /// </summary>
        public static int ActiveJobs(GameSave save)
        {
            int n = 0;
            foreach (var job in save.Factory.Queue) if (!job.Collected) n++;
            return n;
        }

        /// <summary>작업대 레벨이 오르면 제작이 빨라진다. 1.0 / 0.85 / 0.72 …</summary>
        public static double SpeedDivisor(GameSave save, IDataRegistry data)
        {
            var s = data != null && data.Balance != null ? data.Balance.Station : null;
            double per = s != null && s.SpeedPerLevel > 0 ? s.SpeedPerLevel : 0.85;

            double d = 1.0;
            for (int i = 1; i < save.Factory.StationLevel; i++) d *= per;
            return d;
        }

        public IEnumerable<TimedEvent> CollectEvents(ResolveWindow window, ResolveContext ctx)
        {
            var factory = ctx.Save.Factory;

            // 오프라인 상한: 너무 오래 비웠으면 그 시점부터만 친다.
            var cap = CapFor(ctx.Save, ctx.Data, window.To);
            var effectiveFrom = window.From;
            if (window.To - effectiveFrom > cap) effectiveFrom = window.To - cap;

            foreach (var job in factory.Queue)
            {
                if (job.Collected) continue;
                if (job.CompletesAt > window.To) continue;

                var captured = job;
                yield return new TimedEvent(
                    captured.CompletesAt < effectiveFrom ? effectiveFrom : captured.CompletesAt,
                    EventOrder.FactoryOutput,
                    "craft:" + captured.RecipeId + "@" + captured.Seed.ToString("x8"),
                    c => ApplyCraft(c, captured));
            }
        }

        private void ApplyCraft(ResolveContext ctx, CraftJob job)
        {
            if (job.Collected) return;

            var recipe = ctx.Data.GetRecipe(job.RecipeId);
            if (recipe == null) { job.Collected = true; return; }

            var rng = Rng.For(job.Seed, "quality");
            var quality = RollQuality(ref rng, ctx.Save.Factory.StationLevel);

            // 산출 개수 규칙은 작업대(손)와 같은 것을 쓴다. 두 군데에 따로 두면
            // "손으로 만들 때와 큐로 만들 때 개수가 다르다"가 되고, 그건 설명할 수 없다.
            int count = Workbench.OutputCountFor(ctx.Data, recipe, quality);
            if (quality == CraftQuality.Failed) count = 0;

            int overflow = 0;
            if (count > 0)
                overflow = Warehouse.TryAdd(ctx.Save.Warehouse, ctx.Data, recipe.OutputItemId, count);

            int stored = count - overflow;
            if (stored > 0) ctx.Report.AddGain(recipe.OutputItemId, stored);
            if (overflow > 0) ctx.Report.Overflowed.Add(new ItemStack(recipe.OutputItemId, overflow));

            job.Collected = true;
            ctx.Report.Crafts.Add(new CraftResult
            {
                RecipeId = job.RecipeId,
                OutputItemId = recipe.OutputItemId,
                Count = stored,
                Quality = quality,
                CompletedAt = job.CompletesAt,
            });
        }

        /// <summary>
        /// 품질 4단계. 작업대 레벨이 높을수록 실패가 줄고 우수가 늘어난다.
        /// 품질이 영향을 주는 곳은 판매가·제작 성공률·의뢰 만족도 셋뿐이다(GDD §6).
        /// 더 늘리지 않는다 — 복잡한 제작 시뮬레이션은 이 게임의 재미가 아니다.
        /// </summary>
        private static CraftQuality RollQuality(ref Rng rng, int stationLevel)
        {
            int roll = rng.NextInt(100) + (stationLevel - 1) * 5;
            if (roll < 10) return CraftQuality.Failed;
            if (roll < 60) return CraftQuality.Normal;
            if (roll < 90) return CraftQuality.Good;
            return CraftQuality.Excellent;
        }

        // ── 직접 노동 (플레이어 조작, 정산 아님) ─────────────────────

        /// <summary>
        /// 미니게임 점수(0~1)를 품질로 바꾼다. 경계는 <c>balance.laborGradeThresholds</c>.
        /// 판정 규칙을 UI 가 따로 갖지 않게 여기 둔다 — UI 가 도메인 규칙을 중복 구현하면 반드시 어긋난다.
        /// </summary>
        public static CraftQuality GradeManualWork(IDataRegistry data, double score)
        {
            var t = data.Balance.LaborGradeThresholds;
            if (score < t[0]) return CraftQuality.Failed;
            if (score < t[1]) return CraftQuality.Normal;
            if (score < t[2]) return CraftQuality.Good;
            return CraftQuality.Excellent;
        }

        // 직접 노동의 보수 지급은 사라졌다. 작업대가 돈이 아니라 물건을 만들고
        // (Factory/Workbench.cs), 돈은 그 물건을 팔아서 번다. 두드려도 아무것도 쌓이지
        // 않으면 그건 놀이가 아니라 노동이다 — 실제로 그렇게 느껴진다는 지적을 받았다.


        /// <summary>완료돼 수령된 작업을 큐에서 치운다. 정산 후 한 번 부른다.</summary>
        public static void PruneCollected(GameSave save)
        {
            save.Factory.Queue.RemoveAll(j => j.Collected);
        }
    }
}
