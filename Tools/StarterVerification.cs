using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Scav;

class StarterVerification
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-16T00:00:00Z"); }
    sealed class Files : IFileStore {
        public Dictionary<string,string> Data = new Dictionary<string,string>();
        public bool Exists(string p) => Data.ContainsKey(p);
        public string ReadAllText(string p) => Data[p];
        public void WriteAllText(string p, string s) => Data[p] = s;
        public void Replace(string s, string d) { Data[d] = Data[s]; Data.Remove(s); }
        public bool TryMove(string s, string d) { if (!Exists(s)) return false; Replace(s,d); return true; }
    }
    static GameSession Load(Files files, IDataRegistry data, Clock clock) {
        var s = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), data, clock);
        s.Boot(); return s;
    }
    static int Main(string[] args) {
        try {
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(args[0],n)));
            foreach (var employer in ((IDataRegistry)data).AllEmployers)
            foreach (var recipe in new[] { "RCP_SALVAGE", "RCP_VAULT" })
            foreach (var score in new[] { 0.0, 1.0 }) {
                var files = new Files(); var clock = new Clock(); var s = Load(files,data,clock);
                Check(s.ChooseEmployer(employer.NpcId), "Choose employer");
                Check(s.StartWork(recipe), "Free practice starts");
                Check(s.AdvanceWork(score).Completed, "Even failed practice completes");
                Check(s.Sell("JUNK16", 1), "Sell practice output");
                s = Load(files,data,clock);
                Check(Tutorial.Current(s.Save,data) == TutorialStep.HireScav, "One practice and sale must unlock first hire guidance");
                long cash = s.Save.Player.Money;
                var offer = s.Save.Market.Offers.First(o => o.Tier == 1);
                Check(ScavMarket.HireBlockReason(s.Save,offer) == null, "Supported hire is enabled");
                var scav = s.Hire(offer.OfferId);
                Check(scav != null, "First recruit does not require grinding");
                Check(s.Save.Player.Money == cash, "Support must not consume or grant cash");
                Check(scav.Equipment.ContainsKey(EquipSlot.Weapon), "Recruit brings starter weapon");
                Check(s.Hire(offer.OfferId) == null, "Duplicate hire rejected");
                var exp = s.DepartOrientation(new[] { scav.Uid });
                Check(exp != null && exp.CostPaid == 0, "Free orientation remains available");
                clock.UtcNow = clock.UtcNow.AddMinutes(3); s = Load(files,data,clock);
                Check(s.DeliverOrientation(), "Orientation cargo delivered");
                Check(s.Save.Player.Money == cash + 50000, "Orientation reward once");
                Check(!s.DeliverOrientation(), "No duplicate reward");
                s.Save.Scavs.Clear(); s.Commit(); s = Load(files,data,clock);
                Check(s.Hire(s.Save.Market.Offers.First(o => !o.Hired).OfferId) == null, "Losing the team cannot regrant support");
            }
            MigrationAndAbuse(data);
            Console.WriteLine("PASS starter: " + checks + " checks"); return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static void MigrationAndAbuse(IDataRegistry data)
    {
        var files = new Files(); var clock = new Clock(); var s = Load(files,data,clock);
        s.ChooseEmployer("HWANG");
        var offer = s.Save.Market.Offers.First();
        Check(s.Hire(offer.OfferId) == null, "Cannot claim support before practice");
        Warehouse.TryAdd(s.Save.Warehouse,data,"JUNK16",1);
        Check(s.Sell("JUNK16",1), "Pre-practice sale succeeds normally");
        s.StartWork("RCP_SALVAGE"); s.AdvanceWork(0);
        Check(!StarterSupport.Ready(s.Save), "A sale before practice does not complete lesson");
        Check(!s.Sell("JUNK16",999), "Invalid sale rejected");
        Check(!StarterSupport.Ready(s.Save), "Invalid sale cannot unlock support");
        s.Commit(); s = Load(files,data,clock);
        Check(s.Save.Starter.PracticeCompleted, "Practice survives reload before sale");
        s.Sell("JUNK16",1);
        Check(s.Hire("missing") == null && StarterSupport.Ready(s.Save), "Invalid hire cannot consume support");
        var higher = new ScavOffer { OfferId="test-higher", Tier=2, HireCost=900000 };
        s.Save.Market.Offers.Add(higher);
        Check(ScavMarket.HireBlockReason(s.Save,higher) != null && s.Hire(higher.OfferId) == null, "Support never covers higher tier");
        Check(StarterSupport.Ready(s.Save), "Rejected expensive hire preserves support");
        var first = s.Hire(s.Save.Market.Offers.First(o => o.Tier==1).OfferId);
        s.Save.Player.Money = 300000;
        var second = s.Hire(s.Save.Market.Offers.First(o => o.Tier==1 && !o.Hired).OfferId);
        Check(second != null && s.Save.Player.Money==50000, "Second hire charges normal contract");
        s.Save.Scavs.Clear(); s.Save.Market.Offers.Clear(); s.Commit(); s=Load(files,data,clock);
        Check(!StarterSupport.Active(s.Save), "Support stays closed without roster or current offers");

        // Model old JSON by omitting the optional state; preserve all player progress.
        foreach (string history in new[] { "none", "roster", "hired-offer", "departure", "explored", "completed" }) {
            files = new Files(); clock = new Clock(); s = Load(files,data,clock); s.ChooseEmployer("HWANG");
            s.Save.Player.Money=12345;
            Warehouse.TryAdd(s.Save.Warehouse,data,"JUNK16",1);
            if(history=="roster") s.Save.Scavs.Add(new ScavState { Uid="old", Status=ScavStatus.Dead });
            if(history=="hired-offer") s.Save.Market.Offers[0].Hired=true;
            if(history=="departure") s.Save.Expeditions.Add(new ExpeditionState { Uid="old", Resolved=true, MapId="MYEONGDONG" });
            if(history=="explored") s.Save.ExploredMapIds.Add("MYEONGDONG");
            if(history=="completed") s.Save.Orientation.Stage=OrientationStage.Completed;
            s.Save.Starter=null; s.Commit(); s=Load(files,data,clock);
            Check(StarterSupport.Active(s.Save)==(history=="none"), "Legacy eligibility: "+history);
            Check(s.Save.Player.Money==12345 && Warehouse.CountOf(s.Save.Warehouse,"JUNK16")==1, "Migration preserves assets: "+history);
        }
        files = new Files(); clock = new Clock(); s=Load(files,data,clock); s.ChooseEmployer("HWANG");
        s.Save.Player.Money=300000;
        Check(s.Hire(s.Save.Market.Offers[0].OfferId)!=null, "Paid hire remains possible before lesson");
        Check(s.Save.Player.Money==50000 && s.Save.Starter.Closed, "Paid first hire closes support");
    }
}
