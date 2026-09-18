using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using AfterSeoul.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class ExplorationUiTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject host;
        private GameSession session;
        private ExplorationView view;
        private FailingFiles fileStore;

        [SetUp] public void Setup()
        {
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            var clock = new TestClock(DateTimeOffset.UtcNow);
            fileStore = new FailingFiles();
            session = new GameSession(new SaveService(fileStore, new NewtonsoftJsonCodec(), clock), data, clock);
            session.Boot(); session.ChooseEmployer("HWANG");
            session.Save.ExplorationTutorialSeen = 15;
            host = new GameObject("ExplorationUiTest"); host.SetActive(false);
            var shell = host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady", Hidden).Invoke(shell, new object[] { session });
            shell.OpenExploration();
            view = host.GetComponentInChildren<ExplorationView>(true);
            Assert.IsNotNull(view);
            Assert.IsNull(PlayerEquipment.Equipped(session.Save, "Weapon"));
            Assert.IsTrue(view.GetComponentsInChildren<Text>(true).Any(t => t.name == "FirstQuestDialogue"));
            Find("AcceptFirstQuest").onClick.Invoke();
            Assert.IsTrue(view.GetComponentsInChildren<Text>(true).Any(t => t.name == "StarterGiftLine"));
            Find("ReceiveStarterPistol").onClick.Invoke();
        }

        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(host); Tween.Clear(); }
        private void Call(string method, params object[] args) => typeof(ExplorationView).GetMethod(method, Hidden).Invoke(view, args);
        private Button Find(string name) => view.GetComponentsInChildren<Button>(true).Last(b => b.name == name);


        [Test] public void PreparationOffersQuickEquipmentAndPreservesSelectedMapAfterSwap()
        {
            Warehouse.TryAdd(session.Save.Warehouse, session.Data, "WPN01", 1);
            Warehouse.TryAdd(session.Save.Warehouse, session.Data, "AMO01", 40);
            Find("Explore_YONGSAN_MARKET").onClick.Invoke();
            Assert.IsNotNull(Find("QuickSlot_Weapon"));
            Find("QuickSlot_Weapon").onClick.Invoke();
            Assert.IsTrue(view.GetComponentsInChildren<Text>(true).Any(t => t.name == "Compare_WPN01" && t.text.Contains("5.45x39")));
            Find("Equip_WPN01").onClick.Invoke();
            Assert.AreEqual("WPN01", PlayerEquipment.Equipped(session.Save, "Weapon"));
            Assert.IsNotNull(Find("EnterSelectedMap"));
            Find("QuickPack").onClick.Invoke();
            Find("EnterSelectedMap").onClick.Invoke();
            Assert.AreEqual(40, ExplorationSystem.AmmoRemaining(session.Save));
            Assert.IsTrue(session.Save.Exploration.AwaitingEntryChoice);
        }

        [Test] public void MeleePickerExplainsActualDamageBeforeEquipping()
        {
            Call("PickEquipment", "Melee");
            Assert.IsTrue(view.GetComponentsInChildren<Text>(true).Any(t => t.name == "Compare_MEL01" && t.text.Contains("26")));
        }

        [Test] public void FullBagDoesNotSilentlyGrantNewLootOrAllowUnresolvedContinuation()
        {
            Assert.IsTrue(ExplorationSystem.Start(session.Save, session.Data, "YONGSAN_MARKET"));
            var run = session.Save.Exploration;
            run.Phase = ExplorationPhase.LootChoice;
            foreach (var item in session.Data.AllItems.Take(80)) run.Loot.Add(new ItemStack(item.Id, 1));
            var chosen = session.Data.AllItems.Last();
            run.LootOptions = new System.Collections.Generic.List<ItemStack> { new ItemStack(chosen.Id, 1) };
            int before = run.Loot.Count;
            Assert.IsTrue(ExplorationSystem.ChooseLoot(session.Save, 0));
            Assert.AreEqual(before, run.Loot.Count, "New loot must await a space decision.");
            Assert.IsFalse(ExplorationSystem.ContinueEncounter(session.Save));
            Call("Render");
            Assert.IsNotNull(Find("ManageLoot"));
        }

        [Test] public void PreparationCanHealAndReconcilePackedItemsWithoutBlockingDeparture()
        {
            session.Save.Player.Hp = 0;
            Find("Explore_YONGSAN_MARKET").onClick.Invoke();
            Assert.IsFalse(Find("EnterSelectedMap").interactable);
            Find("Recover_MED05").onClick.Invoke();
            Assert.Greater(session.Save.Player.Hp, 0);
            Assert.IsTrue(Find("EnterSelectedMap").interactable);
            Find("EnterSelectedMap").onClick.Invoke();
            Assert.IsTrue(session.Save.Exploration.AwaitingEntryChoice);
        }

        [Test] public void FullBagUiRequiresDiscardConfirmationThenCollectsPendingLoot()
        {
            Assert.IsTrue(ExplorationSystem.Start(session.Save, session.Data, "YONGSAN_MARKET"));
            var run = session.Save.Exploration;
            run.Phase = ExplorationPhase.EncounterResult;
            foreach (var item in session.Data.AllItems.Take(8)) run.Loot.Add(new ItemStack(item.Id, 1));
            run.PendingLoot.Add(new ItemStack("WPN01", 1));
            Call("Render"); Find("ManageLoot").onClick.Invoke();
            Assert.IsFalse(Find("TakePendingLoot").interactable);
            string discard = run.Loot[0].ItemId;
            Find("Discard_" + discard).onClick.Invoke();
            Assert.AreEqual(8, session.Save.Exploration.Loot.Count);
            Find("ConfirmDiscard").onClick.Invoke();
            Assert.IsTrue(Find("TakePendingLoot").interactable);
            fileStore.Fail = true;
            Find("TakePendingLoot").onClick.Invoke();
            Assert.AreEqual(1, session.Save.Exploration.PendingLoot.Count);
            Assert.IsFalse(session.Save.Exploration.Loot.Any(x => x.ItemId == "WPN01"));
            fileStore.Fail = false;
            Find("RetryExplorationSave").onClick.Invoke();
            Find("ManageLoot").onClick.Invoke();
            Find("TakePendingLoot").onClick.Invoke();
            Assert.IsTrue(session.Save.Exploration.Loot.Any(x => x.ItemId == "WPN01"));
            Find("ContinueEncounter").onClick.Invoke();
            Assert.AreEqual(ExplorationPhase.Routes, session.Save.Exploration.Phase);
        }

        [Test] public void RecommendedPackUsesOwnedAlternativesWhenStarterFoodRunsOut()
        {
            foreach (string id in new[] { "MED05", "FOOD01", "FOOD05" })
                Warehouse.TryRemove(session.Save.Warehouse, id, Warehouse.CountOf(session.Save.Warehouse, id));
            foreach (string id in new[] { "MED01", "FOOD09", "FOOD02" }) Warehouse.TryAdd(session.Save.Warehouse, session.Data, id, 2);
            Find("Explore_YONGSAN_MARKET").onClick.Invoke();
            Find("QuickPack").onClick.Invoke();
            Find("EnterSelectedMap").onClick.Invoke();
            foreach (string id in new[] { "MED01", "FOOD09", "FOOD02" })
                Assert.AreEqual(2, session.Save.Exploration.Supplies.Single(x => x.ItemId == id).Count);
        }

        [Test] public void RaidQuestPanelSeparatesStoredItemsFromUnsecuredLoot()
        {
            session.Save.FirstExplorationQuest.Completed = true;
            var pool = session.Data.GetQuestPool(Employers.QuestPoolId(session.Data, "HWANG"));
            var def = pool.First(q => session.Save.Quests.Active.Any(a => a.QuestId == q.Id && !a.Delivered));
            session.Save.TrackedQuestId = "daily:" + def.Id;
            Assert.IsTrue(ExplorationSystem.Start(session.Save, session.Data, "YONGSAN_MARKET"));
            var req = def.Requires[0];
            string id = string.IsNullOrEmpty(req.ItemId) ? session.Data.AllItems.First(i => i.Tags.Contains(req.Tag)).Id : req.ItemId;
            session.Save.Exploration.Loot.Add(new ItemStack(id, 2));
            Call("ShowQuestObjectives");
            var objective = view.GetComponentsInChildren<Text>(true).Last(t => t.name == "QuestObjective");
            StringAssert.Contains("전리품 2", objective.text);
            StringAssert.Contains("생존 귀환", objective.text);
        }

        [Test] public void UnityStartMessagesDoNotTakeGameplayParameters()
        {
            var invalid = typeof(ExplorationView).Assembly.GetTypes()
                .Where(t => typeof(MonoBehaviour).IsAssignableFrom(t))
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m.Name == "Start" && m.GetParameters().Length != 0)
                .Select(m => m.DeclaringType.Name + "." + m.Name).ToArray();
            Assert.IsEmpty(invalid, "Unity Start messages cannot accept gameplay arguments.");
        }

        [Test] public void PreparationHidesUnopenedMapsAndRevealsOnlyNextMainlineMap()
        {
            var names = view.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Explore_")).Select(b => b.name).ToArray();
            CollectionAssert.AreEqual(new[] { "Explore_YONGSAN_MARKET" }, names);
            session.Save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
            Call("Render");
            names = view.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Explore_")).Select(b => b.name).ToArray();
            CollectionAssert.AreEqual(new[] { "Explore_YONGSAN_MARKET", "Explore_GURO_FACTORY" }, names);
        }

        [Test] public void MapTapOpensDetailsAndEntryStartsWithTwoSafeChoices()
        {
            Assert.IsFalse(view.GetComponentsInChildren<ScrollRect>(true).Any());
            Find("Explore_YONGSAN_MARKET").onClick.Invoke();
            Assert.IsNull(session.Save.Exploration);
            Assert.IsNotNull(Find("EnterSelectedMap"));
            Find("EnterSelectedMap").onClick.Invoke();
            Assert.IsTrue(session.Save.Exploration.AwaitingEntryChoice);
            Assert.IsNull(session.Save.Exploration.Enemy);
            Assert.IsNotNull(Find("Route0")); Assert.IsNotNull(Find("Route1"));
        }
        [Test] public void DispatchOverviewHidesLockedRegionsAndShowsDetailsOnlyAfterTap()
        {
            var shell = host.GetComponent<AppShell>();
            var screen = new AfterSeoul.Unity.UI.Screens.ExpeditionScreen();
            typeof(ScreenBase).GetMethod("Create", Hidden).Invoke(screen, new object[] { shell, session, host.transform });
            screen.Refresh();
            var root = host.transform.Find("Screen_탐색");
            Assert.IsFalse(root.GetComponentsInChildren<ScrollRect>(true).Any());
            var markers = root.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("MapSelect_")).ToArray();
            Assert.AreEqual(1, markers.Length); Assert.AreEqual("MapSelect_YONGSAN_MARKET",markers[0].name);
            Assert.IsFalse(root.GetComponentsInChildren<Button>(true).Any(b=>b.name.StartsWith("Depart_")));
            markers[0].onClick.Invoke();
            Assert.IsTrue(root.GetComponentsInChildren<Button>(true).Any(b=>b.name=="Depart_YONGSAN_MARKET"));
        }

        [TestCase(0)] [TestCase(30)]
        public void RouteSelectionNeverShowsPreviousEnemy(double remainingHp)
        {
            session.Save.Exploration = new ExplorationState { MapId = "YONGSAN_MARKET", Location = "매장", Phase = ExplorationPhase.Routes,
                Detected = true, Enemy = new ExplorationEnemy { Hp = remainingHp, Action = EnemyAction.Aiming }, Routes = new[] { "왼쪽", "오른쪽" } };
            Call("Render");
            var enemy = view.GetComponentsInChildren<Image>(true).Single(i => i.name == "EnemyArtwork");
            Assert.IsFalse(enemy.gameObject.activeSelf, "Completed encounters must not retain a threatening enemy pose.");
        }

        [Test] public void NewlyOpenedRegionIntroducesContactBeforeConsumingSupplies()
        {
            session.Save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
            Call("Render");
            Find("Explore_GURO_FACTORY").onClick.Invoke();
            Find("EnterSelectedMap").onClick.Invoke();
            Assert.IsNull(session.Save.Exploration);
            Assert.IsNotNull(Find("MeetRegionalNpc"));
            Find("MeetRegionalNpc").onClick.Invoke();
            Assert.IsTrue(view.GetComponentsInChildren<Image>(true).Any(i => i.name == "RegionalNpcPortrait" && i.sprite != null));
            Find("AcceptRegionalQuest").onClick.Invoke();
            Assert.IsTrue(RegionalExplorationQuest.Progress(session.Save, "GURO_FACTORY").Accepted);
            Assert.IsNull(session.Save.Exploration);
            Find("Explore_GURO_FACTORY").onClick.Invoke();
            Find("EnterSelectedMap").onClick.Invoke();
            Assert.AreEqual("GURO_FACTORY", session.Save.Exploration.MapId);
        }

        [TestCase(true)] [TestCase(false)]
        public void FailedOnboardingSaveRestoresRequiredDialogueOnRetry(bool acceptingQuest)
        {
            if (acceptingQuest) session.Save.FirstExplorationQuest = new FirstExplorationQuestProgress();
            else session.Save.ExplorationStarterPrepared = false;
            Call("ShowArrivalDialogue");
            string button = acceptingQuest ? "AcceptFirstQuest" : "ReceiveStarterPistol";
            fileStore.Fail = true;
            Find(button).onClick.Invoke();
            Assert.IsFalse(acceptingQuest ? session.Save.FirstExplorationQuest.Accepted : session.Save.ExplorationStarterPrepared);
            fileStore.Fail = false;
            Find("RetryExplorationSave").onClick.Invoke();
            Assert.IsTrue(Find(button).gameObject.activeSelf);
            Find(button).onClick.Invoke();
            Assert.IsTrue(acceptingQuest ? session.Save.FirstExplorationQuest.Accepted : session.Save.ExplorationStarterPrepared);
        }

        [Test] public void FirstNpcGiftEquipsOnePistolAndLetsPlayerDepart()
        {
            Assert.AreEqual("WPN04", PlayerEquipment.Equipped(session.Save, "Weapon"));
            Assert.IsTrue(Find("Explore_YONGSAN_MARKET").interactable);
            Find("Explore_YONGSAN_MARKET").onClick.Invoke();
            Find("EnterSelectedMap").onClick.Invoke();
            Assert.IsTrue(ExplorationSystem.IsActive(session.Save));
            Assert.AreEqual(50, ExplorationSystem.AmmoRemaining(session.Save));
        }

        [Test] public void EquipmentChangeReturnsToBodySlotsAndCanContinueToSafeEntry()
        {
            Warehouse.TryAdd(session.Save.Warehouse, session.Data, "MEL01", 1);
            Find("Loadout").onClick.Invoke();
            foreach (var slot in PlayerEquipment.Slots) Assert.IsNotNull(Find("Slot_" + slot));
            Find("Slot_Melee").onClick.Invoke(); Find("Equip_MEL01").onClick.Invoke();
            Assert.AreEqual("MEL01", PlayerEquipment.Equipped(session.Save, "Melee"));
            Assert.IsNotNull(Find("Pack")); Assert.IsNotNull(Find("Slot_Melee"));
            Call("CloseModal");
            Find("Explore_YONGSAN_MARKET").onClick.Invoke(); Find("EnterSelectedMap").onClick.Invoke();
            Assert.IsTrue(session.Save.Exploration.AwaitingEntryChoice);
            Find("Route0").onClick.Invoke();
            Assert.IsFalse(session.Save.Exploration.AwaitingEntryChoice);
            Assert.IsNull(session.Save.Exploration.Enemy);
        }
        private void StartCombat()
        {
            Assert.IsTrue(ExplorationSystem.Start(session.Save, session.Data, "YONGSAN_MARKET"));
            session.Save.Exploration.Phase = ExplorationPhase.Combat;
            session.Save.Exploration.Enemy = new ExplorationEnemy { Name = "PMC", Kind = "PMC", WeaponId = "WPN01", Action = EnemyAction.Aiming, Remaining = 1.5 };
            Call("Render");
        }

        [Test] public void VictoryReceiptShowsLootBeforeRouteButtons()
        {
            StartCombat();
            session.Save.Player.Equipment["Melee"] = "MEL01";
            session.Save.Exploration.Enemy.Hp = 1;
            Assert.IsTrue(ExplorationSystem.Melee(session.Save, session.Data));
            Call("CheckPhase");
            Assert.IsTrue(view.GetComponentsInChildren<Text>(true).Any(t => t.name == "EncounterResultTitle"));
            Assert.IsFalse(view.GetComponentsInChildren<Button>(true).Any(b => b.name == "Route0"));
            foreach (var item in session.Save.Exploration.EncounterLoot)
                Assert.IsTrue(view.GetComponentsInChildren<Text>(true).Any(t => t.name == "Name" && t.text.EndsWith(" × " + item.Count)));
            Find("ContinueEncounter").onClick.Invoke();
            Assert.AreEqual(ExplorationPhase.Routes, session.Save.Exploration.Phase);
            Assert.IsNotNull(Find("Route0"));
        }

        [Test] public void FirstQuestContainerLetsPlayerChooseAnItemAndShowsReceipt()
        {
            Find("Explore_YONGSAN_MARKET").onClick.Invoke();
            Find("EnterSelectedMap").onClick.Invoke();
            Assert.IsNull(session.Save.Exploration.Enemy);
            Find("Route0").onClick.Invoke();
            Find("EncounterPrimary").onClick.Invoke();
            Assert.AreEqual(ExplorationPhase.LootChoice, session.Save.Exploration.Phase);
            var chosen = session.Save.Exploration.LootOptions[1];
            Find("LootChoice_1").onClick.Invoke();
            Assert.AreEqual(chosen.ItemId, session.Save.Exploration.EncounterLoot[0].ItemId);
            Assert.IsTrue(session.Save.Exploration.FirstQuestContainerSearched);
            Assert.IsNotNull(Find("ContinueEncounter"));
        }

        [Test] public void EatingAndDrinkingReenableRoutesWithoutReenteringScreen()
        {
            Assert.IsTrue(ExplorationSystem.Start(session.Save, session.Data, "YONGSAN_MARKET"));
            var run = session.Save.Exploration;
            run.AwaitingEntryChoice = false; run.NodeIndex = 1; run.IntermediateExitIndex = 4;
            run.Phase = ExplorationPhase.Routes; session.Save.Player.Energy = 0; session.Save.Player.Hydration = 20;
            run.Supplies.Add(new ItemStack("FOOD01", 1)); run.Supplies.Add(new ItemStack("FOOD05", 1));
            Call("Render"); Assert.IsFalse(Find("Route0").interactable);
            foreach (var id in new[] { "FOOD01", "FOOD05" }) {
                Find("FieldSupplies").onClick.Invoke(); Find("Use_" + id).onClick.Invoke();
                Assert.IsFalse(Find("Route0").interactable, "Moving while consuming must be blocked.");
                ExplorationSystem.Tick(session.Save, session.Data, 10); Call("CheckPhase");
                Assert.IsTrue(Find("Route0").interactable, "Restored energy must update the existing route buttons.");
                Assert.IsTrue(Find("Route1").interactable);
            }
            Assert.Greater(session.Save.Player.Energy, 0); Assert.Greater(session.Save.Player.Hydration, 20);
            Find("Route0").onClick.Invoke(); Assert.AreEqual(2, session.Save.Exploration.NodeIndex);
        }

        [Test] public void FoundAmmoEnablesAttackEvenIfDepartedWithoutBullets()
        {
            StartCombat();
            Assert.IsFalse(Find("Fire_Single").interactable);
            session.Save.Exploration.Loot.Add(new ItemStack("AMO05", 4));
            Call("UpdateLabels");
            Assert.IsTrue(Find("Fire_Single").interactable);
            Find("Fire_Single").onClick.Invoke();
            Assert.AreEqual(3, ExplorationSystem.AmmoRemaining(session.Save));
        }

        [Test] public void RouteLessonDoesNotSuppressFirstCombatLesson()
        {
            session.Save.ExplorationTutorialSeen = 0;
            Call("MaybeGuide", 3, "route", "extract");
            Find("ExplorationGuideContinue").onClick.Invoke();
            Assert.AreEqual(8, session.Save.ExplorationTutorialSeen);
            Call("MaybeGuide", 2, "combat", "cover");
            Assert.IsTrue(view.GetComponentsInChildren<Text>(true).Any(t => t.name == "GuideText" && t.text == "cover"));
            Find("ExplorationGuideContinue").onClick.Invoke();
            Assert.AreEqual(12, session.Save.ExplorationTutorialSeen);
        }

        [Test] public void HelpFreezesLiveEnemyAndClosesWithoutRestarting()
        {
            StartCombat();
            var enemy = session.Save.Exploration.Enemy;
            Call("ShowHelp");
            Call("Update");
            Assert.AreEqual(1.5, enemy.Remaining);
            Call("CloseModal");
            Assert.AreSame(enemy, session.Save.Exploration.Enemy);
        }

        [Test] public void QuestObjectivesPauseCombatAndResumeTheSameEncounter()
        {
            StartCombat(); var enemy = session.Save.Exploration.Enemy;
            Find("ExplorationQuests").onClick.Invoke(); Call("Update");
            Assert.AreEqual(1.5, enemy.Remaining);
            Assert.IsTrue(view.GetComponentsInChildren<Text>(true).Any(t => t.name == "QuestObjective"));
            Find("ResumeQuestExploration").onClick.Invoke();
            Assert.AreSame(enemy, session.Save.Exploration.Enemy);
            Assert.IsNull(view.transform.Find("ExplorationModal"));
        }

        [Test] public void RejectedSavedCommandRestoresAllInventoryAndCurrency()
        {
            int before = Warehouse.CountOf(session.Save.Warehouse, "MED05");
            long money = session.Save.Player.Money;
            Assert.IsFalse(session.ExecuteSavedAction(s => {
                Warehouse.TryRemove(s.Warehouse, "MED05", 1); s.Player.Money += 9000; return false;
            }));
            Assert.AreEqual(before, Warehouse.CountOf(session.Save.Warehouse, "MED05"));
            Assert.AreEqual(money, session.Save.Player.Money);
        }

        [Test] public void DeathReturnFadesToChosenEmployerAndAcknowledgesOnlyOnce()
        {
            StartCombat();
            session.Save.Player.EmployerNpcId = "DR_CHOI";
            session.Save.Player.Hp = 0;
            ExplorationSystem.Tick(session.Save, session.Data, .1);
            Call("Render");
            Assert.AreEqual(ExplorationOutcome.Death, session.Save.Exploration.Result.Outcome);
            Find("ReturnToBase").onClick.Invoke();
            Tween.Tick(.5f); Tween.Tick(.6f); Tween.Tick(.3f);
            var line = view.GetComponentsInChildren<Text>(true).Single(t => t.name == "RescueLine");
            Assert.AreEqual(Loc.Text("내가 데려와서 응급처치를 했어요. 다음에는 몸 상태를 보고, 늦기 전에 돌아오세요."), line.text);
            Assert.IsTrue(session.Save.Exploration.Result.Acknowledged);
            Assert.IsFalse(ExplorationSystem.Acknowledge(session.Save));
            Find("RescueContinue").onClick.Invoke();
            Assert.IsNull(host.GetComponentInChildren<ExplorationView>(true));
        }

        [Test] public void DiskFailureRollsBackInventoryAndPendingLevelNotification()
        {
            var files = new FailingFiles();
            var clock = new TestClock(DateTimeOffset.UtcNow);
            var failedSession = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), session.Data, clock);
            failedSession.Boot();
            failedSession.ChooseEmployer("HWANG");
            files.Fail = true;
            string before = new NewtonsoftJsonCodec().Serialize(failedSession.Save);
            Assert.Throws<IOException>(() => failedSession.ExecuteSavedAction(s => {
                Warehouse.TryAdd(s.Warehouse, session.Data, "MED05", 1);
                s.Player.Exp += 100000;
                return true;
            }));
            Assert.AreEqual(before, new NewtonsoftJsonCodec().Serialize(failedSession.Save));
            Assert.AreEqual(0, failedSession.ConsumeLevelUps());
        }

        private sealed class FailingFiles : IFileStore
        {
            private readonly MemoryFileStore inner = new MemoryFileStore();
            public bool Fail;
            public bool Exists(string p) => inner.Exists(p);
            public string ReadAllText(string p) => inner.ReadAllText(p);
            public void WriteAllText(string p, string data) { if (Fail) throw new IOException("disk unavailable"); inner.WriteAllText(p, data); }
            public void Replace(string a, string b) => inner.Replace(a, b);
            public bool TryMove(string a, string b) => inner.TryMove(a, b);
        }
    }
}
