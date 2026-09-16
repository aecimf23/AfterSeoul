using AfterSeoul.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class NpcSpeechTests
    {
        [Test] public void Speech_RevealsGradually_AndFinishDoesNotRequireMoreTicks()
        {
            var host = new GameObject("SpeechTest", typeof(RectTransform), typeof(Text));
            try {
                var text=host.GetComponent<Text>();
                var speech=new NpcSpeech(text,"HWANG","돌아왔군. 인원부터 확인해.");
                Assert.AreEqual("",text.text);
                speech.Tick(.1f);
                Assert.IsNotEmpty(text.text);
                Assert.IsFalse(speech.IsComplete);
                speech.Finish();
                Assert.AreEqual("돌아왔군. 인원부터 확인해.",text.text);
                speech.Tick(.2f);
                Assert.IsTrue(speech.IsComplete);
            } finally { Object.DestroyImmediate(host); }
        }

        [Test] public void EveryEmployerHasDistinctGreetings_AndUnknownEmployerStaysSilent()
        {
            string[] ids={"HWANG","DR_CHOI","YONGSAN_KIM"};
            var lines=new System.Collections.Generic.HashSet<string>();
            foreach(var id in ids) for(int i=0;i<2;i++) {
                Assert.IsNotEmpty(EmployerGreeting.Line(id,i));
                Assert.IsTrue(lines.Add(EmployerGreeting.Line(id,i)));
            }
            Assert.AreEqual("",EmployerGreeting.Line(""));
        }
    }
}
