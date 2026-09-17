using System;

namespace AfterSeoul.Core
{
    /// <summary>One-handed signal pursuit with an explicit bank-or-risk decision.</summary>
    public sealed class DeliverySignal
    {
        public double Cursor { get; private set; } = .5;
        public double Target => .5 + .26 * Math.Sin(_time * (.65 + Tier * .16));
        public double HalfWidth => .19 - Tier * .025;
        public double Progress { get; private set; }
        public double Trace { get; private set; }
        public int Tier { get; private set; }
        public bool Choosing { get; private set; }
        public bool Resolved { get; private set; }
        public double Score { get; private set; }
        public double ChoiceSecondsLeft => Math.Max(0, 8 - _choiceTime);
        private double _time, _elapsed, _choiceTime;

        public void Tick(double seconds, bool held)
        {
            if (Resolved || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return;
            // Limit accumulated input after a suspended application; one dropped frame is not a lost cargo.
            seconds = Math.Min(seconds, .25);
            while (seconds > .000001 && !Resolved)
            {
                double dt = Math.Min(seconds, .02); seconds -= dt;
                if (Choosing)
                {
                    _choiceTime += dt;
                    if (_choiceTime >= 8) Bank();
                    continue;
                }
                _elapsed += dt; _time += dt;
                Cursor = Math.Max(0, Math.Min(1, Cursor + (held ? 1 : -1) * .36 * dt));
                bool locked = Math.Abs(Cursor - Target) <= HalfWidth;
                if (locked) { Progress += dt / (3.2 + Tier * .8); Trace = Math.Max(0, Trace - dt * .07); }
                else Trace += dt * (.22 + Tier * .045);
                if (Trace >= 1 || _elapsed >= 35) { Score = 0; Resolved = true; }
                else if (Progress >= 1)
                {
                    Tier++; Progress = 1;
                    if (Tier == 3) Bank();
                    else { Choosing = true; _choiceTime = 0; }
                }
            }
        }

        public void Bank()
        {
            if (Resolved || Tier == 0) return;
            Score = Tier == 3 ? 1 : Tier == 2 ? .7 : .4;
            Choosing = false; Resolved = true;
        }

        public void Continue()
        {
            if (Resolved || !Choosing) return;
            Choosing = false; Progress = 0; Trace = 0; _choiceTime = 0;
        }

        // Saved in the ordinary stage score; settlement happens once in Workbench.Advance.
        public static int MultiplierFor(double score) => score >= .99 ? 4 : score >= .69 ? 2 : 1;
    }
}
