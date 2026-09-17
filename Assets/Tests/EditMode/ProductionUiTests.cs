using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using AfterSeoul.Unity.UI;
using AfterSeoul.Unity.UI.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace AfterSeoul.Tests
{
    public class ProductionUiTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject host;
        AppShell shell;
        GameSession session;
        FactoryScreen factory;
        [SetUp] public void SetUp()
        {
            var clock = new TestClock(DateTimeOffset.Parse("2026-09-16T00:00:00Z"));
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Data",n)));
            session = new GameSession(new SaveService(new MemoryFileStore(),new NewtonsoftJsonCodec(),clock),data,clock);
            session.Boot(); session.ChooseEmployer("HWANG"); session.Save.WelcomePage = -1;
            host = new GameObject("ProductionUi"); host.SetActive(false);
            shell = host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady",Private).Invoke(shell,new object[] {session});
            shell.SelectByName("공장");
            factory = ((System.Collections.Generic.List<ScreenBase>)typeof(AppShell).GetField("_screens",Private).GetValue(shell)).OfType<FactoryScreen>().Single();
        }
        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(host); Tween.Clear(); }
        Button Button(string name) => host.GetComponentsInChildren<Button>(true).First(b => b.name == name);
        Image Art(string name) => host.GetComponentsInChildren<Image>(true).Single(i => i.name == name);
        void Press() => Button("ProductionTap").GetComponent<PressButton>().OnPointerDown(new PointerEventData(null));
        void Hit()
        {
            for (int i=0; i<200; i++) {
                if (session.Conveyor.CooldownRemaining <= 0 && session.Conveyor.Parts.Any(p => p.Position >= session.Conveyor.HitStart && p.Position <= session.Conveyor.HitEnd)) { Press(); return; }
                factory.Tick(.05f);
            }
            Assert.Fail("No conveyor part reached the strike zone");
        }

        [Test] public void RapidPresses_DoNotProduce_AndReleaseDoesNotAwardTwice()
        {
            for (int i=0; i<500; i++) { Press(); factory.Tick(1f/120); }
            Assert.AreEqual(0,session.Save.Factory.Production.WorkDone);
            Assert.AreEqual(0,session.Save.Factory.Production.TotalProduced);
            Hit(); double work=session.Save.Factory.Production.WorkDone;
            Assert.Greater(work,0);
            Button("ProductionTap").onClick.Invoke();
            Assert.AreEqual(work,session.Save.Factory.Production.WorkDone);
        }

        [Test] public void EquipmentScreen_DoesNotAdvanceManualParts()
        {
            double position=session.Conveyor.Parts[0].Position;
            Button("FactoryEquipment").onClick.Invoke(); factory.Tick(10);
            Assert.AreEqual(position,session.Conveyor.Parts[0].Position);
            Assert.AreEqual(0,session.Save.Factory.Production.WorkDone);
        }

        [Test] public void ConveyorSprite_TracksTheJudgedPart_AndPlayerShowsImpact()
        {
            factory.Tick(.1f);
            var part = session.Conveyor.Parts[0];
            var image = Art("ConveyorPart0");
            Assert.IsNotNull(image.sprite);
            Assert.AreEqual(GameArt.Material(part.Tier), image.sprite);
            Assert.AreEqual((float)part.Position, image.rectTransform.anchorMin.x, .00001f);
            Hit();
            Assert.AreEqual(GameArt.Worker(2), Art("ProductionWorker").sprite);
            factory.Tick(.2f);
            Assert.AreEqual(GameArt.Worker(3), Art("ProductionWorker").sprite);
        }

        [Test] public void FirstGun_CanBeMadeWithoutMaterials_AndUnlocksFirstHireSupport()
        {
            var wagesBefore = session.Save.Player.Money;
            var itemsBefore = session.Save.Warehouse.Stacks.Sum(s => s.Count);
            Assert.IsTrue(Button("ProductionTap").interactable);
            for (int i = 0; i < 10; i++) Hit();
            Assert.Greater(session.Save.Player.Money, wagesBefore);
            Assert.AreEqual(itemsBefore,session.Save.Warehouse.Stacks.Sum(s => s.Count));
            Assert.IsTrue(StarterSupport.Ready(session.Save));
            Assert.IsNotNull(Art("ProductionWeaponArt").sprite);
        }

        [Test] public void ThreeAssignedScavs_HaveNamedStations_Animate_AndShowAutomaticEta()
        {
            session.Save.Player.Money = 1000000;
            session.UpgradeProductionEquipment(ProductionEquipment.ExtraBench);
            session.UpgradeProductionEquipment(ProductionEquipment.ExtraBench);
            for (int i=0; i<4; i++) {
                session.Save.Scavs.Add(new ScavState { Uid="crew"+i, Name="Worker"+i, Status=ScavStatus.Idle });
                bool assigned=session.AssignProductionScav("crew"+i);
                Assert.AreEqual(i<3, assigned);
            }
            factory.Refresh();
            Text Label(string name) => host.GetComponentsInChildren<Text>(true).Single(t=>t.name==name);
            for (int i=0; i<3; i++) Assert.AreEqual("Worker"+i,Label("ProductionScavName"+i).text);
            Sprite resting=Art("ProductionScavArt0").sprite;
            Assert.IsNotNull(resting);
            factory.Tick(.6f);
            Assert.AreNotEqual(resting,Art("ProductionScavArt0").sprite);
            ((TestClock)session.Clock).Advance(TimeSpan.FromSeconds(1));
            factory.Tick(.1f);
            StringAssert.Contains("0.03",Label("ProductionProgress").text);
            StringAssert.Contains("05:33",Label("ProductionAutoStatus").text);
            Assert.AreEqual(0,session.Save.Factory.Production.WorkDone,"Visual animation must not award extra work");
            session.UnassignProductionScav("crew1"); factory.Refresh();
            StringAssert.Contains("Worker2",Label("ProductionScavName1").text);
            Assert.IsFalse(Art("ProductionScavArt2").gameObject.activeSelf);
        }

        [Test] public void ActiveWorkstations_AreCentered_AndUnusedStationsHidden()
        {
            factory.Refresh();
            var player=Art("ProductionWorker").rectTransform;
            Assert.AreEqual(.5f,(player.anchorMin.x+player.anchorMax.x)/2,.001f);
            for(int i=0;i<3;i++) Assert.IsFalse(Art("ProductionScavArt"+i).gameObject.activeSelf);
            session.Save.Scavs.Add(new ScavState { Uid="center",Name="Center",Status=ScavStatus.Idle });
            session.AssignProductionScav("center"); factory.Refresh();
            var partner=Art("ProductionScavArt0").rectTransform;
            Assert.IsTrue(partner.gameObject.activeSelf);
            Assert.AreEqual(1f,(player.anchorMin.x+player.anchorMax.x+partner.anchorMin.x+partner.anchorMax.x)/2,.001f);
            session.UnassignProductionScav("center"); factory.Refresh();
            Assert.AreEqual(.5f,(player.anchorMin.x+player.anchorMax.x)/2,.001f);
        }

        [Test] public void ProductionPage_OffersUnlockAndEveryUpgrade_WithoutChangingTabs()
        {
            session.Save.Player.Money=100000; factory.Refresh();
            Assert.IsTrue(Button("InlineProductionUnlock").interactable);
            Button("InlineProductionUpgrade").onClick.Invoke();
            Assert.AreEqual(1,session.Save.Factory.Production.SpeedLevel);
            Button("InlineEquipment_AssemblyJig").onClick.Invoke();
            Assert.AreEqual(1,session.Save.Factory.Production.AssemblyJigLevel);
            Assert.IsNotNull(Button("InlineEquipment_PowerTools"));
            Assert.IsNotNull(Button("InlineEquipment_ExtraBench"));
            Assert.IsTrue(Button("ProductionTap").transform.parent.parent.gameObject.activeSelf);
            Button("InlineProductionUnlock").onClick.Invoke();
            Button("CommissionAccept").onClick.Invoke();
            Assert.IsNotNull(session.Save.Factory.Production.Contract);
            Assert.IsNotNull(Button("InlineCommissionContinue"));
        }

        [Test] public void UpgradeShortcut_LandsAtFirstCard_AndRefreshPreservesScroll()
        {
            shell.enabled=false; host.SetActive(true);
            Canvas.ForceUpdateCanvases();
            var scroll=host.GetComponentsInChildren<ScrollRect>(true).Single(s=>s.name=="ProductionScroll");
            scroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,1200);
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,1000);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Button("ProductionToEquipment").onClick.Invoke();
            var upgrades=(RectTransform)scroll.content.Find("ProductionUpgrades");
            Assert.Greater(upgrades.rect.height,500,"Exercise a laid-out scroll list, not zero-size inactive UI");
            float top=upgrades.anchoredPosition.y+upgrades.rect.height*(1-upgrades.pivot.y);
            Assert.AreEqual(0,scroll.content.anchoredPosition.y+top,2f,"Shortcut must show the next unlock card first");
            float before=scroll.content.anchoredPosition.y;
            factory.Refresh(); LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Assert.AreEqual(before,scroll.content.anchoredPosition.y,2f);
        }

        [Test] public void HomeGreeting_IsDeferredBySettings_DoesNotRestartOnRefresh_AndHidesOnLeaving()
        {
            shell.SelectByName("기지");
            var tick=typeof(AppShell).GetMethod("TickGreeting",Private);
            typeof(AppShell).GetMethod("OpenSettings",Private).Invoke(shell,null);
            tick.Invoke(shell,new object[]{.2f});
            var greeting=host.GetComponentsInChildren<Text>(true).Single(t=>t.name=="GreetingText");
            Assert.AreEqual("",greeting.text);
            typeof(AppShell).GetMethod("CloseSettings",Private).Invoke(shell,null);
            tick.Invoke(shell,new object[]{.2f});
            Assert.IsNotEmpty(greeting.text);
            string first=greeting.text;
            shell.RefreshHeader(); shell.SelectByName("기지");
            tick.Invoke(shell,new object[]{.2f});
            Assert.Greater(greeting.text.Length,first.Length);
            shell.SelectByName("공장");
            Assert.IsFalse(greeting.transform.parent.gameObject.activeSelf);
        }

        [Test] public void Equipment_HasSequentialUnlock_AndPreservesPartsWorkbench()
        {
            Button("FactoryEquipment").onClick.Invoke();
            Button("EquipmentTools").onClick.Invoke();
            Assert.IsNotNull(Button("ProductionUpgrade"));
            Button("EquipmentCommissions").onClick.Invoke();
            Assert.IsNotNull(Button("ProductionUnlock"));
            Button("FactoryParts").onClick.Invoke();
            Assert.IsTrue(Button("Action").interactable);
            Assert.IsNotNull(Button("R_RCP_SALVAGE"));
            Button("FactoryProduction").onClick.Invoke();
            Assert.IsTrue(Button("ProductionTap").interactable);
        }

        [Test] public void ProductionOnlyReport_ShowsQuantityAndWages()
        {
            var report = new ResolveReport { ProductionCompleted = 2, ProductionWages = 10000 };
            var lines = ReportLines.Build(report, session.Save, session.Data);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains("10,000", lines[0].Text);
        }

        [Test] public void PrototypeCommission_RequiresProductionAndTurnInBeforeUnlock()
        {
            Button("FactoryEquipment").onClick.Invoke();
            Button("EquipmentTools").onClick.Invoke();
            Assert.IsFalse(Button("ProductionUpgrade").interactable);
            Button("EquipmentCommissions").onClick.Invoke();
            Assert.IsFalse(Button("ProductionUnlock").interactable);
            Button("FactoryProduction").onClick.Invoke();
            for (int i=0; i<40; i++) Hit();
            Button("FactoryEquipment").onClick.Invoke();
            Button("EquipmentTools").onClick.Invoke();
            Button("ProductionUpgrade").onClick.Invoke();
            Button("EquipmentCommissions").onClick.Invoke();
            Button("ProductionUnlock").onClick.Invoke();
            Assert.AreEqual(1,session.Save.Factory.Production.SpeedLevel);
            Assert.AreEqual(1,session.Save.Factory.Production.UnlockedCount,"Opening a commission must not approve the gun");
            long money = session.Save.Player.Money;
            Button("CommissionAccept").onClick.Invoke();
            Assert.AreEqual(money,session.Save.Player.Money,"Operating cash is a threshold, not a fee");
            Assert.AreEqual("WPN21",session.Save.Factory.Production.SelectedWeaponId);
            Assert.AreEqual(GameArt.Weapon("WPN21"), Art("ProductionWeaponArt").sprite);
            for (int i=0; i<200 && !ProductionWork.TrialReady(session.Save); i++) Hit();
            Assert.AreEqual(1,session.Save.Factory.Production.UnlockedCount);
            Assert.IsFalse(Button("ProductionTap").interactable,"Finished prototypes wait for handover");
            Button("ProductionDeliver").onClick.Invoke();
            Assert.AreEqual(2,session.Save.Factory.Production.UnlockedCount);
            Assert.Greater(session.Save.Player.Money,money);
        }

        [Test] public void HiredScav_CanBeAssignedAndReleasedThroughEquipment()
        {
            for (int i=0; i<10; i++) Hit();
            Assert.IsNotNull(session.Hire(session.Save.Market.Offers.First(o => o.Tier == 1).OfferId));
            var scav = session.Save.Scavs.Single();
            Button("FactoryEquipment").onClick.Invoke();
            Button("EquipmentCrew").onClick.Invoke();
            Button("Assign_" + scav.Uid).onClick.Invoke();
            Assert.AreEqual(ScavStatus.Working, scav.Status);
            Button("Unassign_" + scav.Uid).onClick.Invoke();
            Assert.AreEqual(ScavStatus.Idle, scav.Status);
        }

        [Test] public void CraftedTools_SpendCashAndExpandCrewCapacity()
        {
            session.Save.Player.Money = 100000;
            Button("FactoryEquipment").onClick.Invoke(); Button("EquipmentTools").onClick.Invoke();
            Button("Equipment_AssemblyJig").onClick.Invoke();
            Assert.AreEqual(1, session.Save.Factory.Production.AssemblyJigLevel);
            Assert.AreEqual(92500, session.Save.Player.Money);
            Button("Equipment_ExtraBench").onClick.Invoke();
            Assert.AreEqual(2, AfterSeoul.Factory.ProductionWork.CrewCapacity(session.Save));
            session.Save.Scavs.Add(new ScavState { Uid="a", Name="A", Status=ScavStatus.Idle });
            session.Save.Scavs.Add(new ScavState { Uid="b", Name="B", Status=ScavStatus.Idle });
            session.Save.Scavs.Add(new ScavState { Uid="c", Name="C", Status=ScavStatus.Idle });
            Button("EquipmentCrew").onClick.Invoke();
            Button("Assign_a").onClick.Invoke(); Button("Assign_b").onClick.Invoke();
            Assert.IsFalse(Button("Assign_c").interactable);
            Button("Unassign_a").onClick.Invoke();
            Assert.IsTrue(Button("Assign_c").interactable);
        }

        [Test] public void AutomaticPrototypeCompletion_ShowsDeliveryAndApprovesRequestedGun()
        {
            session.Save.Player.Money=15000;
            session.Save.Scavs.Add(new ScavState { Uid="auto", Name="Auto", Status=ScavStatus.Idle });
            session.AssignProductionScav("auto");
            Button("FactoryEquipment").onClick.Invoke(); Button("ProductionUnlock").onClick.Invoke();
            Button("CommissionAccept").onClick.Invoke();
            ((TestClock)session.Clock).Advance(TimeSpan.FromHours(1));
            var report = session.Tick();
            Assert.IsTrue(report.ProductionCommissionReady);
            Assert.AreEqual(3,report.ProductionSamples);
            Assert.AreEqual(0,report.ProductionWages);
            Assert.IsTrue(Button("ProductionDeliver").gameObject.activeSelf);
            Assert.IsFalse(Button("ProductionTap").interactable);
            Button("ProductionPrevious").onClick.Invoke();
            Assert.AreEqual("WPN04",session.Save.Factory.Production.SelectedWeaponId);
            factory.Tick(.6f);
            Assert.AreEqual(GameArt.Worker(1, 0),Art("ProductionScavArt0").sprite,
                "An undelivered prototype must not stop the approved weapon's automatic work animation");
            Button("ProductionDeliver").onClick.Invoke(); Button("CommissionApproved").onClick.Invoke();
            Assert.AreEqual("WPN21",session.Save.Factory.Production.SelectedWeaponId);
        }
    }
}

