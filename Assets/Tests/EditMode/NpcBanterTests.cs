using AfterSeoul.Unity.UI;
using NUnit.Framework;
namespace AfterSeoul.Tests
{
    public class NpcBanterTests
    {
        [Test] public void DialogueCyclesWithoutRepeating_AndEachEmployerHasItsOwnSequence()
        {
            var banter=new NpcBanter();
            foreach(var npc in new[]{"HWANG","DR_CHOI","YONGSAN_KIM"}) {
                var seen=new System.Collections.Generic.HashSet<string>();
                string first=null;
                for(int i=0;i<6;i++) { string line=banter.Next(npc); if(i==0)first=line; Assert.IsTrue(seen.Add(line)); }
                Assert.AreEqual(first,banter.Next(npc));
            }
            Assert.AreEqual("",banter.Next("unknown"));
        }
        [Test] public void IdleNeedsFortyFiveSecondsWithoutActivity_AndModalsResetTheTimer()
        {
            var banter=new NpcBanter();
            for(int i=0;i<44;i++) Assert.IsFalse(banter.Tick(1,true));
            banter.Activity();
            for(int i=0;i<44;i++) Assert.IsFalse(banter.Tick(1,true));
            Assert.IsTrue(banter.Tick(1,true)); Assert.IsFalse(banter.Tick(1,true));
            for(int i=0;i<100;i++)Assert.IsFalse(banter.Tick(1,false));
            for(int i=0;i<44;i++) Assert.IsFalse(banter.Tick(1,true));
            Assert.IsTrue(banter.Tick(1,true));
        }
    }
}
