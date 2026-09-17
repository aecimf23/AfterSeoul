using System;
using System.IO;
using AfterSeoul.Core;
using AfterSeoul.Factory;
class ConveyorVerification {
 sealed class Clock:IClock { public DateTimeOffset UtcNow {get;set;}=DateTimeOffset.Parse("2026-09-16T00:00:00Z"); }
 sealed class Files:IFileStore { public int Writes; public System.Collections.Generic.Dictionary<string,string> D=new System.Collections.Generic.Dictionary<string,string>(); public bool Exists(string p)=>D.ContainsKey(p); public string ReadAllText(string p)=>D[p]; public void WriteAllText(string p,string s){Writes++;D[p]=s;} public void Replace(string a,string b){D[b]=D[a];D.Remove(a);} public bool TryMove(string a,string b){if(!Exists(a))return false;Replace(a,b);return true;} }
 static int checks; static void Check(bool b,string m){if(!b)throw new Exception(m);checks++;}
 static GameSession New(IDataRegistry d,out Clock c,out Files f) {c=new Clock();f=new Files();var s=new GameSession(new SaveService(f,new NewtonsoftJsonCodec(),c),d,c);s.Boot();s.ChooseEmployer("HWANG");return s;}
 static void Advance(GameSession s,double seconds){for(int i=0;i<(int)Math.Round(seconds*120);i++)s.AdvanceProductionConveyor(1d/120);}
 static ProductionStrikeResult Hit(GameSession s){for(int i=0;i<2000;i++){s.AdvanceProductionConveyor(1d/120);foreach(var p in s.Conveyor.Parts)if(p.Position>=s.Conveyor.HitStart && p.Position<=s.Conveyor.HitEnd && s.Conveyor.CooldownRemaining<=1e-9)return s.StrikeProduction();}throw new Exception("No hit available");}
 static void Verify(IDataRegistry d){
 var s=New(d,out var c,out var f);
 s.Save.Scavs.Add(new ScavState{Uid="worker",Status=ScavStatus.Idle});s.AssignProductionScav("worker");int writes=f.Writes;
 for(int i=0;i<500;i++){c.UtcNow=c.UtcNow.AddMilliseconds(10);s.StrikeProduction();}
 Check(f.Writes==writes,"Miss and cooldown spam must not save or resolve automatic work");
 Check(s.Save.Factory.Production.WorkDone==0,"Runtime-only spam leaves timeline settlement for normal tick");
 s.Tick();Check(Math.Abs(s.Save.Factory.Production.WorkDone-.05)<1e-8,"Normal tick preserves pending automatic work after spam");
 s=New(d,out c,out f);s.Save.Player.Money=1000000;s.UnlockProductionGun();s.Save.Scavs.Add(new ScavState{Uid="last-sample-worker",Status=ScavStatus.Idle});s.AssignProductionScav("last-sample-worker");
 var trial=s.Save.Factory.Production.Contract;double remaining=ProductionWork.Current(s.Save,d).WorkRequired*trial.Required;c.UtcNow=c.UtcNow.AddSeconds(Math.Ceiling(remaining/ProductionWork.AutoWorkPerSecond(s.Save,d))+1);
 var settlementHit=Hit(s);Check(settlementHit.Kind==ProductionStrikeKind.Blocked && settlementHit.WorkAdded==0 && ProductionWork.TrialReady(s.Save),"Valid strike settles auto-completed trial before awarding manual work");
 s=New(d,out c,out f);s.AdvanceProductionConveyor(0);
 Check(s.Conveyor.Parts.Count==1 && s.Conveyor.Parts[0].Position==.1,"First part starts visible at .1");
 Check(s.StrikeProduction().Kind==ProductionStrikeKind.Miss && s.Conveyor.Parts.Count==0,"Early strike consumes future part");
 Check(s.StrikeProduction().Kind==ProductionStrikeKind.Cooldown && s.Save.Factory.Production.WorkDone==0,"Cooldown has no work");
 var hit=Hit(s);Check(hit.Kind==ProductionStrikeKind.Hit && hit.Tier==1 && hit.WorkAdded==1,"Timed basic hit grants one work");
 Check(s.StrikeProduction().Kind==ProductionStrikeKind.Cooldown && s.Save.Factory.Production.WorkDone==1,"Consumed part cannot score twice");
 for(int i=1;i<10;i++)hit=Hit(s);Check(hit.Production.CompletedCount==1 && s.Save.Player.Money==5000,"Exactly ten basic hits finish initial gun");
 for(int level=0;level<=20;level++) {
 s=New(d,out c,out f);s.Save.Factory.Production.SpeedLevel=level;
 for(int i=0;i<120*120;i++){s.AdvanceProductionConveyor(1d/120);s.TapProduction();}
 Check(s.Save.Factory.Production.TotalProduced==0 && s.Save.Factory.Production.WorkDone==0,"Continuous 120Hz input cannot score at supply level "+level);
 Check(Hit(s).Kind==ProductionStrikeKind.Hit,"Quiet recovery restores valid timing at supply level "+level);
 }
 s=New(d,out c,out f);for(int i=0;i<120*60;i++){s.AdvanceProductionConveyor(1d/120);s.TapProduction();}
 Check(s.Save.Factory.Production.TotalProduced==0 && s.Save.Factory.Production.WorkDone==0,"120Hz mash for one minute scores zero");
 s=New(d,out c,out f);for(int i=0;i<10;i++)Hit(s);Check(s.Save.Factory.Production.TotalProduced==1,"Proper input outperforms mash");
 s.AdvanceProductionConveyor(.1);double position=s.Conveyor.Parts.Count>0?s.Conveyor.Parts[0].Position:-1;double cooldown=s.Conveyor.CooldownRemaining;c.UtcNow=c.UtcNow.AddDays(1);s.Tick();
 Check(s.Save.Factory.Production.TotalProduced==1 && s.Conveyor.CooldownRemaining==cooldown,"Wall time grants no manual production or cooldown");
 if(position>=0)Check(s.Conveyor.Parts[0].Position==position,"Hidden screen does not move parts");
 s=New(d,out c,out f);s.AdvanceProductionConveyor(0);s.AdvanceProductionConveyor(3600);Check(Math.Abs(s.Conveyor.Parts[0].Position-(.1+.25/3))<1e-9,"Resume delta capped at quarter second");
 double before=s.Conveyor.Parts[0].Position;s.AdvanceProductionConveyor(double.NaN);s.AdvanceProductionConveyor(double.PositiveInfinity);s.AdvanceProductionConveyor(-1);Check(s.Conveyor.Parts[0].Position==before,"Invalid delta ignored");
 Check(!s.SelectProductionGun("WPN04") && s.Conveyor.Parts[0].Position==before,"Repeated selection cannot reset part");
 Check(!s.UpgradeProduction() && s.Conveyor.Parts[0].Position==before,"Failed upgrade preserves belt");
 s.Save.Player.Money=1000000000;s.StrikeProduction();cooldown=s.Conveyor.CooldownRemaining;Check(s.UpgradeProduction() && s.Conveyor.Parts.Count==0 && s.Conveyor.CooldownRemaining==cooldown,"Supply upgrade clears without free part or cooldown escape");
 double interval=ProductionWork.FeedInterval(s.Save,d);Check(interval<1.6 && ProductionWork.WorkPerClick(s.Save,d)==1,"Supply improves frequency not tap work");
 double priorInterval=1.6;for(int level=1;level<=20;level++){s.Save.Factory.Production.SpeedLevel=level;double nextInterval=ProductionWork.FeedInterval(s.Save,d);Check(nextInterval<priorInterval && nextInterval>=.55,"Every supply level improves frequency "+level);priorInterval=nextInterval;}
 double previous=0;for(int level=0;level<=5;level++){s.Save.Factory.Production.AssemblyJigLevel=level;double mean=0,sum=0;for(int tier=1;tier<=4;tier++){double chance=ProductionWork.MaterialChance(s.Save,d,tier);sum+=chance;mean+=chance*ProductionWork.MaterialWork(d,tier);}Check(Math.Abs(sum-1)<1e-9 && mean>previous,"Every quality upgrade improves expected work "+level);previous=mean;}
 Check(ProductionWork.MaterialTier(s.Save,d)==4 && ProductionWork.MaterialWork(d,4)==5,"Top material configurable work");
 s=New(d,out c,out f);s.Save.Player.Money=1000000000;s.AdvanceProductionConveyor(0);Check(s.UnlockProductionGun() && s.Conveyor.Parts.Count==0,"Commission clears old gun parts");
 var pstate=s.Save.Factory.Production;ProductionWork.AddWork(s.Save,d,ProductionWork.Current(s.Save,d).WorkRequired*pstate.Contract.Required);double work=pstate.WorkDone;s.AdvanceProductionConveyor(10);Check(s.StrikeProduction().Kind==ProductionStrikeKind.Blocked && pstate.WorkDone==work,"Ready trial blocks manual work");
 Check(s.SelectProductionGun("WPN04") && s.Conveyor.Parts.Count==0,"Selecting another gun clears belt");
 Hit(s);s.Commit();var saved=f.ReadAllText("save.json");Check(!saved.Contains("CooldownRemaining") && !saved.Contains("ConveyorPart"),"Belt never persisted");
 long total=pstate.TotalProduced;double done=pstate.WorkDone;c.UtcNow=c.UtcNow.AddDays(1);var reload=new GameSession(new SaveService(f,new NewtonsoftJsonCodec(),c),d,c);reload.Boot();Check(reload.Save.Factory.Production.TotalProduced==total && reload.Save.Factory.Production.WorkDone==done,"Reload awards no manual offline work");
 s=New(d,out c,out f);s.Save.Factory.Production.SpeedLevel=20;s.Save.Factory.Production.AssemblyJigLevel=5;double awarded=0;bool sawTop=false;for(int i=0;i<100;i++){hit=Hit(s);awarded+=hit.WorkAdded;sawTop|=hit.Tier==4;Check(hit.WorkAdded==ProductionWork.MaterialWork(d,hit.Tier),"Hit awards frozen part tier");}Check(sawTop && Math.Abs(awarded-390)<1e-8,"Deterministic quality distribution matches advertised odds");Advance(s,60);Check(s.Conveyor.Parts.Count<=8,"Unattended belt stays bounded");
 }
 static int Main(string[] args){try{var d=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(args[0],n)));var c=new Clock();var s=new GameSession(new SaveService(new Files(),new NewtonsoftJsonCodec(),c),d,c);s.Boot();s.ChooseEmployer("HWANG");for(int i=0;i<1000;i++)s.TapProduction();Check(s.Save.Factory.Production.TotalProduced==0 && s.Save.Factory.Production.WorkDone==0,"Untimed repeated taps must never award work");Verify(d);Console.WriteLine("PASS conveyor: "+checks+" checks");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}





