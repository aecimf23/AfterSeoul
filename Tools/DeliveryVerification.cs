using System;
using System.IO;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using Newtonsoft.Json;

class DeliveryVerification
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static void ReachChoice(DeliverySignal game)
    {
        for (int i = 0; i < 2500 && !game.Choosing && !game.Resolved; i++)
            game.Tick(.02, game.Cursor < game.Target);
        Check(game.Choosing || game.Resolved && game.Score == 1, "Tracking signal should acquire cargo");
    }
    static int Main(string[] args)
    {
        try
        {
            var game = new DeliverySignal();
            game.Bank(); Check(!game.Resolved, "Cannot bank before acquiring cargo");
            ReachChoice(game); game.Bank(); Check(game.Score == .4, "First cargo cashout");
            game.Continue(); game.Tick(1, false); Check(game.Score == .4, "Resolved result cannot change");
            game = new DeliverySignal(); ReachChoice(game); game.Continue(); ReachChoice(game); game.Bank();
            Check(DeliverySignal.MultiplierFor(game.Score) == 2, "Second cargo doubles yield");
            game = new DeliverySignal(); ReachChoice(game); game.Continue(); ReachChoice(game); game.Continue(); ReachChoice(game);
            Check(game.Resolved && DeliverySignal.MultiplierFor(game.Score) == 4, "Final cargo pays fourfold");
            game = new DeliverySignal(); ReachChoice(game); game.Continue();
            for (int i = 0; i < 1000 && !game.Resolved; i++) game.Tick(.02, true);
            Check(game.Resolved && game.Score == 0, "Losing pursuit loses bonus");
            game = new DeliverySignal(); ReachChoice(game);
            for (int i = 0; i < 500 && !game.Resolved; i++) game.Tick(.02, false);
            Check(game.Resolved && game.Score == .4, "Decision timeout banks safely");
            game = new DeliverySignal(); game.Tick(double.NaN, true); game.Tick(double.PositiveInfinity, false);
            Check(game.Cursor == .5, "Invalid time must not poison state");
            game.Tick(100, true); Check(!game.Resolved, "Resume spike cannot instantly lose cargo");

            var data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(args[0], name)));
            foreach (var recipe in data.Recipes)
            {
                Check(!Minigames.HasAdjacentRepeat(recipe), "Repeated game: " + recipe.Id);
                foreach (var id in recipe.StepGames) { MinigameKind parsed; Check(Minigames.TryParse(id, out parsed), "Unknown game " + id); }
            }
            var salvage = data.GetRecipe("RCP_SALVAGE");
            Check(salvage.ManualSteps == 1, "Starter work is one game, not three chores");
            Check(Minigames.KindFor(salvage, 0) == MinigameKind.Signal, "Starter recipe must expose new game");
            var legacy = new GameSave();
            legacy.Factory.Workbench.RecipeId = salvage.Id;
            legacy.Factory.Workbench.StepsDone = 2;
            legacy.Factory.Workbench.Scores.Add(1); legacy.Factory.Workbench.Scores.Add(1);
            Workbench.NormalizeDeliveryWork(legacy, data);
            Check(legacy.Factory.Workbench.StepsDone == 0 && legacy.Factory.Workbench.Scores.Count == 0,
                "Old three-step save must enter the replacement game cleanly");
            foreach (double score in new[] { 0.0, .4, .7, 1.0 })
            {
                var save = new GameSave(); save.Factory.StationLevel = 1; save.Warehouse.Capacity = 100;
                Check(Workbench.TryStart(save, data, salvage.Id), "Start starter work");

                save = JsonConvert.DeserializeObject<GameSave>(JsonConvert.SerializeObject(save));
                var result = Workbench.Advance(save, data, score);
                int expected = Workbench.OutputCountFor(data, salvage, FactorySystem.GradeManualWork(data, .75)) * DeliverySignal.MultiplierFor(score);
                Check(result.Completed && result.Output.Count == expected, "Saved score settles actual reward " + score);
                Check(!Workbench.Advance(save, data, 1).Completed, "Reward must not settle twice");
            }
            Console.WriteLine("PASS delivery minigame: " + checks + " checks"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
