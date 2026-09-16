using System;
using System.Threading.Tasks;

namespace SeoulLink
{
    public static class MailLoginWait
    {
        // The SDK browser launch itself may wait indefinitely for a callback.
        public static async Task UntilSignedIn(Task launch, Task signedIn, Task deadline)
        {
            var first = await Task.WhenAny(launch, signedIn, deadline);
            if (first == deadline) { Observe(launch); throw new InvalidOperationException("LOGIN_TIMEOUT"); }
            if (first == launch) await launch;
            else Observe(launch);
            if (await Task.WhenAny(signedIn, deadline) == deadline) throw new InvalidOperationException("LOGIN_TIMEOUT");
            await signedIn;
        }
        private static async void Observe(Task task)
        {
            try { await task; } catch { /* A late SDK callback cannot revive an abandoned login. */ }
        }
    }
}
