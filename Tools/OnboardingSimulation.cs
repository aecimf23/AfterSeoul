using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Scav;
using AfterSeoul.Quest;

public static class OnboardingSimulation
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
    static string Outcome(GameSession s)=>s.Save.Player.Money+"|"+s.Save.Player.Exp+"|"+
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
        string once=State(offline);offline.Tick();offline.Tick();
        Check(once==State(offline),"Duplicate return reward");
        branches++;
    }

    static void ProbeExtensions(IDataRegistry data,string directory)
    {
        var lines=new List<string>();
        foreach(var employer in data.AllEmployers)
        {
            var raw=File.ReadAllText(Path.Combine(directory,employer.NpcId+"-fast-save.json"));
            var saved=Codec.Deserialize<GameSave>(raw);
            var files=new MemoryFiles();files.WriteAllText(SaveService.FileName,raw);
            var clock=new TestClock(saved.SavedAt);var s=Load(files,data,clock);
            var scav=s.Save.Scavs.First();
            var offer=Shop.OffersFor(s.Save,data).Where(x=>!scav.Equipment.ContainsKey(data.GetItem(x.ItemId).EquipSlot))
                .OrderBy(x=>x.Price).First();
            long before=s.Save.Player.Money;
            Check(s.Buy(offer.ItemId),"Earned-money purchase failed");
            Check(s.Save.Player.Money==before-offer.Price,"Wrong purchase price");
            Check(s.Equip(scav.Uid,offer.ItemId),"Purchased equipment cannot equip");
            Check(scav.Equipment[data.GetItem(offer.ItemId).EquipSlot]==offer.ItemId,"Equipment not retained");
            lines.Add(employer.NpcId+": earned-money purchase/equip "+offer.ItemId+" price="+offer.Price);
            s.CancelWork();
            Check(s.StartWork("RCP_SALVAGE"),"Salvage probe");
            for(int i=0;i<3;i++){clock.Advance(TimeSpan.FromSeconds(15));s.AdvanceWork(.8);}
            Check(s.StartWork("RCP_BOLT"),"Material-consuming craft");
            clock.Advance(TimeSpan.FromSeconds(15));s.AdvanceWork(.8);
            var once=State(s);s=Load(files,data,clock);
            Check(once==State(s),"Mid-recipe reload loses input/step");
            for(int i=0;i<2;i++){clock.Advance(TimeSpan.FromSeconds(15));s.AdvanceWork(.8);}
            Check(Warehouse.CountOf(s.Save.Warehouse,"JUNK03")>0,"Bolt craft produced nothing");
            long money=s.Save.Player.Money;
            Check(!s.Deliver("NOT_AN_ACTIVE_QUEST"),"Invalid delivery accepted");
            Check(s.Save.Player.Money==money,"Invalid delivery grants money");
            lines.Add(employer.NpcId+": material craft + mid-step reload + invalid delivery rejection passed");
        }
        File.WriteAllLines(Path.Combine(directory,"extension-probes.txt"),lines);
    }
    public static void Main(string[] args)
    {
        var data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(args[0],n)));
        Directory.CreateDirectory(args[1]);
        var rows=new List<string>{"employer,profile,startVariant,stepSeconds,score,crafts,steps,firstSaleSec,hireSec,equipSec,departSec,returnSec,firstQuestSec,delivered,cash30,level30,injured,lost,firstReturnBeyond30"};
        var timelines=new List<string>();
        foreach(var employer in ((IDataRegistry)data).AllEmployers)
        for(int profile=0;profile<4;profile++)
        for(int sample=0;sample<12;sample++)
        {
            var name=new[]{"fast","steady","slow","queue_only"}[profile];
            int stepSeconds=new[]{15,30,60,0}[profile];
            double score=new[]{.85,.6,.3,0}[profile];
            // Start-time variation supplies genuine daily-market/quest randomness. No grants or overrides.
            var start=new DateTimeOffset(2026,9,15,1,0,0,TimeSpan.Zero).AddSeconds(sample*37);
            var clock=new TestClock(start);var files=new MemoryFiles();
            var s=Load(files,data,clock);
            Check(s.Save.Player.Money==0,"Unexpected starting grant");
            Check(s.ChooseEmployer(employer.NpcId),"Employer choice");
            Check(s.Save.Quests.Active.Count>0,"Empty first-day quests");
            int crafts=0,steps=0,firstSale=-1,hire=-1,equip=-1,depart=-1,returned=-1,firstQuest=-1,delivered=0;
            int nextStep=60+stepSeconds; bool returnSeen=false;
            Action<int,string> log=(t,message)=> { if(sample==0)timelines.Add(employer.NpcId+"/"+name+" "+t+"s "+message); };
            string scavId=null;
            var pool=data.GetQuestPool(Employers.QuestPoolId(data,employer.NpcId));
            for(int t=0;t<=1800;t+=5)
            {
                clock.UtcNow=start.AddSeconds(t);s.Tick();
                Check(s.Save.Player.Money>=0,"Negative cash");
                Check(s.Save.Warehouse.Stacks.All(x=>x.Count>0),"Invalid inventory count");
                if(t==900)
                {
                    s.Commit();var before=State(s);s=Load(files,data,clock);
                    Check(before==State(s),"Reload changed progress at 15min");
                    log(t,"reload preserved progress");
                }
                if(t<60)continue; // Reading/choosing/navigation budget.
                foreach(var q in s.Save.Quests.Active.ToArray())
                {
                    if(q.Delivered)continue;
                    var def=pool.FirstOrDefault(x=>x.Id==q.QuestId);
                    if(def!=null && DailyQuestSystem.MeetsRequirements(s.Save,data,def))
                    {
                        Check(s.Deliver(q.QuestId),"Deliver rejected");
                        delivered++;if(firstQuest<0)firstQuest=t;
                        var before=State(s);
                        Check(!s.Deliver(q.QuestId),"Quest delivered twice");
                        Check(before==State(s),"Repeated delivery mutated save");
                        log(t,"quest "+q.QuestId);
                    }
                }
                // Reserve only salvage requirements until that quest can be delivered.
                int reserve=0;
                var salvage=data.GetItem("JUNK16");
                foreach(var q in s.Save.Quests.Active.Where(x=>!x.Delivered))
                {
                    var def=pool.FirstOrDefault(x=>x.Id==q.QuestId);
                    if(def==null)continue;
                    foreach(var req in def.Requires)
                        if(req.ItemId=="JUNK16" || (!string.IsNullOrEmpty(req.Tag) && salvage.Tags.Contains(req.Tag)))
                            reserve+=req.Count;
                }
                int sale=Warehouse.CountOf(s.Save.Warehouse,"JUNK16")-reserve;
                if(sale>0)
                {
                    Check(s.Sell("JUNK16",sale),"Sale rejected");
                    if(firstSale<0)firstSale=t;
                    log(t,"sold "+sale+" salvage; cash="+s.Save.Player.Money);
                }
                if(FactorySystem.ActiveJobs(s.Save)==0) s.EnqueueCraft("RCP_SALVAGE");
                if(scavId==null)
                {
                    var offer=s.Save.Market.Offers.OrderBy(x=>x.HireCost).FirstOrDefault();
                    if(offer!=null && s.Save.Player.Money>=offer.HireCost)
                    {
                        var scav=s.Hire(offer.OfferId);Check(scav!=null,"Hire rejected");
                        scavId=scav.Uid;hire=t;log(t,"hired "+scav.Name+"; cash="+s.Save.Player.Money);
                        // Exercise warehouse -> equipment transfer with the included starter weapon.
                        var weapon=scav.Equipment[EquipSlot.Weapon];
                        Check(s.Unequip(scavId,EquipSlot.Weapon),"Unequip starter");
                        Check(s.Equip(scavId,weapon),"Re-equip starter");
                        equip=t;log(t,"starter equipment roundtrip "+weapon);
                    }
                }
                if(scavId!=null && depart<0)
                {
                    var exp=s.Depart("MYEONGDONG",new[]{scavId});
                    if(exp!=null)
                    {
                        depart=t;log(t,"depart; planned return="+(exp.ReturnsAt-start).TotalSeconds);
                        s.Commit();
                        VerifyReturn(files,data,clock.UtcNow,exp.ReturnsAt.AddSeconds(1));
                    }
                }
                if(!returnSeen && s.Save.Expeditions.Any(x=>x.Resolved))
                {
                    returnSeen=true;returned=t;log(t,"return; cash="+s.Save.Player.Money);
                }
                if(profile!=3)
                {
                    if(s.Save.Factory.Workbench.IsIdle) Check(s.StartWork("RCP_SALVAGE"),"Free work blocked");
                    if(t>=nextStep)
                    {
                        var result=s.AdvanceWork(score);steps++;
                        if(result.Completed)crafts++;
                        nextStep=t+stepSeconds;
                    }
                }
            }
            var expedition=s.Save.Expeditions.FirstOrDefault();
            rows.Add(string.Join(",",employer.NpcId,name,sample,stepSeconds,score,crafts,steps,firstSale,hire,equip,depart,returned,firstQuest,delivered,s.Save.Player.Money,s.Save.Player.Level,
                s.Save.Scavs.Count(x=>x.Status==ScavStatus.Injured),s.Save.Scavs.Count(x=>x.Status==ScavStatus.Missing||x.Status==ScavStatus.Dead),
                expedition==null ? -1 : (int)(expedition.ReturnsAt-start).TotalSeconds));
            if(sample==0)File.WriteAllText(Path.Combine(args[1],employer.NpcId+"-"+name+"-save.json"),State(s));
        }
        File.WriteAllLines(Path.Combine(args[1],"runs.csv"),rows);
        File.WriteAllLines(Path.Combine(args[1],"timelines.txt"),timelines);
        ProbeExtensions(data,args[1]);
        File.WriteAllText(Path.Combine(args[1],"verification.txt"),"Completed runs: "+(rows.Count-1)+"\nAssertions passed: "+assertions+"\nOnline/offline return comparisons: "+branches+"\nNo Unity editor launched; no UI inputs; real game C# sources; virtual time.\n");
        Console.WriteLine(File.ReadAllText(Path.Combine(args[1],"verification.txt")));
    }
}
