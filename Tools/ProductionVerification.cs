using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Factory;

class ProductionVerification
{
    static int checks;
    static double SecondsForWork(IDataRegistry data,double work) => work/data.Balance.Production.AutoWorkPerScav;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-16T00:00:00Z"); }
    sealed class Files : IFileStore {
        public Dictionary<string,string> Data = new Dictionary<string,string>();
        public bool Exists(string p) => Data.ContainsKey(p);
        public string ReadAllText(string p) => Data[p];
        public void WriteAllText(string p,string s) => Data[p]=s;
        public void Replace(string a,string b) { Data[b]=Data[a]; Data.Remove(a); }
        public bool TryMove(string a,string b) { if (!Exists(a)) return false; Replace(a,b); return true; }
    }
    static GameSession Load(IDataRegistry data, Clock clock, Files files) {
        var s=new GameSession(new SaveService(files,new NewtonsoftJsonCodec(),clock),data,clock); s.Boot(); return s;
    }
    static int Main(string[] args) {
        try {
            // Reflection makes the pre-implementation failure an assertion, not a compiler error.
            Check(typeof(GameSession).GetMethod("TapProduction") != null, "Production click API must exist");
            var data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(args[0],n)));
            Verify(data);
            Console.WriteLine("PASS production: "+checks+" checks"); return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Verify(IDataRegistry data) {
        Check(data.Balance.Production.AutoWorkPerScav==.01 && new ProductionTuning().AutoWorkPerScav==.01,"Initial automatic rate preserves expedition economy");
        var guns=ProductionWork.Guns(data);
        Check(guns.Count==25 && guns[0].WeaponId=="WPN04", "25 contracts start with P17");
        Check(guns.Select(g=>g.WeaponId).Distinct().Count()==25, "No duplicate guns");
        foreach(var gun in guns) Check(data.GetItem(gun.WeaponId)!=null && gun.WorkRequired>0 && gun.Wage>0,"Valid gun "+gun.WeaponId);
        foreach(var employer in data.AllEmployers) {
            var clock=new Clock(); var files=new Files(); var s=Load(data,clock,files); s.ChooseEmployer(employer.NpcId);
            var before=new NewtonsoftJsonCodec().Serialize(s.Save.Warehouse);
            for(int i=0;i<9;i++) Check(EarnOneTap(s).CompletedCount==0,"Incomplete clicks");
            var result=EarnOneTap(s);
            Check(result.CompletedCount==1 && result.Wages==5000 && s.Save.Player.Money==5000,"First contract pays wages");
            Check(new NewtonsoftJsonCodec().Serialize(s.Save.Warehouse)==before && s.Save.Mail.Outbox.Count==0,"No items or shipments");
            Check(StarterSupport.Ready(s.Save),"Contract opens first hire support");
            s=Load(data,clock,files); Check(s.Save.Factory.Production.TotalProduced==1,"Production reload");
            Check(!s.UnlockProductionGun(),"Insufficient unlock money");
            Check(s.UpgradeProduction() && s.Save.Player.Money==0 && ProductionWork.FeedInterval(s.Save,data)<1.6,"Independent speed upgrade");
            Check(!s.SelectProductionGun(guns[1].WeaponId),"Locked selection blocked");
            s.Save.Player.Money=1000000000000;
            s.TapProduction(); long acceptanceCash=s.Save.Player.Money; Check(s.UnlockProductionGun(),"Accept next commission");
            Check(s.Save.Factory.Production.UnlockedCount==1 && s.Save.Player.Money==acceptanceCash,"Commission acceptance neither unlocks nor charges money");
            Check(s.Save.Factory.Production.SelectedWeaponId==guns[1].WeaponId && s.Save.Factory.Production.WorkDone==0,"Acceptance selects trial and resets old work");
            Check(!s.DeliverProductionCommission() && !s.UnlockProductionGun(),"Incomplete trial cannot deliver or accept twice");
            Check(!s.SelectProductionGun(guns[2].WeaponId),"Trial cannot bypass next gun");
            CompleteTrial(s);
            Check(s.Save.Player.Money==acceptanceCash && s.Save.Factory.Production.UnlockedCount==1,"Samples held unpaid until delivery");
            Check(s.DeliverProductionCommission() && s.Save.Player.Money==acceptanceCash+3*guns[1].Wage,"Delivery pays samples and approves gun");
            Check(!s.DeliverProductionCommission(),"Delivery cannot repeat");
            for(int i=2;i<guns.Count;i++) { Check(s.UnlockProductionGun(),"Sequential commission "+i); CompleteTrial(s); Check(s.DeliverProductionCommission(),"Sequential delivery "+i); }
            Check(!s.UnlockProductionGun() && ProductionWork.NextGun(s.Save,data)==null,"Final unlock capped");
            while(s.UpgradeProduction()) {}
            Check(s.Save.Factory.Production.SpeedLevel==20 && ProductionWork.SpeedUpgradeCost(s.Save,data)==0,"Speed capped");
        }
        var c=new Clock(); var f=new Files(); var session=Load(data,c,f); session.ChooseEmployer("HWANG");
        foreach(ScavStatus status in Enum.GetValues(typeof(ScavStatus))) {
            session.Save.Scavs.Add(new ScavState { Uid=status.ToString(),Status=status });
            Check(ProductionWork.CanAssign(session.Save,status.ToString())==(status==ScavStatus.Idle),"Eligibility "+status);
        }
        Check(!session.AssignProductionScav("missing"),"Missing scav rejected");
        Check(session.AssignProductionScav("Idle") && !session.AssignProductionScav("Idle"),"Assignment unique");
        Check(session.DepartOrientation(new[]{"Idle"})==null,"Working cannot depart");
        c.UtcNow=c.UtcNow.AddSeconds(SecondsForWork(data,5)); session.Tick();
        Check(Math.Abs(session.Save.Factory.Production.WorkDone-5)<1e-8,"Partial automatic work");
        session=Load(data,c,f);
        Check(Math.Abs(session.Save.Factory.Production.WorkDone-5)<1e-8,"Partial tick survives reload");
        c.UtcNow=c.UtcNow.AddSeconds(SecondsForWork(data,5)); var report=session.Tick();
        Check(report.ProductionWages==5000 && !report.IsEmpty,"Automatic wages report");
        Check(session.Tick().ProductionWages==0,"Duplicate resolve harmless");
        long paid=session.Save.Factory.Production.TotalWages;
        c.UtcNow=c.UtcNow.AddSeconds(-5); session.Tick();
        Check(session.Save.Factory.Production.TotalWages==paid,"Backwards time no wages");
        c.UtcNow=c.UtcNow.AddSeconds(5);
        Check(session.UnassignProductionScav("Idle") && session.Save.Scavs.First(x=>x.Uid=="Idle").Status==ScavStatus.Idle,"Unassign restores idle");
        c.UtcNow=c.UtcNow.AddHours(1); session.Tick(); Check(session.Save.Factory.Production.TotalWages==paid,"Unassigned earns nothing");
        Check(session.AssignProductionScav("Idle"),"Reassign");
        session.Save.Scavs.RemoveAll(x=>x.Uid=="Idle"); c.UtcNow=c.UtcNow.AddSeconds(SecondsForWork(data,10)); session.Tick();
        Check(session.Save.Factory.Production.TotalWages==paid && session.Save.Factory.Production.AssignedScavUids.Count==0,"Roster loss stops work");
        CompareOffline(data);
        MutationBoundaries(data);
        LargeClockRollback(data);
        ThreeWorkerCap(data);
        CommissionsAndEquipment(data);
    }
    static void CompareOffline(IDataRegistry data) {
        var ca=new Clock(); var cb=new Clock(); var fa=new Files(); var fb=new Files();
        var a=Load(data,ca,fa); var b=Load(data,cb,fb); a.ChooseEmployer("HWANG"); b.ChooseEmployer("HWANG");
        foreach(var s in new[]{a,b}) { s.Save.Scavs.Add(new ScavState { Uid="worker", Status=ScavStatus.Idle }); s.AssignProductionScav("worker"); }
        for(int i=0;i<137;i++) { ca.UtcNow=ca.UtcNow.AddSeconds(SecondsForWork(data,.25)); a.Tick(); }
        cb.UtcNow=cb.UtcNow.AddSeconds(SecondsForWork(data,34.25)); b.Tick();
        Check(a.Save.Player.Money==b.Save.Player.Money && Math.Abs(a.Save.Factory.Production.WorkDone-b.Save.Factory.Production.WorkDone)<1e-8,"Online offline equivalent");
        b=Load(data,cb,fb); long wages=b.Save.Factory.Production.TotalWages; double work=b.Save.Factory.Production.WorkDone;
        cb.UtcNow=cb.UtcNow.AddDays(30); var cap=Support.OfflineCap(b.Save,data,cb.UtcNow);
        b.Tick(); long expected=(long)Math.Floor((work+cap.TotalSeconds*data.Balance.Production.AutoWorkPerScav)/10)*5000;
        Check(b.Save.Factory.Production.TotalWages-wages==expected,"Long offline capped");
        // No assigned workers in a fresh game, even when legacy assistants exist.
        var c=new Clock(); var fresh=Load(data,c,new Files()); fresh.ChooseEmployer("HWANG"); fresh.Save.Factory.AutoLevel=5;
        c.UtcNow=c.UtcNow.AddDays(1); fresh.Tick(); Check(fresh.Save.Factory.Production.TotalProduced==0,"Legacy assistants not counted");
    }
    static void MutationBoundaries(IDataRegistry data) {
        var c=new Clock(); var f=new Files(); var s=Load(data,c,f); s.ChooseEmployer("HWANG");
        s.Save.Scavs.Add(new ScavState { Uid="one", Status=ScavStatus.Idle });
        c.UtcNow=c.UtcNow.AddHours(1); Check(s.AssignProductionScav("one"),"Assignment after idle");
        Check(s.Save.Factory.Production.TotalProduced==0 && s.Save.Factory.Production.WorkDone==0,"Assignment not retroactive");
        s.Save.Player.Money=100000; c.UtcNow=c.UtcNow.AddSeconds(SecondsForWork(data,5)); s.UpgradeProduction();
        Check(Math.Abs(s.Save.Factory.Production.WorkDone-5)<1e-8,"Upgrade settles old speed first");
        c.UtcNow=c.UtcNow.AddSeconds(SecondsForWork(data,2.5)); s.UnassignProductionScav("one");
        Check(Math.Abs(s.Save.Factory.Production.WorkDone-8)<1e-8,"Unassign settles new-speed interval");
        s.AssignProductionScav("one"); c.UtcNow=c.UtcNow.AddSeconds(SecondsForWork(data,2.5));
        s.UnlockProductionGun();
        Check(s.Save.Factory.Production.TotalProduced==1 && s.Save.Factory.Production.WorkDone==0,"Selection pays old gun before resetting remainder");
        s.Save.Factory.Production=null; s.Save.Factory.StationLevel=3; s.Save.Factory.AutoLevel=2; s.Commit();
        s=Load(data,c,f);
        Check(s.Save.Factory.Production!=null && s.Save.Factory.Production.UnlockedCount==1,"Explicit null production migration");
        Check(s.Save.Factory.StationLevel==3 && s.Save.Factory.AutoLevel==2,"Legacy factory state preserved");
        Check((int)ScavStatus.Dead==5 && (int)ScavStatus.Working==6,"Existing enum numbers unchanged");
        // A returned scav must not become a retroactive factory worker during event application.
        s.Save.Scavs.Clear(); s.Save.Scavs.Add(new ScavState { Uid="returning", Status=ScavStatus.OnExpedition });
        s.Save.Factory.Production.AssignedScavUids.Add("returning");
        var report=new ResolveReport(); var ctx=new ResolveContext(s.Save,data,report);
        var end=c.UtcNow.AddSeconds(SecondsForWork(data,10)); var events=new ProductionSystem().CollectEvents(new ResolveWindow(c.UtcNow,end),ctx).ToList();
        s.Save.Scavs[0].Status=ScavStatus.Working;
        foreach(var e in events) e.Apply(ctx);
        Check(s.Save.Factory.Production.TotalProduced==0,"Roster snapshots prevent retroactive return work");
        s.Save.Factory.Production.AssignedScavUids.Add("returning");
        Check(Math.Abs(ProductionWork.AutoWorkPerSecond(s.Save,data)-data.Balance.Production.AutoWorkPerScav)<1e-8,"Duplicate saved assignment counts once");
        var next=end.AddSeconds(SecondsForWork(data,10)); events=new ProductionSystem().CollectEvents(new ResolveWindow(end,next),ctx).ToList();
        foreach(var e in events) e.Apply(ctx);
        foreach(var e in events) e.Apply(ctx);
        Check(s.Save.Factory.Production.TotalProduced==1,"Duplicate collected event applies once");
    }
    static void LargeClockRollback(IDataRegistry data) {
        var c=new Clock(); var f=new Files(); var s=Load(data,c,f); s.ChooseEmployer("HWANG");
        s.Save.Scavs.Add(new ScavState { Uid="clock-worker", Status=ScavStatus.Idle });
        s.AssignProductionScav("clock-worker");
        c.UtcNow=c.UtcNow.AddSeconds(SecondsForWork(data,5)); s.Tick();
        Check(s.Save.Factory.Production.WorkDone==5,"Rollback fixture has partial work");
        c.UtcNow=c.UtcNow.AddYears(-1); var report=s.Tick();
        var recovery=c.UtcNow.AddHours(36);
        Check(report.ClockWentBackwards && report.ProductionWages==0,"Large rollback grants no work");
        Check(s.Save.SavedAt==recovery && s.Save.Factory.Production.LastWorkedAt==recovery,"Production shares bounded future-clock recovery");
        s=Load(data,c,f);
        Check(s.Save.Factory.Production.WorkDone==5 && s.Save.Factory.Production.LastWorkedAt==recovery,"Recovery watermark and partial work survive reload");
        c.UtcNow=recovery; s.Tick();
        Check(s.Save.Factory.Production.TotalProduced==0,"Recovery boundary itself grants no work");
        c.UtcNow=c.UtcNow.AddSeconds(SecondsForWork(data,5)); report=s.Tick();
        Check(report.ProductionWages==5000 && s.Save.Factory.Production.TotalProduced==1,"Production resumes after bounded clock recovery");
        Check(s.Tick().ProductionWages==0,"Recovered wages remain idempotent");
    }
    static ProductionResult EarnOneTap(GameSession s) {
        for(int i=0;i<1000;i++) {
            s.AdvanceProductionConveyor(.1);
            if(s.Conveyor.CooldownRemaining<=1e-9 && s.Conveyor.Parts.Any(p=>p.Position>=s.Conveyor.HitStart && p.Position<=s.Conveyor.HitEnd))return s.TapProduction();
        }
        throw new Exception("No hittable part arrived");
    }
    static void CompleteTrial(GameSession s) {
        int limit=200000;
        bool unpaid=true;
        while(!ProductionWork.TrialReady(s.Save) && limit-->0) {
            var result=EarnOneTap(s);
            unpaid &= result.Wages==0;
        }
        Check(unpaid,"Trial clicks defer all wages");
        Check(ProductionWork.TrialReady(s.Save),"Trial reaches required samples");
    }
    static void ThreeWorkerCap(IDataRegistry data) {
        var clock=new Clock();var files=new Files();var s=Load(data,clock,files);s.ChooseEmployer("HWANG");s.Save.Player.Money=1000000000;
        for(int i=0;i<4;i++)s.Save.Scavs.Add(new ScavState{Uid="cap"+i,Status=ScavStatus.Idle});
        Check(s.AssignProductionScav("cap0") && !s.AssignProductionScav("cap1"),"Only first production slot starts unlocked");
        Check(s.UpgradeProductionEquipment(ProductionEquipment.ExtraBench) && ProductionWork.CrewCapacity(s.Save)==2 && s.AssignProductionScav("cap1"),"First bench unlocks second slot");
        Check(s.UpgradeProductionEquipment(ProductionEquipment.ExtraBench) && ProductionWork.CrewCapacity(s.Save)==3 && s.AssignProductionScav("cap2"),"Second bench unlocks third slot");
        Check(!s.UpgradeProductionEquipment(ProductionEquipment.ExtraBench) && ProductionWork.EquipmentCost(s.Save,data,ProductionEquipment.ExtraBench)==0,"No fourth bench can be purchased");
        Check(!s.AssignProductionScav("cap3") && s.Save.Scavs.Find(x=>x.Uid=="cap3").Status==ScavStatus.Idle,"Fourth worker rejected safely");
        Check(s.DepartOrientation(new[]{"cap0"})==null,"Production worker cannot simultaneously depart");
        var p=s.Save.Factory.Production;p.ExtraBenches=8;p.AssemblyJigLevel=4;p.PowerToolsLevel=3;
        s.Save.Scavs.Find(x=>x.Uid=="cap3").Status=ScavStatus.Working;p.AssignedScavUids.Add("cap3");
        s.Save.Scavs.Add(new ScavState{Uid="fifth",Status=ScavStatus.Working});p.AssignedScavUids.Add("fifth");
        s.Save.Scavs.Add(new ScavState{Uid="away",Status=ScavStatus.OnExpedition});p.AssignedScavUids.Insert(0,"away");p.AssignedScavUids.Insert(0,"missing");p.AssignedScavUids.Insert(3,"cap0");
        s.Commit();s=Load(data,clock,files);p=s.Save.Factory.Production;
        Check(p.ExtraBenches==2 && ProductionWork.CrewCapacity(s.Save)==3,"Legacy bench level migrates to three slots");
        Check(p.AssignedScavUids.SequenceEqual(new[]{"cap0","cap1","cap2"}),"Migration retains first three unique valid working scavs");
        Check(s.Save.Scavs.Find(x=>x.Uid=="cap3").Status==ScavStatus.Idle && s.Save.Scavs.Find(x=>x.Uid=="fifth").Status==ScavStatus.Idle && s.Save.Scavs.Count==6,"Overflow workers safely released without loss");
        Check(s.Save.Scavs.Find(x=>x.Uid=="away").Status==ScavStatus.OnExpedition && !s.AssignProductionScav("away"),"Migration preserves expedition status and blocks double assignment");
        Check(p.AssemblyJigLevel==4 && p.PowerToolsLevel==3,"Migration preserves other equipment purchases");
        Check(!s.AssignProductionScav("cap3") && s.DepartOrientation(new[]{"cap1"})==null,"Migrated cap and expedition exclusion enforced");
        s.Commit();s=Load(data,clock,files);Check(s.Save.Factory.Production.AssignedScavUids.SequenceEqual(new[]{"cap0","cap1","cap2"}) && s.Save.Scavs.Find(x=>x.Uid=="cap3").Status==ScavStatus.Idle,"Worker migration is idempotent after reload");
    }
    static void CommissionsAndEquipment(IDataRegistry data) {
        var c=new Clock(); var f=new Files(); var s=Load(data,c,f); s.ChooseEmployer("HWANG");
        var p=s.Save.Factory.Production;
        Check(ProductionWork.CrewCapacity(s.Save)==1,"First bench free");
        Check(!s.UpgradeProductionEquipment(ProductionEquipment.AssemblyJig),"Equipment needs cash");
        s.Save.Player.Money=1000000000;
        Check(s.UpgradeProductionEquipment(ProductionEquipment.AssemblyJig) && s.Save.Player.Money==999992500,"Jig costs 7500");
        Check(ProductionWork.MaterialTier(s.Save,data)==2,"Jig unlocks improved material");
        s.Save.Scavs.Add(new ScavState{Uid="a",Status=ScavStatus.Idle}); s.Save.Scavs.Add(new ScavState{Uid="b",Status=ScavStatus.Idle});
        Check(s.AssignProductionScav("a") && !s.AssignProductionScav("b"),"Free bench enforces capacity");
        Check(s.UpgradeProductionEquipment(ProductionEquipment.PowerTools),"Craft power tools");
        Check(Math.Abs(ProductionWork.AutoWorkPerSecond(s.Save,data)-data.Balance.Production.AutoWorkPerScav*1.5)<1e-8,"Tools improve auto rate");
        Check(s.UpgradeProductionEquipment(ProductionEquipment.ExtraBench) && ProductionWork.CrewCapacity(s.Save)==2 && s.AssignProductionScav("b"),"Additional bench enables second worker");
        foreach(ProductionEquipment kind in Enum.GetValues(typeof(ProductionEquipment))) {
            while(s.UpgradeProductionEquipment(kind)) {}
            Check(ProductionWork.EquipmentCost(s.Save,data,kind)==0,"Equipment cap "+kind);
            Check(ProductionWork.EquipmentLevel(s.Save,kind)==(kind==ProductionEquipment.ExtraBench ? 2 : 5),"Equipment level cap "+kind);
        }
        Check(!s.UpgradeProductionEquipment((ProductionEquipment)99),"Invalid equipment rejected");
        s.UnlockProductionGun(); var gun=ProductionWork.Current(s.Save,data); long cash=s.Save.Player.Money;
        string inventory=new NewtonsoftJsonCodec().Serialize(s.Save.Warehouse);
        ProductionWork.AddWork(s.Save,data,gun.WorkRequired*1.5); s.Commit(); s=Load(data,c,f); p=s.Save.Factory.Production;
        Check(p.Contract.Crafted==1 && Math.Abs(p.WorkDone-gun.WorkRequired*.5)<1e-8,"Partial trial and work survive reload");
        Check(s.SelectProductionGun("WPN04") && s.SelectProductionGun(gun.WeaponId) && p.Contract.Crafted==1 && p.WorkDone==0,"Trial switching preserves samples but resets work");
        c.UtcNow=c.UtcNow.AddDays(30); var sampleReport=s.Tick();
        Check(sampleReport.ProductionCompleted==0 && !sampleReport.IsEmpty,"Prototype progress reports independently of delivered goods");
        Check(sampleReport.ProductionSamples==2 && sampleReport.ProductionCommissionReady && sampleReport.ProductionWages==0,"Offline report distinguishes samples and ready transition");
        Check(ProductionWork.TrialReady(s.Save) && p.Contract.Crafted==p.Contract.Required && p.WorkDone==0,"Offline trial stops exactly at requirement");
        long total=p.TotalProduced; s.TapProduction(); c.UtcNow=c.UtcNow.AddHours(2); var readyReport=s.Tick();
        Check(p.TotalProduced==total && s.Save.Player.Money==cash && p.WorkDone==0,"Ready trial blocks excess work and cash");
        Check(!readyReport.ProductionProgressed,"Ready trial does not persist nonexistent work each tick");
        Check(readyReport.ProductionSamples==0 && !readyReport.ProductionCommissionReady,"Ready notification cannot repeat");
        s=Load(data,c,f); p=s.Save.Factory.Production;
        s.SelectProductionGun("WPN04");
        c.UtcNow=c.UtcNow.AddSeconds(21/ProductionWork.AutoWorkPerSecond(s.Save,data));
        Check(s.DeliverProductionCommission(out long commissionPaid) && p.UnlockedCount==2 && p.Contract==null,"Ready trial delivers after reload");
        Check(commissionPaid==3*gun.Wage,"Delivery output excludes pending regular auto wages");
        Check(s.Save.Player.Money==cash+10000+3*gun.Wage,"Settled regular wages and exact commissioned quantity both paid");
        Check(new NewtonsoftJsonCodec().Serialize(s.Save.Warehouse)==inventory && s.Save.Mail.Outbox.Count==0,"Samples never enter inventory or outbox");
        Check(!s.DeliverProductionCommission(out commissionPaid) && commissionPaid==0,"Reloaded delivery cannot repeat or report payment");
        p.UnlockedCount=15; p.SelectedWeaponId=ProductionWork.Guns(data)[14].WeaponId;
        p.ExtraBenches=0; s.Commit(); s=Load(data,c,f); p=s.Save.Factory.Production;
        Check(p.UnlockedCount==15 && p.SelectedWeaponId==ProductionWork.Guns(data)[14].WeaponId,"Existing instant unlocks migrate unchanged");
        Check(p.AssignedScavUids.Count==2 && ProductionWork.CrewCapacity(s.Save)==2,"Existing overcapacity workers preserved");
        s.Save.Scavs.Add(new ScavState{Uid="c",Status=ScavStatus.Idle});
        Check(!s.AssignProductionScav("c"),"Migration does not permit extra worker");
        s.UnassignProductionScav("b"); Check(ProductionWork.CrewCapacity(s.Save)==1 && !s.AssignProductionScav("b"),"Released legacy excess slot requires bench");
    }
}


