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

        /// <summary>제작을 큐에 넣는다. 재료를 즉시 차감한다.</summary>
        public CraftJob Enqueue(GameSave save, IDataRegistry data, string recipeId, System.DateTimeOffset now)
        {
            var recipe = data.GetRecipe(recipeId);
            if (recipe == null) return null;
            if (recipe.StationLevel > save.Factory.StationLevel) return null;
            if (save.Factory.Queue.Count >= QueueCapacity(save)) return null;

            // 재료 확인 → 차감. 하나라도 모자라면 아무것도 차감하지 않는다.
            foreach (var input in recipe.Inputs)
                if (Warehouse.CountOf(save.Warehouse, input.ItemId) < input.Count) return null;
            foreach (var input in recipe.Inputs)
                Warehouse.TryRemove(save.Warehouse, input.ItemId, input.Count);

            var job = new CraftJob
            {
                RecipeId = recipeId,
                StartedAt = now,
                CompletesAt = now.AddSeconds(recipe.WorkSeconds * SpeedDivisor(save)),
                Seed = save.TakeSeed(),
                Collected = false,
            };
            save.Factory.Queue.Add(job);
            return job;
        }

        public static int QueueCapacity(GameSave save) => save.Factory.StationLevel;

        /// <summary>작업대 레벨이 오르면 제작이 빨라진다. 1.0 / 0.85 / 0.72 …</summary>
        private static double SpeedDivisor(GameSave save)
        {
            double d = 1.0;
            for (int i = 1; i < save.Factory.StationLevel; i++) d *= 0.85;
            return d;
        }

        public IEnumerable<TimedEvent> CollectEvents(ResolveWindow window, ResolveContext ctx)
        {
            var factory = ctx.Save.Factory;

            // 오프라인 상한: 너무 오래 비웠으면 그 시점부터만 친다.
            var effectiveFrom = window.From;
            if (window.To - effectiveFrom > OfflineProductionCap)
                effectiveFrom = window.To - OfflineProductionCap;

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

            int count = recipe.OutputCount;
            if (quality == CraftQuality.Failed) count = 0;
            else if (quality == CraftQuality.Excellent) count += 1;

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

        /// <summary>
        /// 직접 노동 1회. 품질에 따른 보수를 지급하고 지급액을 돌려준다.
        ///
        /// 제작 큐와 달리 시드가 없다. 품질은 플레이어 손이 정한 것이라 다시 굴릴 난수가 없다.
        /// 초반 수입원이다 — 20~30회면 첫 스캐브 고용비가 모인다 (DATA_SCHEMA §3-1).
        /// </summary>
        public long CompleteManualWork(GameSave save, IDataRegistry data, CraftQuality quality)
        {
            var pay = data.Balance.LaborPayByQuality;
            int i = (int)quality;
            long amount = i >= 0 && i < pay.Length ? pay[i] : 0;
            save.Player.Money += amount;
            return amount;
        }

        /// <summary>완료돼 수령된 작업을 큐에서 치운다. 정산 후 한 번 부른다.</summary>
        public static void PruneCollected(GameSave save)
        {
            save.Factory.Queue.RemoveAll(j => j.Collected);
        }
    }
}
