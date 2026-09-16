using System;

namespace AfterSeoul.Core
{
    public enum LaunchPhase { Logo, Story, Title, Entered }

    /// <summary>Cold launch only. Tapping the film skips to the title, never through it.</summary>
    public sealed class LaunchFlow
    {
        public double Elapsed { get; private set; }
        public LaunchPhase Phase => _entered ? LaunchPhase.Entered : Elapsed < 1.6 ? LaunchPhase.Logo : Elapsed < 5.4 ? LaunchPhase.Story : LaunchPhase.Title;
        private bool _entered;
        public void Tick(double seconds)
        {
            if (_entered || seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            Elapsed += seconds;
        }
        public void Tap()
        {
            if (Phase == LaunchPhase.Title) _entered = true;
            else if (!_entered) Elapsed = 5.4;
        }
    }
}
