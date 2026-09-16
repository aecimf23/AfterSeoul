using System;
using AfterSeoul.Core;

class LaunchVerification
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static int Main()
    {
        try
        {
            var flow = new LaunchFlow();
            Check(flow.Phase == LaunchPhase.Logo, "Cold boot starts at logo");
            flow.Tick(1.6); Check(flow.Phase == LaunchPhase.Story, "Logo flows into story");
            flow.Tick(3.9); Check(flow.Phase == LaunchPhase.Title, "Story flows into title");
            flow.Tick(10000); Check(flow.Phase == LaunchPhase.Title, "Title never auto-enters gameplay");
            flow.Tap(); Check(flow.Phase == LaunchPhase.Entered, "Title requires explicit tap");
            flow.Tick(999); flow.Tap(); Check(flow.Phase == LaunchPhase.Entered, "Entry is stable");
            foreach(double elapsed in new[]{0.0,1.0,1.7,4.0,5.3})
            {
                flow=new LaunchFlow(); flow.Tick(elapsed); flow.Tap();
                Check(flow.Phase == LaunchPhase.Title,"Skipping film cannot enter game");
                flow.Tap(); Check(flow.Phase == LaunchPhase.Entered,"Second tap enters after skipping");
            }
            flow=new LaunchFlow();flow.Tick(double.NaN);flow.Tick(double.PositiveInfinity);flow.Tick(-1);
            Check(flow.Phase==LaunchPhase.Logo,"Invalid time is ignored");
            Console.WriteLine("PASS launch flow: "+checks+" checks");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
