using System;
using System.Collections.Generic;
using System.IO;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Mail;
using Newtonsoft.Json;
using SeoulLink;
using System.Threading.Tasks;
using AfterSeoul.Unity.MobileLink;

class MailVerification
{
    sealed class Server : IMailClient
    {
        public string PlayerId { get; set; } = "account-a";
        public bool Available => true;
        public bool LoseReply;
        public bool RejectSend;
        public Dictionary<string,Parcel> Parcels = new Dictionary<string,Parcel>();
        public Task LoginAsync() => Task.CompletedTask;
        public void Logout() { PlayerId=null; }
        public Task<MailResponse> CallAsync(MailRequest request)
        {
            if(request.action=="send")
            {
                if(RejectSend) throw new InvalidOperationException("DAILY_SHIPMENT_LIMIT");
                if(!Parcels.ContainsKey(request.txId)) Parcels.Add(request.txId,new Parcel {txId=request.txId,items=request.items,status="pending"});
                if(LoseReply) { LoseReply=false; throw new IOException("response lost after server commit"); }
            }
            return Task.FromResult(new MailResponse {schemaVersion=1,shipments=new List<Parcel>(Parcels.Values)});
        }
    }
    sealed class Clock : IClock { public DateTimeOffset UtcNow => new DateTimeOffset(2026,9,16,12,0,0,TimeSpan.Zero); }
    sealed class Codec : IJsonCodec
    {
        public string Serialize<T>(T value) => JsonConvert.SerializeObject(value);
        public T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json);
    }
    sealed class Files : IFileStore
    {
        public bool Fail;
        public Dictionary<string,string> Data = new Dictionary<string,string>();
        public bool Exists(string path) => Data.ContainsKey(path);
        public string ReadAllText(string path) => Data[path];
        public void WriteAllText(string path,string data) { if(Fail) throw new IOException("disk full"); Data[path]=data; }
        public void Replace(string a,string b) { Data[b]=Data[a]; Data.Remove(a); }
        public bool TryMove(string a,string b) => false;
    }
    static void Check(bool yes,string why) { if(!yes) throw new Exception(why); }
    static int Main(string[] args)
    {
        try
        {
            var data=JsonDataRegistry.Load(name=>File.ReadAllText(Path.Combine(args[0],name)));
            var files=new Files(); var clock=new Clock();
            var session=new GameSession(new SaveService(files,new Codec(),clock),data,clock);
            session.Boot(); session.Save.Mail.Linked=true;
            Warehouse.TryAdd(session.Save.Warehouse,data,"MED16",2); session.Commit(); session.Tick();
            files.Fail=true;
            try { session.QueueShipment(new[]{new ItemStack("MED16",2)}); } catch(IOException) { }
            Check(Warehouse.CountOf(session.Save.Warehouse,"MED16")==2,"Failed save destroyed warehouse goods");
            Check(session.Save.Mail.Outbox.Count==0,"Failed save left a sendable parcel");
            Check(session.Save.Mail.DailyShipmentsUsed==0,"Failed save charged quota");
            files.Fail=false;
            var parcel=session.QueueShipment(new[]{new ItemStack("MED16",2)});
            Check(parcel!=null && Warehouse.CountOf(session.Save.Warehouse,"MED16")==0,"Successful queue did not deduct goods");
            Check(session.Save.Mail.Outbox.Count==1,"Successful queue missing parcel");
            var restored=new GameSession(new SaveService(files,new Codec(),clock),data,clock);restored.Boot();
            Check(restored.Save.Mail.Outbox.Count==1 && Warehouse.CountOf(restored.Save.Warehouse,"MED16")==0,"Restart duplicated or lost queue");
            Check(System.Text.RegularExpressions.Regex.IsMatch(parcel.TxId,"^m2p_[a-f0-9]{32}$"),"Not a protocol GUID transaction");
            Check(session.BindMailAccount("account-a"),"First account binding failed");
            Check(!session.BindMailAccount("account-b"),"Save rebound to another account");
            parcel.AccountId="account-a";session.Commit();
            var server=new Server();var link=new AccountMailLink(session,server);session.MailLink=link;
            var completion=new TaskCompletionSource<bool>();
            server.LoseReply=true;link.Sync((ok,message)=>completion.SetResult(ok));
            Check(!completion.Task.Result && !parcel.Uploaded,"Lost reply falsely marked uploaded");
            Check(server.Parcels.Count==1 && session.Save.Mail.Outbox.Count==1,"Lost reply discarded pending parcel");
            completion=new TaskCompletionSource<bool>();link.Sync((ok,message)=>completion.SetResult(ok));
            Check(completion.Task.Result && parcel.Uploaded && server.Parcels.Count==1,"Retry duplicated parcel or failed to recover");
            server.Parcels[parcel.TxId].status="claimed";
            completion=new TaskCompletionSource<bool>();link.Sync((ok,message)=>completion.SetResult(ok));
            Check(completion.Task.Result && parcel.Claimed,"Receipt did not reach sender");
            parcel.Claimed=false;
            session.Save.Mail.Outbox.Add(new MailShipment { TxId="m2p_ffffffffffffffffffffffffffffffff",AccountId="account-a",
                Items=new List<ItemStack>{new ItemStack("MED16",1)} });
            server.RejectSend=true;completion=new TaskCompletionSource<bool>();link.Sync((ok,message)=>completion.SetResult(ok));
            Check(!completion.Task.Result && parcel.Claimed,"Rejected upload blocked earlier delivery receipt");
            server.PlayerId="account-b";completion=new TaskCompletionSource<bool>();link.Sync((ok,message)=>completion.SetResult(ok));
            Check(!completion.Task.Result,"Wrong-account sync allowed");
            Check(Warehouse.CountOf(session.Save.Warehouse,"MED16")==0,"Network retries refunded sent inventory");
            Console.WriteLine("PASS mobile: save-failure rollback, quota rollback, durable queue/restart, GUID protocol.");return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e);return 1; }
    }
}
