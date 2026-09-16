using System;
using System.Linq;
using System.Threading.Tasks;
using AfterSeoul.Core;
using AfterSeoul.Mail;
using SeoulLink;

namespace AfterSeoul.Unity.MobileLink
{
    public sealed class AccountMailLink : IAccountMailLink
    {
        private readonly GameSession session;
        private readonly IMailClient client;
        private bool busy;
        public AccountMailLink(GameSession session, IMailClient client) { this.session = session; this.client = client; }
        public bool Available => client.Available;
        public bool Connected => client.PlayerId != null && client.PlayerId == session.Save.Mail.AccountId;
        public string UnavailableReason => "본편 연동 서비스를 준비 중입니다. 연동 없이도 계속 플레이할 수 있습니다.";
        public async void Connect(string code, Action<bool, string> done)
        {
            if (busy) { done?.Invoke(false, "연동 요청을 처리 중입니다."); return; }
            busy = true;
            bool ok = false; string message;
            try
            {
                await client.LoginAsync();
                if (!string.IsNullOrEmpty(session.Save.Mail.AccountId) && session.Save.Mail.AccountId != client.PlayerId)
                    throw new InvalidOperationException("ACCOUNT_MISMATCH");
                await client.CallAsync(new MailRequest { action="status" });
                if (!session.BindMailAccount(client.PlayerId)) throw new InvalidOperationException("ACCOUNT_MISMATCH");
                ok=true; message=client.PlayerId;
            }
            catch (Exception e) { if(e.Message=="ACCOUNT_MISMATCH") client.Logout(); message=Message(e); }
            finally { busy=false; }
            done?.Invoke(ok,message);
        }
        public async void Sync(Action<bool, string> done)
        {
            if (busy) { done?.Invoke(false, "연동 요청을 처리 중입니다."); return; }
            busy=true; bool ok=false; string message;
            try { await SyncCore(); ok=true; message="배송함을 동기화했습니다."; }
            catch(Exception e) { message=Message(e); }
            finally { busy=false; }
            done?.Invoke(ok,message);
        }
        private async Task SyncCore()
        {
            if (!Connected || !session.Save.Mail.Linked) throw new InvalidOperationException("LOGIN_REQUIRED");
            string owner=client.PlayerId;
            await ReconcileReceipts(owner);
            // Only durably queued, explicitly account-bound parcels may ever leave this save.
            foreach(var parcel in session.Save.Mail.Outbox.ToArray())
            {
                if (parcel.Claimed || parcel.AccountId != owner) continue;
                RequireOwner(owner);
                if (!parcel.Uploaded)
                {
                    await client.CallAsync(new MailRequest { action="send",txId=parcel.TxId,
                        items=parcel.Items.Select(i=>new MailItem { itemId=i.ItemId,count=i.Count }).ToList() });
                    RequireOwner(owner);
                    parcel.Uploaded=true;
                    try { session.Commit(); } catch { parcel.Uploaded=false; throw; }
                }
            }
            await ReconcileReceipts(owner);
        }
        private async Task ReconcileReceipts(string owner)
        {
            var status=await client.CallAsync(new MailRequest { action="status" });
            RequireOwner(owner);
            foreach(var local in session.Save.Mail.Outbox)
            {
                var remote=status.shipments.FirstOrDefault(p=>p.txId==local.TxId);
                if(local.AccountId!=owner || local.Claimed || remote==null || remote.status!="claimed") continue;
                local.Claimed=true;
                try { session.Commit(); } catch { local.Claimed=false; throw; }
            }
        }
        private void RequireOwner(string owner)
        {
            if (!Connected || owner != client.PlayerId || !session.Save.Mail.Linked) throw new InvalidOperationException("ACCOUNT_MISMATCH");
        }
        public void Disconnect() { if(!busy) client.Logout(); }
        private static string Message(Exception e)
        {
            switch(e.Message)
            {
                case "ACCOUNT_MISMATCH": return "이 저장 데이터는 다른 계정에 연결되어 있습니다. 원래 계정으로 로그인하세요.";
                case "LOGIN_REQUIRED": return "본편과 같은 계정으로 다시 로그인하세요.";
                case "LOGIN_TIMEOUT": return "로그인 시간이 지났습니다. 다시 시도하세요.";
                case "NOT_CONFIGURED": return "연동 서비스를 준비 중입니다.";
                case "MAILBOX_NOT_PROVISIONED": return "계정 우편함이 아직 준비되지 않았습니다. 서비스 설정이 필요합니다.";
                case "DAILY_SHIPMENT_LIMIT":
                case "DAILY_VALUE_LIMIT":
                case "CATEGORY_DAILY_LIMIT": return "오늘 서버 전송 한도에 도달했습니다. 물건은 발송함에 보관되며 다음 날 다시 보낼 수 있습니다.";
                case "PENDING_CAPACITY": return "본편 우편함이 가득 찼습니다. 본편에서 먼저 수령하세요.";
                default: return "연동을 완료하지 못했습니다. 보급품은 발송함에 보관됩니다. 잠시 후 다시 시도하세요.";
            }
        }
    }
}
