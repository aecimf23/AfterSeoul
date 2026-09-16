using System;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class ProgressResetTests
    {
        private GameSession session;
        private MemoryFileStore files;
        private TestClock clock;
        [SetUp] public void Setup()
        {
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            clock = new TestClock(new DateTimeOffset(2026,9,16,0,0,0,TimeSpan.Zero));
            files = new MemoryFileStore();
            session = new GameSession(new SaveService(files,new NewtonsoftJsonCodec(),clock),data,clock);
            session.Boot(); session.ChooseEmployer("HWANG");
        }
        [Test] public void ResetPersistsFreshProgressAndAllowsSupportedFirstHire()
        {
            session.Save.Player.Money = 999999;
            session.Save.Starter.Closed = true;
            session.Save.WelcomePage = -1;
            session.Save.Mail.Outbox.Add(new MailShipment { TxId="preserved", Uploaded=true, Claimed=true });
            session.Save.Mail.DailyShipmentsUsed = 2;
            var mail = session.Save.Mail;
            session.ResetProgress();
            Assert.IsTrue(session.NeedsEmployerChoice);
            Assert.AreEqual(0,session.Save.WelcomePage);
            Assert.IsNull(session.LastReport);
            Assert.AreEqual(0,session.PendingLevelUps);
            Assert.AreSame(mail,session.Save.Mail);
            Assert.AreEqual(2,session.Save.Mail.DailyShipmentsUsed);
            Assert.AreEqual(files.ReadAllText("save.json"),files.ReadAllText("save.json.bak"));
            session.Boot();
            Assert.IsTrue(session.NeedsEmployerChoice);
            Assert.IsTrue(session.ChooseEmployer("HWANG"));
            Assert.IsTrue(StarterSupport.Active(session.Save));
            Assert.IsTrue(session.StartWork("RCP_SALVAGE"));
            Assert.IsTrue(session.AdvanceWork(0).Completed);
            Assert.IsTrue(session.Sell("JUNK16",1));
            var money = session.Save.Player.Money;
            Assert.IsNotNull(session.Hire(session.Save.Market.Offers.First(o=>o.Tier==1).OfferId));
            Assert.AreEqual(money,session.Save.Player.Money);
        }
        [Test] public void ConfirmationIsRequiredAndDoubleTapOnlyResetsOnce()
        {
            var root = new GameObject("ResetTest",typeof(RectTransform));
            try {
                int restarted=0;
                var original=session.Save;
                ProgressResetDialog.Show(null,session,root.transform,()=>restarted++);
                Assert.AreSame(original,session.Save);
                var buttons=root.GetComponentsInChildren<Button>(true);
                var confirm=buttons.First(b=>b.name=="ConfirmProgressReset");
                confirm.onClick.Invoke(); confirm.onClick.Invoke();
                Assert.AreEqual(1,restarted);
                Assert.AreNotSame(original,session.Save);
                Assert.IsTrue(session.NeedsEmployerChoice);
            } finally { UnityEngine.Object.DestroyImmediate(root); Tween.Clear(); }
        }
        [Test] public void CancelKeepsProgress()
        {
            var root=new GameObject("ResetTest",typeof(RectTransform));
            try {
                var original=session.Save;
                ProgressResetDialog.Show(null,session,root.transform,()=>Assert.Fail("Cancelled reset"));
                root.GetComponentsInChildren<Button>(true).First(b=>b.name=="CancelProgressReset").onClick.Invoke();
                Assert.AreSame(original,session.Save);
            } finally { UnityEngine.Object.DestroyImmediate(root); Tween.Clear(); }
        }
    }
}
