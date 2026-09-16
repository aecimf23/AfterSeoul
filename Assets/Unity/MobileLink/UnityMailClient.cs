using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;
using UnityEngine.Networking;

namespace SeoulLink
{
    [Serializable] public sealed class MailConfiguration
    {
        public bool enabled;
        public string projectId;
        public string environment = "production";
    }

    // Copied to the mobile project by Tools/Sync-MobileMail.ps1; keep this source authoritative.
    public sealed class UnityMailClient : IMailClient
    {
        public static UnityMailClient Instance { get; } = new UnityMailClient();
        private IUnityServices services;
        private IAuthenticationService auth;
        private IPlayerAccountService accounts;
        private Task login;
        private TaskCompletionSource<bool> loginCancelled;
        private MailConfiguration configuration;
        public string PlayerId => auth != null && auth.IsSignedIn ? auth.PlayerId : null;
        public bool Available
        {
            get
            {
                if (configuration == null)
                {
                    var asset = Resources.Load<TextAsset>("MobileMailConfig");
                    if (asset != null) configuration = JsonConvert.DeserializeObject<MailConfiguration>(asset.text);
                }
                return configuration != null && configuration.enabled &&
                    !string.IsNullOrEmpty(configuration.projectId) && configuration.projectId == Application.cloudProjectId;
            }
        }
        public Task LoginAsync()
        {
            if (login != null && !login.IsCompleted) return login;
            loginCancelled = new TaskCompletionSource<bool>();
            return login = LoginCore();
        }
        public void CancelLogin() { loginCancelled?.TrySetResult(true); }
        private async Task LoginCore()
        {
            if (!Available) throw new InvalidOperationException("NOT_CONFIGURED");
            if (services == null || auth == null || accounts == null)
            {
                services = UnityServices.CreateServices();
                await services.InitializeAsync(new InitializationOptions().SetProfile("seoul-mail").SetEnvironmentName(configuration.environment));
                auth = services.GetAuthenticationService();
                accounts = services.GetPlayerAccountService();
            }
            if (auth == null || accounts == null) { services = null; throw new InvalidOperationException("LOGIN_FAILED"); }
            if (auth.IsSignedIn && auth.IsAuthorized) return;
            if (!accounts.IsSignedIn)
            {
                var currentAccounts = accounts;
                var completion = new TaskCompletionSource<bool>();
                Action signedIn = () => completion.TrySetResult(true);
                Action<RequestFailedException> failed = _ => completion.TrySetException(new InvalidOperationException("LOGIN_FAILED"));
                currentAccounts.SignedIn += signedIn;
                currentAccounts.SignInFailed += failed;
                try
                {
                    var deadline = Task.WhenAny(Task.Delay(TimeSpan.FromMinutes(3)), loginCancelled.Task);
                    await MailLoginWait.UntilSignedIn(currentAccounts.StartSignInAsync(), completion.Task, deadline);
                }
                catch
                {
                    // Late callbacks belong to the abandoned instance, never a new login attempt.
                    currentAccounts.SignOut();
                    auth?.SignOut(true);
                    services = null; auth = null; accounts = null;
                    throw;
                }
                finally { currentAccounts.SignedIn -= signedIn; currentAccounts.SignInFailed -= failed; }
            }
            if (auth.IsSignedIn) auth.SignOut();
            await auth.SignInWithUnityAsync(accounts.AccessToken);
        }
        public void Logout()
        {
            if (login != null && !login.IsCompleted) return;
            auth?.SignOut(true);
            accounts?.SignOut();
        }
        public async Task<MailResponse> CallAsync(MailRequest request)
        {
            if (!Available) throw new InvalidOperationException("NOT_CONFIGURED");
            if (auth == null || !auth.IsSignedIn || !auth.IsAuthorized) throw new InvalidOperationException("LOGIN_REQUIRED");
            string playerId = auth.PlayerId;
            var body = new JObject { ["params"] = new JObject { ["request"] = JObject.FromObject(request) } };
            using (var web = new UnityWebRequest("https://cloud-code.services.api.unity.com/v1/projects/" +
                configuration.projectId + "/scripts/SeoulMailbox", "POST"))
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Content-Type", "application/json");
                web.SetRequestHeader("Authorization", "Bearer " + auth.AccessToken);
                web.timeout = 25;
                var operation = web.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                if (PlayerId != playerId) throw new InvalidOperationException("ACCOUNT_MISMATCH");
                if (web.result != UnityWebRequest.Result.Success) throw new InvalidOperationException("NETWORK_ERROR");
                var response = JObject.Parse(web.downloadHandler.text)["output"]?.ToObject<MailResponse>();
                if (response == null || response.schemaVersion != 1 || response.shipments == null)
                    throw new InvalidOperationException("SCHEMA_MISMATCH");
                if (!string.IsNullOrEmpty(response.error)) throw new InvalidOperationException(response.error);
                return response;
            }
        }
    }
}
