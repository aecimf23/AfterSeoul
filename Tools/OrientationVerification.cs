using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Scav;
using AfterSeoul.Quest;

public static class OrientationVerification
{
    sealed class MemoryFiles : IFileStore
    {
        public Dictionary<string,string> Files = new Dictionary<string,string>();
        public bool Exists(string p)=>Files.ContainsKey(p);
        public string ReadAllText(string p)=>Files[p];
        public void WriteAllText(string p,string s)=>Files[p]=s;
        public void Replace(string a,string b) { Files[b]=Files[a]; Files.Remove(a); }
        public bool TryMove(string a,string b) { if(!Exists(a))return false;Replace(a,b);return true; }
        public MemoryFiles Copy() { var x=new MemoryFiles(); foreach(var p in Files)x.Files[p.Key]=p.Value;return x; }
    }
    static readonly NewtonsoftJsonCodec Codec = new NewtonsoftJsonCodec();
    static int assertions, branches;
    static void Check(bool pass,string message) { assertions++; if(!pass) throw new Exception(message); }
    static string State(GameSession s)=>Codec.Serialize(s.Save);
    static string Outcome(GameSession s)=>Codec.Serialize(s.Save.Orientation)+"|"+s.Save.Player.Money+"|"+s.Save.Player.Exp+"|"+
        string.Join(";",s.Save.Warehouse.Stacks.OrderBy(x=>x.ItemId).Select(x=>x.ItemId+":"+x.Count))+"|"+
        string.Join(";",s.Save.Scavs.Select(x=>x.Uid+":"+x.Status+":"+string.Join("/",x.Equipment.OrderBy(y=>y.Key).Select(y=>y.Key+"="+y.Value))));
    static GameSession Load(MemoryFiles f,IDataRegistry d,TestClock c)
    {
        var s=new GameSession(new SaveService(f,Codec,c),d,c); s.Boot();return s;
    }
    static void VerifyReturn(MemoryFiles files,IDataRegistry data,DateTimeOffset start,DateTimeOffset until)
    {
        var f1=files.Copy();var f2=files.Copy();
        var c1=new TestClock(start);var c2=new TestClock(start);
        var active=Load(f1,data,c1);var offline=Load(f2,data,c2);
        offline.Suspend();
        while(c1.UtcNow<until) { c1.UtcNow = c1.UtcNow.AddSeconds(5)>until?until:c1.UtcNow.AddSeconds(5);active.Tick(); }
        c2.UtcNow=until;offline=Load(f2,data,c2);
        Check(Outcome(active)==Outcome(offline),"Online/offline return diverged");
        if(offline.Save.Expeditions.Any(e=>e.IsOrientation && e.Resolved) &&
           offline.Save.Orientation.Stage!=OrientationStage.Completed)
            Check(offline.Save.Orientation.Stage==OrientationStage.ReadyToDeliver &&
                offline.Save.Orientation.HasCargo,"Offline delivery entitlement");
        string once=State(offline);offline.Tick();offline.Tick();
        Check(once==State(offline),"Duplicate return reward");
        branches++;
    }

    public static void Main(string[] args)
    {
        var data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(args[0],n)));
        foreach(var employer in ((IDataRegistry)data).AllEmployers)
        {
            var files=new MemoryFiles();var clock=new TestClock(DateTimeOffset.Parse("2026-09-15T00:00:00Z"));
            var s=Load(files,data,clock);Check(s.ChooseEmployer(employer.NpcId),"employer choice");
            Check(s.Save.Orientation.Stage==OrientationStage.Pending,"new state");
            Check(s.DepartOrientation(null)==null,"null team");
            s.Save.Player.Money=1000000;
            var scav=s.Hire(s.Save.Market.Offers.OrderBy(x=>x.HireCost).First().OfferId);
            Check(scav!=null,"hire");
            Check(s.DepartOrientation(new[]{scav.Uid,scav.Uid})==null,"duplicate team");
            Check(s.DepartOrientation(new[]{"absent"})==null,"absent team");
            var weapon=scav.Equipment[EquipSlot.Weapon];
            s.Unequip(scav.Uid,EquipSlot.Weapon);
            Check(s.DepartOrientation(new[]{scav.Uid})==null,"unarmed");
            s.Equip(scav.Uid,weapon);
            var oldFiles=files.Copy();
            s.Save.Player.Money=0;
            s.Save.Warehouse.Capacity=0;
            var exp=s.DepartOrientation(new[]{scav.Uid});
            Check(exp!=null && exp.IsOrientation,"departure");
            Check(exp.ReturnsAt==clock.UtcNow.AddMinutes(3) && exp.CostPaid==0,"duration and cost");
            Check(s.DepartOrientation(new[]{scav.Uid})==null,"repeat departure");
            Check(!s.DeliverOrientation(),"premature delivery");
            VerifyReturn(files,data,clock.UtcNow,exp.ReturnsAt.AddSeconds(1));
            s=Load(files,data,clock);
            clock.Advance(TimeSpan.FromSeconds(179));s.Tick();
            Check(s.Save.Orientation.Stage==OrientationStage.Outbound,"early return");
            clock.Advance(TimeSpan.FromSeconds(1));var report=s.Tick();
            Check(s.Save.Orientation.Stage==OrientationStage.ReadyToDeliver,"exact return");
            Check(report.Expeditions.Count==1 && report.Expeditions[0].IsOrientation,"return report");
            Check(s.Save.Scavs[0].Status==ScavStatus.Idle && s.Save.Scavs[0].Equipment[EquipSlot.Weapon]==weapon,"safe personnel and gear");
            Check(s.Save.Orientation.HasCargo,"full warehouse cargo");
            Check(s.Save.Player.Money==0,"no automatic reward");
            VerifyReturn(files,data,clock.UtcNow,exp.ReturnsAt.AddSeconds(1));
            s=Load(files,data,clock);Check(s.DeliverOrientation(),"delivery after reload");
            Check(s.Save.Player.Money==50000 && !s.Save.Orientation.HasCargo,"reward and consumed cargo");
            Check(!s.DeliverOrientation(),"duplicate delivery");
            VerifyReturn(files,data,clock.UtcNow,exp.ReturnsAt.AddSeconds(1));
            s=Load(files,data,clock);Check(!s.DeliverOrientation(),"duplicate after reload");
            Check(s.Save.Player.Money==50000,"reward only once");
            // Advanced and historical saves never receive a new tutorial.
            foreach(int mode in new[]{0,1})
            {
                var past=files.Copy();var old=Load(past,data,clock);
                old.Save.Orientation=null;old.Save.Expeditions.Clear();
                old.Save.Player.Level=mode==0?3:1;
                old.Save.Scavs[0].ExpeditionCount=mode==0?0:1;
                old.Commit();old=Load(past,data,clock);
                Check(old.Save.Orientation.Stage==OrientationStage.Skipped,"advanced/history migration");
            }
            // Legacy early save and normal first departure.
            var legacy=Load(oldFiles,data,new TestClock(clock.UtcNow.AddMinutes(-3)));
            legacy.Save.Orientation=null;legacy.Commit();
            legacy=Load(oldFiles,data,new TestClock(legacy.Clock.UtcNow));
            Check(legacy.Save.Orientation.Stage==OrientationStage.Pending,"early migration");
            var normal=legacy.Depart("MYEONGDONG",new[]{scav.Uid});
            Check(normal!=null && !normal.IsOrientation,"normal departure");
            Check(legacy.Save.Orientation.Stage==OrientationStage.Skipped,"normal skips protection");
            var due=normal.ReturnsAt;
            legacy.Save.Orientation=null;legacy.Commit();
            legacy=Load(oldFiles,data,new TestClock(legacy.Clock.UtcNow));
            Check(legacy.Save.Orientation.Stage==OrientationStage.Skipped,"inflight migration");
            Check(legacy.Save.Expeditions[0].ReturnsAt==due && !legacy.Save.Expeditions[0].IsOrientation,"legacy trip unchanged");
            Check(legacy.DepartOrientation(new[]{scav.Uid})==null,"no late protection");
            VerifyReturn(oldFiles,data,legacy.Clock.UtcNow,due.AddSeconds(1));
        }
        Console.WriteLine("PASS: "+assertions+" checks; "+branches+" online/offline comparisons");
    }
}