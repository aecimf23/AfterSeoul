using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 단계별 미니게임 배정.
    ///
    /// <para>여기서 보는 건 재미가 아니라 <b>단조로움이 데이터로 새어들어오지 못하게</b> 하는 것이다.
    /// 네 단계짜리 레시피가 같은 동작 네 번이면, 단계를 나눈 이유가 사라진다.</para>
    /// </summary>
    [TestFixture]
    public class MinigameTests
    {
        private static RecipeDef Recipe(int steps, params string[] games) => new RecipeDef
        {
            Id = "RCP_T", ManualSteps = steps, OutputItemId = "JUNK16", OutputCount = 1,
            StepGames = games,
        };

        // ── 배정 ─────────────────────────────────────────────────

        [Test]
        public void ExplicitGames_AreUsed()
        {
            var recipe = Recipe(3, "inspect", "hold", "timing");

            Assert.AreEqual(MinigameKind.Inspect, Minigames.KindFor(recipe, 0));
            Assert.AreEqual(MinigameKind.Hold, Minigames.KindFor(recipe, 1));
            Assert.AreEqual(MinigameKind.Timing, Minigames.KindFor(recipe, 2));
        }

        /// <summary>지정을 안 했어도 단조로우면 안 된다 — 기본값이 돌아가며 배정된다.</summary>
        [Test]
        public void WithoutGames_RotatesInstead_OfRepeating()
        {
            var recipe = Recipe(4);
            var seen = new HashSet<MinigameKind>();

            for (int i = 0; i < recipe.ManualSteps; i++) seen.Add(Minigames.KindFor(recipe, i));

            Assert.AreEqual(Minigames.All.Length, seen.Count, "네 단계면 종류를 다 거쳐야 한다");
            Assert.IsFalse(Minigames.HasAdjacentRepeat(recipe), "붙은 단계가 같으면 안 된다");
        }

        /// <summary>지정이 단계 수보다 짧으면 나머지는 기본 배정으로 이어간다 — 데이터가 모자라도 안 죽는다.</summary>
        [Test]
        public void ShortGameList_FallsBackForTheRest()
        {
            var recipe = Recipe(4, "hold");

            Assert.AreEqual(MinigameKind.Hold, Minigames.KindFor(recipe, 0));
            Assert.DoesNotThrow(() => Minigames.KindFor(recipe, 3));
        }

        [Test]
        public void UnknownGameId_FallsBack_RatherThanCrashing()
        {
            var recipe = Recipe(2, "타격", "hold");

            Assert.AreEqual(Minigames.All[0], Minigames.KindFor(recipe, 0));
            Assert.AreEqual(MinigameKind.Hold, Minigames.KindFor(recipe, 1));
        }

        [Test]
        public void NegativeAndNullAreSafe()
        {
            Assert.DoesNotThrow(() => Minigames.KindFor(null, 0));
            Assert.DoesNotThrow(() => Minigames.KindFor(Recipe(3), -1));
        }

        [Test]
        public void EveryKind_HasIdAndLabel()
        {
            foreach (var kind in Minigames.All)
            {
                Assert.IsNotEmpty(Minigames.IdOf(kind));
                Assert.IsNotEmpty(Minigames.LabelOf(kind));

                MinigameKind round;
                Assert.IsTrue(Minigames.TryParse(Minigames.IdOf(kind), out round), "id 는 되읽혀야 한다");
                Assert.AreEqual(kind, round);
            }
        }

        [Test]
        public void HasAdjacentRepeat_CatchesTheMonotonousCase()
        {
            Assert.IsTrue(Minigames.HasAdjacentRepeat(Recipe(3, "timing", "timing", "hold")));
            Assert.IsFalse(Minigames.HasAdjacentRepeat(Recipe(3, "timing", "hold", "timing")));
        }
    }

    /// <summary>
    /// 실제 <c>recipes.json</c> 검증.
    ///
    /// <para><see cref="MinigameTests"/> 가 규칙을 보는 곳이라면 여기는 <b>진짜 데이터</b>를 본다.
    /// 예전에 manualSteps 와 stepNames 가 JSON 에만 있고 파서에 연결되지 않아서, 4단계 레시피가
    /// 조용히 3단계로 돌고 단계 이름이 전부 기본값이었다 — 그 종류의 사고를 여기서 잡는다.</para>
    /// </summary>
    [TestFixture]
    public class RecipeDataTests
    {
        private JsonDataRegistry _data;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
        }

        [Test]
        public void ManualStepsAndNames_SurviveLoading()
        {
            foreach (var recipe in _data.Recipes)
            {
                if (recipe.ManualSteps <= 0) continue;

                Assert.AreEqual(recipe.ManualSteps, recipe.StepNames.Length,
                    $"{recipe.Id}: stepNames 가 manualSteps 와 개수가 다르다 " +
                    "(파서가 값을 흘리면 여기서 티가 난다)");

                foreach (var name in recipe.StepNames)
                    Assert.IsNotEmpty(name, $"{recipe.Id}: 빈 단계 이름");
            }
        }

        [Test]
        public void EveryStep_HasAGame()
        {
            foreach (var recipe in _data.Recipes)
            {
                if (recipe.ManualSteps <= 0) continue;

                Assert.AreEqual(recipe.ManualSteps, recipe.StepGames.Length,
                    $"{recipe.Id}: stepGames 가 manualSteps 와 개수가 다르다");

                foreach (var game in recipe.StepGames)
                {
                    MinigameKind kind;
                    Assert.IsTrue(Minigames.TryParse(game, out kind),
                        $"{recipe.Id}: 모르는 미니게임 '{game}'");
                }
            }
        }

        /// <summary>손으로 만드는 레시피에 같은 동작이 연달아 나오면 안 된다.</summary>
        [Test]
        public void NoRecipe_RepeatsTheSameGameBackToBack()
        {
            foreach (var recipe in _data.Recipes)
            {
                if (recipe.ManualSteps <= 0) continue;

                Assert.IsFalse(Minigames.HasAdjacentRepeat(recipe),
                    $"{recipe.Id}: 붙어 있는 두 단계가 같은 미니게임이다 — 단계를 나눈 의미가 없다");
            }
        }

        /// <summary>빈손으로 시작할 레시피가 하나는 있어야 한다 — 게임의 입구다.</summary>
        [Test]
        public void AtLeastOneRecipe_NeedsNoInputs()
        {
            foreach (var recipe in _data.Recipes)
                if (recipe.ManualSteps > 0 && recipe.Inputs.Length == 0) Assert.Pass();

            Assert.Fail("재료 없이 만들 수 있는 레시피가 없다 — 빈손 플레이어가 막힌다");
        }
    }
}
