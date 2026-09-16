using System;
using System.IO;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using Newtonsoft.Json;

class VaultVerification
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static void OpenSafe(VaultSearch board, int target)
    {
        if (board.Opened == 0) board.Open(0);
        for (int i = 0; i < 16 && board.Opened < target; i++)
            if (!board.IsHazard(i)) board.Open(i);
    }
    static int Main(string[] args)
    {
        try
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var b = new VaultSearch(seed); b.Open(seed % 16);
                Check(!b.Collapsed && b.Opened == 1, "First touch safe");
                int hazards = 0;
                for (int i = 0; i < 16; i++)
                {
                    if (b.IsHazard(i)) hazards++;
                    int expected = 0;
                    for (int j = 0; j < 16; j++)
                        if (i != j && Math.Abs(i/4-j/4) <= 1 && Math.Abs(i%4-j%4) <= 1 && b.IsHazard(j)) expected++;
                    Check(b.Nearby(i) == expected, "Clue must match adjacent hazards");
                }
                Check(hazards == 3, "Exactly three hazards");
                b.Scan(); b.Scan(); Check(!b.Collapsed && b.Scans == 0 && !b.Scan(), "Two safe scans only");
            }
            foreach (int count in new[] { 3, 6, 10 })
            {
                var b = new VaultSearch(12); OpenSafe(b, count); b.Bank();
                Check(b.Resolved && DeliverySignal.MultiplierFor(b.Score) == (count == 10 ? 4 : count == 6 ? 2 : 1), "Bank correct tier");
                double score = b.Score; Check(!b.Open(15) && !b.Scan() && score == b.Score, "No changes after settlement");
            }
            var failed = new VaultSearch(22); OpenSafe(failed, 6);
            for (int i = 0; i < 16; i++) if (failed.IsHazard(i)) { failed.Open(i); break; }
            Check(failed.Collapsed && failed.Score == 0, "Hazard loses bonus");
            var empty = new VaultSearch(4); empty.Bank(); Check(!empty.Resolved && !empty.Open(-1) && !empty.Open(16), "Invalid actions ignored");
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(args[0],n)));
            var recipe = data.GetRecipe("RCP_VAULT");
            Check(recipe != null && recipe.ManualSteps == 1 && Minigames.KindFor(recipe,0) == MinigameKind.Vault, "Live recipe integration");
            foreach (double score in new[] { 0, .4, .7, 1 })
            {
                var save = new GameSave(); save.Factory.StationLevel=1; save.Warehouse.Capacity=100;
                Check(Workbench.TryStart(save,data,recipe.Id), "Free entry");
                save=JsonConvert.DeserializeObject<GameSave>(JsonConvert.SerializeObject(save));
                var result=Workbench.Advance(save,data,score);
                int expected=Workbench.OutputCountFor(data,recipe,FactorySystem.GradeManualWork(data,.75))*DeliverySignal.MultiplierFor(score);
                Check(result.Completed && result.Output.Count==expected, "Actual warehouse reward");
                Check(!Workbench.Advance(save,data,score).Completed, "No double payout");
            }
            foreach(var r in data.Recipes) Check(!Minigames.HasAdjacentRepeat(r),"No adjacent repetitions");
            Console.WriteLine("PASS vault: "+checks+" checks"); return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
