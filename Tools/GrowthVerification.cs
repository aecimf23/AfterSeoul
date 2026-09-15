using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Scav;
using AfterSeoul.Quest;

public static class GrowthVerification
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
        IDataRegistry data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(args[0],n)));
        foreach(var employer in ((IDataRegistry)data).AllEmployers)
        {
            var files=new MemoryFiles();var clock=new TestClock(DateTimeOffset.Parse("2026-09-15T00:00:00Z"));
            var s=Load(files,data,clock);s.ChooseEmployer(employer.NpcId);
            s.Save.Player.Money=1000000;
            var scav=s.Hire(s.Save.Market.Offers.OrderBy(x=>x.HireCost).First().OfferId);
            s.DepartOrientation(new[]{scav.Uid});clock.Advance(TimeSpan.FromMinutes(3));s.Tick();s.DeliverOrientation();
            var goal=GrowthGuide.Current(s.Save,data);
            Check(goal.Kind==GrowthGoalKind.Equip,"first equipment");
            Check(s.Buy(goal.ItemId) && s.Equip(scav.Uid,goal.ItemId),"suggestion purchasable/equippable");
            Check(GrowthGuide.Current(s.Save,data).Kind==GrowthGoalKind.Depart,"ready to depart");
            s.Save.Player.Money=0;
            Check(GrowthGuide.Current(s.Save,data).Kind==GrowthGoalKind.Earn,"departure funds");
            s.Save.Player.Money=1000000;s.Depart("MYEONGDONG",new[]{scav.Uid});
            Check(GrowthGuide.Current(s.Save,data).Kind==GrowthGoalKind.Wait,"wait on active trip");
            clock.Advance(TimeSpan.FromMinutes(20));s.Tick();
            // Isolate actionable delivery from randomized return risk.
            scav.Status=ScavStatus.Idle;
            var pool=data.GetQuestPool(Employers.QuestPoolId(data,employer.NpcId));
            var quest=pool.First(q=>q.Requires.Length>0);
            s.Save.Quests.Active.Clear();s.Save.Quests.Active.Add(new ActiveQuest{QuestId=quest.Id});
            s.Save.Warehouse.Stacks.Clear();
            Check(GrowthGuide.Current(s.Save,data).Kind==GrowthGoalKind.Collect,"missing supplies");
            foreach(var req in quest.Requires) {
                var id=!string.IsNullOrEmpty(req.ItemId)?req.ItemId:data.AllItems.First(i=>i.Tags.Contains(req.Tag)).Id;
                s.Save.Warehouse.Stacks.Add(new ItemStack{ItemId=id,Count=req.Count});
            }
            Check(GrowthGuide.Current(s.Save,data).Kind==GrowthGoalKind.Deliver,"deliver actionable");
            Check(s.Deliver(quest.Id),"quest delivery");
            var target=GrowthGuide.NextMap(s.Save,data);
            Check(target!=null,"next region");
            Check(target.Unlock.Type!="npcTrust" || target.Unlock.NpcId==employer.NpcId,"reachable employer target");
            var before=Codec.Serialize(s.Save);GrowthGuide.Current(s.Save,data);GrowthGuide.NextMap(s.Save,data);
            Check(before==Codec.Serialize(s.Save),"guide read only");
            s.Commit();s=Load(files,data,clock);
            Check(GrowthGuide.NextMap(s.Save,data).Id==target.Id,"reload target stable");
            s.Save.Scavs[0].Status=ScavStatus.Injured;
            Check(GrowthGuide.Current(s.Save,data).Kind==GrowthGoalKind.Treat,"recover instead of impossible trip");
        }
        Console.WriteLine("PASS growth: "+assertions+" checks");
    }
}