using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Factory
{
    /// <summary>미니게임 한 번의 결과.</summary>
    public struct WorkStepResult
    {
        /// <summary>이번 단계 점수 0~1.</summary>
        public double Score;

        /// <summary>이번 단계로 완성됐는가.</summary>
        public bool Completed;

        /// <summary>완성됐다면 만들어진 것. 아니면 <c>Count</c> 가 0 이다.</summary>
        public ItemStack Output;

        /// <summary>완성품의 품질.</summary>
        public CraftQuality Quality;

        /// <summary>창고가 꽉 차서 못 받은 개수.</summary>
        public int Overflow;

        public int StepsDone;
        public int StepsTotal;
    }

    /// <summary>
    /// 작업대 — <b>손으로 물건을 만든다</b> (GDD §6).
    ///
    /// <para><b>왜 돈이 아니라 물건인가:</b> 예전에는 미니게임 한 번이 곧바로 보수였다.
    /// 두드려도 아무것도 쌓이지 않으니 그건 놀이가 아니라 노동이고, 실제로 그렇게 느껴졌다.
    /// 이제 한 번의 성공이 공정을 한 단계 밀고, 단계가 차면 물건이 나온다. 돈은 그 물건을
    /// 팔아서 번다.</para>
    ///
    /// <para><b>품질은 산출 개수를 바꾼다.</b> 보수 배수로 두면 플레이어 눈에는 숫자 하나가
    /// 달라질 뿐이지만, 개수로 두면 "잘해서 하나 더 나왔다"가 보인다. 같은 정보를
    /// 볼 수 있는 형태로 주는 것이다.</para>
    ///
    /// <para>큐 제작(<see cref="FactorySystem.Enqueue"/>)과 같은 레시피를 쓴다. 기다리느냐
    /// 직접 하느냐의 차이일 뿐이고, 성장하면 직접 하던 것이 큐로 넘어간다 (GDD §2).</para>
    /// </summary>
    public static class Workbench
    {
        /// <summary>
        /// 작업을 시작한다. 재료를 즉시 차감한다.
        /// 이미 만드는 중이면 실패 — 먼저 <see cref="Cancel"/> 해야 한다.
        /// </summary>
        public static bool TryStart(GameSave save, IDataRegistry data, string recipeId)
        {
            if (StartBlockReason(save, data, recipeId) != null) return false;

            var recipe = data.GetRecipe(recipeId);
            foreach (var input in recipe.Inputs)
                Warehouse.TryRemove(save.Warehouse, input.ItemId, input.Count);

            var bench = save.Factory.Workbench;
            bench.Clear();
            bench.RecipeId = recipeId;
            return true;
        }

        /// <summary>시작할 수 없는 이유. 가능하면 null.</summary>
        public static string StartBlockReason(GameSave save, IDataRegistry data, string recipeId)
        {
            var recipe = data.GetRecipe(recipeId);
            if (recipe == null) return Loc.Text("제작법을 찾을 수 없습니다");
            if (recipe.ManualSteps <= 0) return Loc.Text("작업대에서 만들 수 없습니다");
            if (recipe.StationLevel > save.Factory.StationLevel)
                return Loc.Text("작업대 레벨 {0} 필요" , recipe.StationLevel);

            if (!save.Factory.Workbench.IsIdle) return Loc.Text("이미 작업 중입니다");

            foreach (var input in recipe.Inputs)
                if (Warehouse.CountOf(save.Warehouse, input.ItemId) < input.Count)
                    return Loc.Text("{0} 부족" , Loc.ItemName(input.ItemId));

            return null;
        }

        /// <summary>
        /// 작업을 접는다. <b>재료는 돌려준다.</b> 진행도만 사라진다 —
        /// 잘못 눌렀다고 재료를 태우면 고르는 것 자체가 무서워진다.
        /// </summary>
        public static bool Cancel(GameSave save, IDataRegistry data)
        {
            var bench = save.Factory.Workbench;
            if (bench.IsIdle) return false;

            var recipe = data.GetRecipe(bench.RecipeId);
            if (recipe != null)
                foreach (var input in recipe.Inputs)
                    Warehouse.TryAdd(save.Warehouse, data, input.ItemId, input.Count);

            bench.Clear();
            return true;
        }

        /// <summary>
        /// 미니게임 한 번의 결과를 반영한다. 마지막 단계였으면 물건이 나온다.
        /// </summary>
        public static void NormalizeDeliveryWork(GameSave save, IDataRegistry data)
        {
            var bench = save.Factory.Workbench;
            if (bench.IsIdle) return;
            var recipe = data.GetRecipe(bench.RecipeId);
            // Old saves may be halfway through the former three-step free salvage job.
            if (bench.RecipeId == "RCP_SALVAGE" && recipe != null && recipe.ManualSteps == 1 && bench.StepsDone > 0)
            { bench.StepsDone = 0; bench.Scores.Clear(); }
        }

        public static WorkStepResult Advance(GameSave save, IDataRegistry data, double score)
        {
            NormalizeDeliveryWork(save, data);
            var result = new WorkStepResult();
            var bench = save.Factory.Workbench;
            if (bench.IsIdle) return result;

            var recipe = data.GetRecipe(bench.RecipeId);
            if (recipe == null) { bench.Clear(); return result; }

            if (score < 0) score = 0;
            if (score > 1) score = 1;

            bench.StepsDone++;
            bench.Scores.Add(score);

            result.Score = score;
            result.StepsDone = bench.StepsDone;
            result.StepsTotal = recipe.ManualSteps;

            if (bench.StepsDone < recipe.ManualSteps) return result;

            // ── 완성 ──
            double average = 0;
            int qualitySteps = 0;
            int deliveryMultiplier = 1;
            for (int i = 0; i < bench.Scores.Count; i++)
            {
                if (Minigames.KindFor(recipe, i) == MinigameKind.Signal || Minigames.KindFor(recipe, i) == MinigameKind.Vault)
                    deliveryMultiplier = System.Math.Max(deliveryMultiplier, DeliverySignal.MultiplierFor(bench.Scores[i]));
                else { average += bench.Scores[i]; qualitySteps++; }
            }
            average = qualitySteps > 0 ? average / qualitySteps : .75;

            var quality = FactorySystem.GradeManualWork(data, average);
            int count = OutputCountFor(data, recipe, quality) * deliveryMultiplier;

            int overflow = Warehouse.TryAdd(save.Warehouse, data, recipe.OutputItemId, count);

            save.Player.Exp += Leveling.ExpFromLabor(quality, data.Balance);

            result.Completed = true;
            result.Quality = quality;
            result.Output = new ItemStack(recipe.OutputItemId, count);
            result.Overflow = overflow;

            bench.Clear();
            return result;
        }

        /// <summary>
        /// 품질이 정하는 산출 개수. <c>OutputCount</c> 가 양호 기준이다.
        ///
        /// <para>실패해도 0 개는 아니다 — 재료를 넣고 세 번 두드린 끝에 아무것도 없으면
        /// 그건 벌이 아니라 고장으로 읽힌다. 대신 아래로도 위로도 눈에 띄게 움직인다.</para>
        /// </summary>
        public static int OutputCountFor(IDataRegistry data, RecipeDef recipe, CraftQuality quality)
        {
            var table = data.Balance.ManualOutputByQuality;
            int i = (int)quality;

            double ratio = table != null && i >= 0 && i < table.Length ? table[i] : 1.0;
            int count = (int)System.Math.Round(recipe.OutputCount * ratio);
            return count < 1 ? 1 : count;
        }

        /// <summary>지금 단계의 이름. 화면이 "무엇을 하는 중인지" 말할 수 있게.</summary>
        public static string StepName(RecipeDef recipe, int stepIndex)
        {
            if (recipe != null && recipe.StepNames != null &&
                stepIndex >= 0 && stepIndex < recipe.StepNames.Length)
                return Loc.Text(recipe.StepNames[stepIndex]);

            return DefaultStepNames[stepIndex % DefaultStepNames.Length];
        }

        private static string[] DefaultStepNames => new string[] { Loc.Text("재료 배치"), Loc.Text("조립"), Loc.Text("검수") };

        /// <summary>작업대에서 만들 수 있는 레시피. 화면의 선택 목록이 쓴다.</summary>
        public static List<RecipeDef> AvailableRecipes(GameSave save, IDataRegistry data)
        {
            var list = new List<RecipeDef>();
            foreach (var recipe in data.AllRecipes)
            {
                if (recipe.ManualSteps <= 0) continue;
                if (recipe.StationLevel > save.Factory.StationLevel) continue;
                list.Add(recipe);
            }

            // 재료 없이 만들 수 있는 것을 먼저. 빈손인 플레이어가 첫 줄에서 막히면 안 된다.
            list.Sort((a, b) =>
            {
                int c = a.Inputs.Length.CompareTo(b.Inputs.Length);
                if (c != 0) return c;
                c = a.StationLevel.CompareTo(b.StationLevel);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
        }
    }
}
