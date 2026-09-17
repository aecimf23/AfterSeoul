using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;
using UnityEngine;
using UnityEngine.UI;
namespace AfterSeoul.Tests {
 public class AccountResetTests {
  const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
  GameSession session; SaveService saves; FailingStore files; GameObject host;
  [SetUp] public void Setup() {
   var data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Data",n)));
   files=new FailingStore(); var clock=new TestClock(DateTimeOffset.UtcNow);
   saves=new SaveService(files,new NewtonsoftJsonCodec(),clock);
   session=new GameSession(saves,data,clock); session.Boot(); session.ChooseEmployer("HWANG");
   session.Save.Player.Money=999999; session.Save.ExplorationStarterPrepared=true;
   session.Save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET"); session.Commit();
  }
  [TearDown] public void Cleanup() { if(host!=null) UnityEngine.Object.DestroyImmediate(host); Tween.Clear(); }
  [Test] public void ResetPersistsFreshOnboardingWithoutOldProgress() {
   session.ResetProgress(); var reloaded=saves.LoadOrCreate();
   Assert.IsTrue(session.NeedsEmployerChoice); Assert.AreEqual(0,reloaded.WelcomePage);
   Assert.IsNull(reloaded.Player.EmployerNpcId); Assert.IsFalse(reloaded.ExplorationStarterPrepared);
   Assert.IsFalse(reloaded.FirstExplorationQuest.Accepted); Assert.IsEmpty(reloaded.SurvivedExplorationMapIds);
   Assert.IsEmpty(reloaded.Scavs); Assert.IsEmpty(reloaded.Expeditions); Assert.IsNull(reloaded.Exploration);
   Assert.AreEqual(0,reloaded.Player.Money); Assert.IsTrue(session.LastReport.IsEmpty);
  }
  [Test] public void FailedSaveKeepsCurrentAndPersistedProgress() {
   var before=new NewtonsoftJsonCodec().Serialize(session.Save); files.Fail=true;
   Assert.Throws<IOException>(()=>session.ResetProgress()); files.Fail=false;
   Assert.AreEqual(before,new NewtonsoftJsonCodec().Serialize(session.Save));
   Assert.AreEqual(before,new NewtonsoftJsonCodec().Serialize(saves.LoadOrCreate()));
  }
  [Test] public void ResetRequiresConfirmationAndCancelPreservesSave() {
   host=new GameObject("ResetUi"); host.SetActive(false); var shell=host.AddComponent<AppShell>();
   typeof(AppShell).GetMethod("OnReady",Hidden).Invoke(shell,new object[]{session});
   typeof(AppShell).GetMethod("OpenSettings",Hidden).Invoke(shell,null);
   var before=new NewtonsoftJsonCodec().Serialize(session.Save);
   Click("ResetAccount"); Assert.AreEqual(before,new NewtonsoftJsonCodec().Serialize(session.Save));
   Click("CancelAccountReset"); Assert.AreEqual(before,new NewtonsoftJsonCodec().Serialize(session.Save));
   Assert.IsTrue(host.GetComponentsInChildren<Button>(true).Any(b=>b.name=="ResetPreferences"));
   Click("ResetAccount"); Click("ConfirmAccountReset");
   Assert.IsTrue(session.NeedsEmployerChoice); Assert.AreEqual(0,session.Save.WelcomePage);
   Assert.IsTrue(host.GetComponentsInChildren<RectTransform>(true).Any(r=>r.name=="WelcomeBriefing"));
   Assert.AreEqual(1,host.GetComponentsInChildren<Canvas>(true).Length);
  }
  void Click(string id)=>host.GetComponentsInChildren<Button>(true).Last(b=>b.name==id).onClick.Invoke();
  sealed class FailingStore:IFileStore {
   MemoryFileStore inner=new MemoryFileStore(); public bool Fail;
   public bool Exists(string p)=>inner.Exists(p); public string ReadAllText(string p)=>inner.ReadAllText(p);
   public void WriteAllText(string p,string c){if(Fail)throw new IOException("test disk failure");inner.WriteAllText(p,c);}
   public void Replace(string a,string b)=>inner.Replace(a,b); public bool TryMove(string a,string b)=>inner.TryMove(a,b);
  }
 }
}
