using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
class ProgressResetVerification {
 sealed class Clock : IClock { public DateTimeOffset UtcNow {get;set;} = DateTimeOffset.Parse("2026-09-16T00:00:00Z"); }
 sealed class Files : IFileStore {
  public Dictionary<string,string> Data=new Dictionary<string,string>(); public string FailTarget;
  public bool Exists(string p)=>Data.ContainsKey(p);
  public string ReadAllText(string p)=>Data[p];
  public void WriteAllText(string p,string s)=>Data[p]=s;
  public void Replace(string s,string d){if(d==FailTarget)throw new IOException("Simulated disk failure");if(Exists(d) && d!="save.json.bak")Data[d+".bak"]=Data[d];Data[d]=Data[s];Data.Remove(s);}
  public bool TryMove(string s,string d){if(!Exists(s))return false;Replace(s,d);return true;}
 }
 static void Check(bool x,string m){if(!x)throw new Exception(m);}
 static int Main(string[] args){try{
  var data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(args[0],n)));var clock=new Clock();var files=new Files();var codec=new NewtonsoftJsonCodec();
  var session=new GameSession(new SaveService(files,codec,clock),data,clock);session.Boot();session.ChooseEmployer("HWANG");
  session.Save.Player.Money=999999;session.Save.WelcomePage=-1;session.Save.Starter.Closed=true;
  session.Save.Mail.AccountId="test-account";session.Save.Mail.DailyShipmentsUsed=2;
  session.Save.Mail.Outbox.Add(new MailShipment{TxId="keep-this-transaction",Uploaded=true,Claimed=true});
  session.Save.Support.ActiveUntil=clock.UtcNow.AddDays(20);var mail=session.Save.Mail;var support=session.Save.Support;
  session.StrikeProduction();session.Commit();clock.UtcNow=clock.UtcNow.AddDays(4);
  session.ResetProgress();
  Check(session.NeedsEmployerChoice && session.Save.WelcomePage==0,"First-run entry restored");
  Check(session.Save.Scavs.Count==0 && session.Save.Expeditions.Count==0 && session.Save.Warehouse.Stacks.Count==0,"Progress cleared");
  Check(session.Save.SavedAt==clock.UtcNow && session.Save.Player.CreatedAt==clock.UtcNow,"No stale offline rewards");
  Check(session.LastReport==null && session.PendingLevelUps==0 && session.Conveyor.Parts.Count==0 && session.Conveyor.CooldownRemaining==0,"Transient state cleared");
  Check(ReferenceEquals(mail,session.Save.Mail) && ReferenceEquals(support,session.Save.Support),"External paid state preserved");
  Check(files.Data["save.json"]==files.Data["save.json.bak"],"Backup cannot resurrect old progress");
  var loaded=new GameSession(new SaveService(files,codec,clock),data,clock);loaded.Boot();Check(loaded.NeedsEmployerChoice,"Restart persists");
  Check(loaded.ChooseEmployer("HWANG") && StarterSupport.Active(loaded.Save),"Starter support reopened");
  Check(loaded.StartWork("RCP_SALVAGE") && loaded.AdvanceWork(0).Completed && loaded.Sell("JUNK16",1),"Starter loop playable");
  long money=loaded.Save.Player.Money;Check(loaded.Hire(loaded.Save.Market.Offers.First(o=>o.Tier==1).OfferId)!=null,"First hire succeeds");
  Check(loaded.Save.Player.Money==money,"First hire remains supported");
    var before=loaded.Save;files.FailTarget="save.json";
  try{loaded.ResetProgress();throw new Exception("Expected primary failure");}catch(IOException){}
  Check(ReferenceEquals(before,loaded.Save),"Failed primary must preserve live progress");
  files.FailTarget="save.json.bak";
  try{loaded.ResetProgress();throw new Exception("Expected backup failure");}catch(IOException){}
  Check(!ReferenceEquals(before,loaded.Save) && loaded.NeedsEmployerChoice,"Backup error cannot resurrect previous live state");
  files.FailTarget=null;loaded.Commit();
  Check(codec.Deserialize<GameSave>(files.Data["save.json"]).Player.EmployerNpcId==null,"Following save must retain reset");
  string diskRoot=Path.Combine(Path.GetTempPath(),"AfterSeoulResetTest_"+Guid.NewGuid().ToString("N"));
  try {
   var diskSession=new GameSession(new SaveService(new FileStore(diskRoot),codec,clock),data,clock);
   diskSession.Boot();diskSession.ChooseEmployer("HWANG");diskSession.Save.Player.Money=987654;diskSession.Commit();diskSession.ResetProgress();
   Check(File.ReadAllText(Path.Combine(diskRoot,"save.json"))==File.ReadAllText(Path.Combine(diskRoot,"save.json.bak")),"Real atomic file backup is fresh");
   Check(!File.Exists(Path.Combine(diskRoot,"save.json.bak.bak")),"No old secondary backup");
  } finally { foreach(var file in Directory.GetFiles(diskRoot)) File.Delete(file); Directory.Delete(diskRoot); }
  Console.WriteLine("Progress reset verification passed (starter hire, receipts, failure recovery, real disk backup)");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

