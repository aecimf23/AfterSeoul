using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SeoulLink
{
    public interface IMailClient
    {
        string PlayerId { get; }
        bool Available { get; }
        Task LoginAsync();
        Task<MailResponse> CallAsync(MailRequest request);
        void Logout();
    }
    [Serializable] public sealed class MailItem { public string itemId; public int count; }
    [Serializable] public sealed class Parcel
    {
        public string txId, createdAt, status, profileId;
        public List<MailItem> items = new List<MailItem>();
    }
    [Serializable] public sealed class MailRequest
    {
        public int schemaVersion = 1;
        public string action, profileId, txId;
        public List<MailItem> items;
    }
    [Serializable] public sealed class MailResponse
    {
        public int schemaVersion;
        public string error;
        public List<Parcel> shipments;
    }
}
