using System;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;

namespace AfterSeoul.Tests
{
    public class ExplorationTests
    {
        [Test] public void EveryNpcFirstQuestConnectsAcceptanceContainerExtractionAndReport()
        {
            var real = JsonDataRegistry.Load(n => System.IO.File.ReadAllText(System.IO.Path.Combine("Assets/StreamingAssets/Data", n)));
            foreach (var npc in new[] { "HWANG", "DR_CHOI", "YONGSAN_KIM" }) {
                var s = new GameSave(); s.Player.EmployerNpcId = npc;
                Assert.IsTrue(FirstExplorationQuest.Accept(s));
                Assert.IsTrue(ExplorationSystem.PrepareStarter(s, real));
                Assert.IsTrue(ExplorationSystem.Start(s, real, "YONGSAN_MARKET"));
                Assert.IsNull(s.Exploration.Enemy);
                Assert.IsTrue(ExplorationSystem.Move(s, real, 0));
                Assert.AreEqual(FirstExplorationQuest.RequiredContainer(s), s.Exploration.ContainerKind);
                Assert.IsTrue(ExplorationSystem.Choose(s, real, EncounterChoice.Search));
                Assert.IsTrue(ExplorationSystem.ChooseLoot(s, 0));
                Assert.IsTrue(ExplorationSystem.ContinueEncounter(s));
                Assert.IsTrue(s.Exploration.FirstQuestContainerSearched);
                s.Exploration.AwaitingEntryChoice = false;
                s.Exploration.NodeIndex = s.Exploration.IntermediateExitIndex;
                Assert.IsTrue(ExplorationSystem.Extract(s, real));
                Assert.IsTrue(s.FirstExplorationQuest.ReadyToReport);
                Assert.IsFalse(FirstExplorationQuest.Report(s));
                Assert.IsTrue(ExplorationSystem.Acknowledge(s));
                long before = s.Player.Money;
                Assert.IsTrue(FirstExplorationQuest.Report(s));
                Assert.AreEqual(before + 25000, s.Player.Money);
                Assert.IsFalse(FirstExplorationQuest.Report(s));
            }
        }

        [Test] public void ContainerChoicesAreDistinctSavedAndOnlyChosenItemIsGranted()
        {
            var real = JsonDataRegistry.Load(n => System.IO.File.ReadAllText(System.IO.Path.Combine("Assets/StreamingAssets/Data", n)));
            foreach (var kind in LootContainers.Kinds) {
                var options = LootContainers.RollChoices(real, kind, n => 0);
                Assert.AreEqual(2, options.Count, kind);
                Assert.AreNotEqual(options[0].ItemId, options[1].ItemId);
                var s = new GameSave(); s.Player.Equipment["Melee"] = "MEL01";
                Assert.IsTrue(ExplorationSystem.Start(s, real, "YONGSAN_MARKET"));
                var e = s.Exploration; e.Enemy = null; e.Phase = ExplorationPhase.Encounter; e.ContainerKind = kind; e.LootOptions = options;
                Assert.IsTrue(ExplorationSystem.Choose(s, real, EncounterChoice.Search));
                Assert.AreEqual(ExplorationPhase.LootChoice, e.Phase);
                Assert.IsFalse(ExplorationSystem.Move(s, real, 0));
                Assert.IsFalse(ExplorationSystem.ChooseLoot(s, -1));
                var codec = new NewtonsoftJsonCodec();
                s = codec.Deserialize<GameSave>(codec.Serialize(s));
                string before = codec.Serialize(s);
                ExplorationSystem.Tick(s, real, 20);
                Assert.AreEqual(before, codec.Serialize(s));
                Assert.IsTrue(ExplorationSystem.ChooseLoot(s, 1));
                Assert.AreEqual(ExplorationPhase.EncounterResult, s.Exploration.Phase);
                Assert.AreEqual(options[1].ItemId, s.Exploration.Loot[0].ItemId);
                Assert.AreEqual(options[1].Count, s.Exploration.Loot[0].Count);
                Assert.IsFalse(ExplorationSystem.ChooseLoot(s, 0));
                Assert.AreEqual(1, s.Exploration.Loot.Count);
            }
        }

        [Test] public void AvoidingEnemyStillOffersLootAndRouteHintMatchesNextContainer()
        {
            var s = Start(); var e = s.Exploration;
            e.Phase = ExplorationPhase.Encounter; e.Enemy = new ExplorationEnemy(); e.Detected = false;
            Assert.IsTrue(ExplorationSystem.Choose(s, data, EncounterChoice.Avoid));
            Assert.AreEqual(ExplorationPhase.LootChoice, e.Phase);
            Assert.IsTrue(ExplorationSystem.ChooseLoot(s, 0));
            Assert.IsTrue(ExplorationSystem.ContinueEncounter(s));
            string expected = e.RouteContainers[1];
            Assert.IsTrue(ExplorationSystem.Move(s, data, 1));
            Assert.AreEqual(expected, e.ContainerKind);
            Assert.Greater(e.LootOptions.Count, 0);
        }

        [Test] public void MainlineRouteOpensOneMapPerSurvivalAndBranchesAfterTheBase()
        {
            var real = JsonDataRegistry.Load(n => System.IO.File.ReadAllText(System.IO.Path.Combine("Assets/StreamingAssets/Data", n)));
            var s = new GameSave();
            s.Player.Equipment["Melee"] = "MEL01";
            // Neither high account level nor trader trust can skip direct exploration.
            s.Player.Level = 99; s.NpcTrust["HWANG"] = 99;
            var expected = new[] { "YONGSAN_MARKET", "GURO_FACTORY", "HAN_RIVER", "NAMSAN_WOODS", "GANGNAM_STREETS", "YONGSAN_BASE", "MYEONGDONG", "UIJEONGBU" };
            CollectionAssert.AreEqual(expected, System.Linq.Enumerable.Select(ExplorationSystem.OrderedMaps(real), m => m.Id));
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < expected.Length; j++)
                    Assert.AreEqual(j <= i, ExplorationSystem.RouteLockReason(s, expected[j]) == null, expected[j]);
                Assert.IsTrue(ExplorationSystem.Start(s, real, expected[i]));
                s.Exploration.Phase = ExplorationPhase.Routes;
                s.Exploration.AwaitingEntryChoice = false;
                s.Exploration.NodeIndex = s.Exploration.IntermediateExitIndex;
                Assert.IsTrue(ExplorationSystem.Extract(s, real));
                Assert.IsTrue(s.SurvivedExplorationMapIds.Contains(expected[i]));
                Assert.IsTrue(ExplorationSystem.Acknowledge(s));
                var codec = new NewtonsoftJsonCodec();
                s = codec.Deserialize<GameSave>(codec.Serialize(s));
            }
            foreach (var id in expected) Assert.IsNull(ExplorationSystem.RouteLockReason(s, id));
            Assert.IsNull(ExplorationSystem.StartBlockReason(s, real, "MYEONGDONG"));
            Assert.IsNull(ExplorationSystem.StartBlockReason(s, real, "UIJEONGBU"));
        }

        [Test] public void FailedReturnDoesNotUnlockNextMapAndOldSuccessfulResultIsPreserved()
        {
            var real = JsonDataRegistry.Load(n => System.IO.File.ReadAllText(System.IO.Path.Combine("Assets/StreamingAssets/Data", n)));
            var s = new GameSave(); s.Player.Equipment["Melee"] = "MEL01";
            Assert.IsTrue(ExplorationSystem.Start(s, real, "YONGSAN_MARKET"));
            Assert.IsTrue(ExplorationSystem.EmergencyReturn(s, real));
            Assert.IsNotNull(ExplorationSystem.RouteLockReason(s, "GURO_FACTORY"));
            Assert.AreEqual(0, s.SurvivedExplorationMapIds.Count);
            s.Exploration.Result.Outcome = ExplorationOutcome.Success;
            Assert.IsTrue(ExplorationSystem.Acknowledge(s));
            Assert.IsTrue(ExplorationSystem.Start(s, real, "GURO_FACTORY"));
            Assert.IsTrue(s.SurvivedExplorationMapIds.Contains("YONGSAN_MARKET"));
            Assert.IsNotNull(ExplorationSystem.RouteLockReason(s, "HAN_RIVER"));
        }

        [Test] public void VictoryShowsOnlyThisEncountersLootUntilConfirmedEvenAfterReload()
        {
            var s = Start();
            s.Player.Equipment["Melee"] = "MEL01";
            var e = s.Exploration;
            e.Loot.Add(new ItemStack("MED05", 99));
            e.Phase = ExplorationPhase.Combat;
            e.Enemy = new ExplorationEnemy { Name = "Test scav", Hp = 1 };
            Assert.IsTrue(ExplorationSystem.Melee(s, data));
            Assert.AreEqual(ExplorationPhase.EncounterResult, e.Phase);
            Assert.Greater(e.EncounterLoot.Count, 0);
            Assert.Less(e.EncounterLoot[0].Count, 99);
            Assert.IsFalse(ExplorationSystem.Move(s, data, 0));
            Assert.IsFalse(ExplorationSystem.Extract(s, data));
            Assert.IsFalse(ExplorationSystem.Melee(s, data));
            var codec = new NewtonsoftJsonCodec();
            s = codec.Deserialize<GameSave>(codec.Serialize(s));
            string before = codec.Serialize(s);
            ExplorationSystem.Tick(s, data, 30);
            Assert.AreEqual(before, codec.Serialize(s));
            string loot = codec.Serialize(s.Exploration.Loot);
            Assert.IsTrue(ExplorationSystem.ContinueEncounter(s));
            Assert.AreEqual(ExplorationPhase.Routes, s.Exploration.Phase);
            Assert.IsFalse(ExplorationSystem.ContinueEncounter(s));
            Assert.AreEqual(loot, codec.Serialize(s.Exploration.Loot));
        }

        [Test] public void SearchShowsReceiptAndNextEncounterClearsIt()
        {
            var s = Start(); var e = s.Exploration;
            e.Enemy = null; e.Phase = ExplorationPhase.Encounter;
            Assert.IsTrue(ExplorationSystem.Choose(s, data, EncounterChoice.Search));
            Assert.IsTrue(ExplorationSystem.ChooseLoot(s, 0));
            Assert.AreEqual(ExplorationPhase.EncounterResult, e.Phase);
            Assert.Greater(e.EncounterLoot.Count, 0);
            Assert.IsFalse(ExplorationSystem.Choose(s, data, EncounterChoice.Search));
            Assert.IsTrue(ExplorationSystem.ContinueEncounter(s));
            Assert.IsTrue(ExplorationSystem.Move(s, data, 0));
            Assert.AreEqual(0, e.EncounterLoot.Count);
        }

        [Test] public void StarterPreparationEquipsExactlyOnceEvenWithAFullWarehouse()
        {
            var real = JsonDataRegistry.Load(n => System.IO.File.ReadAllText(System.IO.Path.Combine("Assets/StreamingAssets/Data", n)));
            foreach (int capacity in new[] { 0, 60 }) {
                var save = new GameSave(); save.Warehouse.Capacity = capacity;
                Assert.IsTrue(ExplorationSystem.PrepareStarter(save, real));
                Assert.AreEqual("WPN04", PlayerEquipment.Equipped(save, "Weapon"));
                Assert.IsNull(ExplorationSystem.StartBlockReason(save, real, "YONGSAN_MARKET"));
                string before = new NewtonsoftJsonCodec().Serialize(save);
                Assert.IsFalse(ExplorationSystem.PrepareStarter(save, real));
                Assert.AreEqual(before, new NewtonsoftJsonCodec().Serialize(save));
                Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, "WPN04"));
            }
        }

        [Test] public void AlreadyClaimedStarterIsEquippedWithoutAnotherGrantAndOtherWeaponsStayEquipped()
        {
            var real = JsonDataRegistry.Load(n => System.IO.File.ReadAllText(System.IO.Path.Combine("Assets/StreamingAssets/Data", n)));
            var save = new GameSave();
            Assert.IsTrue(ExplorationSystem.ClaimStarterKit(save, real));
            Assert.IsTrue(ExplorationSystem.PrepareStarter(save, real));
            Assert.AreEqual("WPN04", PlayerEquipment.Equipped(save, "Weapon"));
            Assert.AreEqual(50, Warehouse.CountOf(save.Warehouse, "AMO05"));
            var equipped = new GameSave(); equipped.Player.Equipment["Weapon"] = "WPN01";
            Assert.IsTrue(ExplorationSystem.PrepareStarter(equipped, real));
            Assert.AreEqual("WPN01", PlayerEquipment.Equipped(equipped, "Weapon"));
            Assert.AreEqual(1, Warehouse.CountOf(equipped.Warehouse, "WPN04"));
            var stranded = new GameSave { ExplorationStarterClaimed = true };
            Assert.IsTrue(ExplorationSystem.PrepareStarter(stranded, real));
            Assert.AreEqual("WPN04", PlayerEquipment.Equipped(stranded, "Weapon"));
            Assert.IsFalse(ExplorationSystem.PrepareStarter(stranded, real));
        }

        [Test] public void StarterDialogueKeepsAllThreeEmployerVoicesDistinct()
        {
            var gifts = new System.Collections.Generic.HashSet<string>();
            var invitations = new System.Collections.Generic.HashSet<string>();
            var guidance = new System.Collections.Generic.HashSet<string>();
            foreach (var npc in new[] { "HWANG", "DR_CHOI", "YONGSAN_KIM" }) {
                gifts.Add(ExplorationDialogue.Gift(npc));
                invitations.Add(ExplorationDialogue.Invitation(npc));
                guidance.Add(ExplorationDialogue.Ready(npc));
            }
            Assert.AreEqual(3, gifts.Count); Assert.AreEqual(3, invitations.Count); Assert.AreEqual(3, guidance.Count);
        }
        FakeRegistry data;
        [SetUp]
        public void Setup()
        {
            data = FakeRegistry.Build();
        }

        GameSave Start()
        {
            var s = new GameSave();
            s.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
            s.Player.Equipment["Melee"] = "MEL01";
            Assert.IsTrue(ExplorationSystem.Start(s, data, "GURO_FACTORY"));
            return s;
        }

        [Test]
        public void ActualDataStarterCompletesIntermediateThenFinalExitAcrossSeeds()
        {
            var real = JsonDataRegistry.Load(name => System.IO.File.ReadAllText(System.IO.Path.Combine("Assets/StreamingAssets/Data", name)));
            for (uint seed = 1; seed <= 12; seed++)
            {
                var s = new GameSave{RngCounter = seed};
                s.Player.Level = 7;
                s.Player.Exp = 1234;
                Assert.IsTrue(ExplorationSystem.ClaimStarterKit(s, real));
                Assert.IsTrue(PlayerEquipment.TryEquip(s, real, "WPN04"));
                Assert.IsTrue(PlayerEquipment.TryEquip(s, real, "MEL01"));
                StartWithRemainingSupplies(s, real);
                CompleteUsingPlayerCommands(s, real, false, true);
                Assert.AreEqual(s.Exploration.IntermediateExitIndex, s.Exploration.NodeIndex, "seed " + seed);
                AssertSuccessfulSingleSettlement(s, real);
                Assert.IsTrue(ExplorationSystem.Acknowledge(s));
                Assert.IsTrue(ExplorationSystem.Rest(s));
                StartWithRemainingSupplies(s, real);
                CompleteUsingPlayerCommands(s, real, true, true);
                Assert.AreEqual(s.Exploration.NodeCount - 1, s.Exploration.NodeIndex, "seed " + seed);
                AssertSuccessfulSingleSettlement(s, real);
                Assert.AreEqual(7, s.Player.Level);
                Assert.AreEqual(1234, s.Player.Exp);
                Assert.AreEqual(1, s.Player.CharacterLevel);
                Assert.AreEqual(0, s.Player.CharacterExp);
                Assert.AreEqual("WPN04", PlayerEquipment.Equipped(s, "Weapon"));
                Assert.AreEqual("MEL01", PlayerEquipment.Equipped(s, "Melee"));
            }
        }

        static void StartWithRemainingSupplies(GameSave s, IDataRegistry real)
        {
            var supplies = new System.Collections.Generic.List<ItemStack>();
            foreach (var x in s.Warehouse.Stacks)
                if (ExplorationSystem.IsSupply(real, x.ItemId))
                    supplies.Add(x);
            Assert.IsTrue(ExplorationSystem.Start(s, real, "YONGSAN_MARKET", supplies));
        }

        static void CompleteUsingPlayerCommands(GameSave s, IDataRegistry real, bool finalExit, bool checkReload)
        {
            var codec = new NewtonsoftJsonCodec();
            string resumedResult = null;
            for (int step = 0; step < 20000 && ExplorationSystem.IsActive(s); step++)
            {
                var e = s.Exploration;
                if (checkReload && e.NodeIndex >= 1)
                {
                    var resumed = codec.Deserialize<GameSave>(codec.Serialize(s));
                    CompleteUsingPlayerCommands(resumed, real, finalExit, false);
                    resumedResult = codec.Serialize(resumed);
                    checkReload = false;
                }

                if (e.UseRemaining > 0)
                {
                    ExplorationSystem.Tick(s, real, .1);
                    continue;
                }

                bool safeToUse = e.Phase != ExplorationPhase.Combat || e.Enemy.Action == EnemyAction.Cover;
                if (safeToUse && ((s.Player.Hp < 65 && TryUseCarried(s, real, "MED05")) || (s.Player.Energy < 32 && TryUseCarried(s, real, "FOOD01")) || (s.Player.Hydration < 30 && TryUseCarried(s, real, "FOOD05"))))
                    continue;
                switch (e.Phase)
                {
                    case ExplorationPhase.LootChoice:
                        Assert.IsTrue(ExplorationSystem.ChooseLoot(s, 0));
                        break;
                    case ExplorationPhase.EncounterResult:
                        while (s.Exploration.PendingLoot.Count > 0) {
                            var pending = s.Exploration.PendingLoot[0];
                            Assert.IsTrue(ExplorationSystem.ResolvePendingLoot(s, ExplorationSystem.CanCarry(s.Exploration, pending.ItemId)));
                        }
                        Assert.IsTrue(ExplorationSystem.ContinueEncounter(s));
                        break;
                    case ExplorationPhase.Encounter:
                        Assert.IsTrue(ExplorationSystem.Choose(s, real, e.Enemy == null ? EncounterChoice.Search : EncounterChoice.Fight));
                        break;
                    case ExplorationPhase.Routes:
                        if (ExplorationSystem.CanExtract(e) && (!finalExit || e.NodeIndex == e.NodeCount - 1))
                            Assert.IsTrue(ExplorationSystem.Extract(s, real));
                        else
                            Assert.IsTrue(ExplorationSystem.Move(s, real, Math.Max(0, e.NodeIndex) % 2), "Movement stalled with energy " + s.Player.Energy);
                        break;
                    case ExplorationPhase.Combat:
                        if (e.Enemy.Action == EnemyAction.Aiming && e.Enemy.Remaining < 1.2 && e.CoverCooldown <= 0)
                        {
                            Assert.IsTrue(ExplorationSystem.Cover(s));
                            var duringCover = codec.Deserialize<GameSave>(codec.Serialize(s));
                            ExplorationSystem.Tick(s, real, .1);
                            ExplorationSystem.Tick(duringCover, real, .1);
                            Assert.AreEqual(codec.Serialize(s), codec.Serialize(duringCover), "Active cover timing changed after reload");
                        }

                        if (e.AttackCooldown <= 0)
                        {
                            if (e.Enemy.Action == EnemyAction.Cover || ExplorationSystem.AmmoRemaining(s) == 0)
                                Assert.IsTrue(ExplorationSystem.Melee(s, real));
                            else
                                Assert.IsTrue(ExplorationSystem.Attack(s, real, FireMode.Single));
                        }

                        ExplorationSystem.Tick(s, real, .1);
                        break;
                }
            }

            Assert.IsNotNull(s.Exploration.Result, "Player command simulation timed out");
            Assert.AreEqual(ExplorationOutcome.Success, s.Exploration.Result.Outcome, "Run " + s.Exploration.Uid);
            if (resumedResult != null)
                Assert.AreEqual(resumedResult, codec.Serialize(s), "Saved midway run diverged after reload");
        }

        static bool TryUseCarried(GameSave s, IDataRegistry real, string id)
        {
            foreach (var x in s.Exploration.Supplies)
                if (x.ItemId == id && x.Count > 0)
                    return ExplorationSystem.Use(s, real, id);
            foreach (var x in s.Exploration.Loot)
                if (x.ItemId == id && x.Count > 0)
                    return ExplorationSystem.Use(s, real, id);
            return false;
        }

        static void AssertSuccessfulSingleSettlement(GameSave s, IDataRegistry real)
        {
            Assert.IsTrue(s.Exploration.Result.Settled);
            Assert.Greater(s.Exploration.Result.Items.Count, 0);
            Assert.AreEqual(0, s.Exploration.Loot.Count);
            foreach (var item in s.Exploration.Result.Items)
                Assert.GreaterOrEqual(Warehouse.CountOf(s.Warehouse, item.ItemId), item.Count, "Extracted loot was not stored");
            var codec = new NewtonsoftJsonCodec();
            string before = codec.Serialize(s);
            Assert.IsFalse(ExplorationSystem.Extract(s, real));
            Assert.IsFalse(ExplorationSystem.EmergencyReturn(s, real));
            ExplorationSystem.Tick(s, real, 30);
            Assert.AreEqual(before, codec.Serialize(s));
        }

        [Test]
        public void GunConsumesCarriedAndFoundMatchingAmmo()
        {
            var s = Start();
            s.Player.Equipment["Weapon"] = "WPN01";
            s.Exploration.Phase = ExplorationPhase.Combat;
            s.Exploration.Enemy = new ExplorationEnemy{Hp = 1000};
            s.Exploration.Supplies.Add(new ItemStack("AMO01", 2));
            s.Exploration.Loot.Add(new ItemStack("AMO02", 3));
            Assert.IsTrue(ExplorationSystem.Attack(s, data, FireMode.Auto));
            Assert.AreEqual(0, ExplorationSystem.AmmoRemaining(s));
            Assert.AreEqual(5, s.Exploration.ShotsSinceReload);
        }

        [Test]
        public void TimedCoverReducesIncomingDamage()
        {
            var a = Start();
            a.Exploration.Phase = ExplorationPhase.Combat;
            a.Exploration.Enemy = new ExplorationEnemy{Kind = "PMC", Hp = 100, Action = EnemyAction.Aiming, Remaining = .1};
            var codec = new NewtonsoftJsonCodec();
            var b = codec.Deserialize<GameSave>(codec.Serialize(a));
            Assert.IsTrue(ExplorationSystem.Cover(a));
            ExplorationSystem.Tick(a, data, .2);
            ExplorationSystem.Tick(b, data, .2);
            Assert.Greater(a.Player.Hp, b.Player.Hp);
            Assert.IsFalse(ExplorationSystem.Cover(a));
        }

        [Test]
        public void ActualStarterKitCanOnlyBeClaimedOnce()
        {
            var real = JsonDataRegistry.Load(name => System.IO.File.ReadAllText(System.IO.Path.Combine("Assets/StreamingAssets/Data", name)));
            var s = new GameSave();
            Assert.IsTrue(ExplorationSystem.ClaimStarterKit(s, real));
            Assert.IsFalse(ExplorationSystem.ClaimStarterKit(s, real));
            Assert.AreEqual(1, Warehouse.CountOf(s.Warehouse, "WPN04"));
        }

        [Test]
        public void LengthAndExitsAreSaved()
        {
            for (uint seed = 1; seed < 80; seed++)
            {
                var s = new GameSave{RngCounter = seed};
                s.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
                s.Player.Equipment["Melee"] = "MEL01";
                Assert.IsTrue(ExplorationSystem.Start(s, data, "GURO_FACTORY"));
                var e = s.Exploration;
                Assert.That(e.NodeCount, Is.InRange(6, 10));
                Assert.That(e.IntermediateExitIndex, Is.InRange(2, e.NodeCount - 2));
                Assert.AreEqual(2, e.Routes.Length);
                var copy = new NewtonsoftJsonCodec().Deserialize<GameSave>(new NewtonsoftJsonCodec().Serialize(s));
                Assert.AreEqual(e.RngState, copy.Exploration.RngState);
                Assert.AreEqual(e.EncounterKind, copy.Exploration.EncounterKind);
            }
        }

        [Test]
        public void ZeroEnergyBlocksMoveButExitNeedsNoResources()
        {
            var s = Start();
            s.Exploration.Phase = ExplorationPhase.Routes;
            s.Exploration.AwaitingEntryChoice = false;
                s.Exploration.NodeIndex = s.Exploration.IntermediateExitIndex;
            s.Player.Energy = 0;
            Assert.IsFalse(ExplorationSystem.Move(s, data, 0));
            Assert.IsTrue(ExplorationSystem.Extract(s, data));
            Assert.AreEqual(ExplorationOutcome.Success, s.Exploration.Result.Outcome);
        }

        [Test]
        public void SettlementAndOverflowAreExactlyOnce()
        {
            var s = Start();
            s.Warehouse.Capacity = 0;
            s.Exploration.Loot.Add(new ItemStack("JUNK03", 2));
            s.Exploration.Phase = ExplorationPhase.Routes;
            s.Exploration.AwaitingEntryChoice = false;
            s.Exploration.NodeIndex = s.Exploration.NodeCount - 1;
            Assert.IsTrue(ExplorationSystem.Extract(s, data));
            Assert.IsFalse(ExplorationSystem.Extract(s, data));
            Assert.AreEqual(2, s.ExplorationOverflow[0].Count);
            s.Warehouse.Capacity = 60;
            Assert.AreEqual(2, ExplorationSystem.ClaimOverflow(s, data));
            Assert.AreEqual(0, ExplorationSystem.ClaimOverflow(s, data));
        }

        [Test]
        public void PausedCombatDoesNotAdvance()
        {
            var s = Start();
            s.Exploration.Phase = ExplorationPhase.Combat;
            s.Exploration.Enemy = new ExplorationEnemy{Hp = 80, Remaining = 1, Action = EnemyAction.Aiming};
            s.Exploration.Paused = true;
            ExplorationSystem.Tick(s, data, 30);
            Assert.AreEqual(1, s.Exploration.Enemy.Remaining);
            Assert.AreEqual(100, s.Player.Hp);
        }

        [Test]
        public void ExhaustionIsNotDeathAndDoesNotAwardCharacterExp()
        {
            var s = Start();
            s.Player.Hydration = 0;
            s.Player.Energy = 0;
            ExplorationSystem.Tick(s, data, .1);
            Assert.AreEqual(ExplorationOutcome.Exhausted, s.Exploration.Result.Outcome);
            Assert.AreEqual(0, s.Player.CharacterExp);
            Assert.AreEqual(1, s.Player.Level);
        }

        [Test]
        public void EquipmentTransfersAndSeparatesMelee()
        {
            var s = new GameSave();
            Warehouse.TryAdd(s.Warehouse, data, "MEL01", 1);
            Warehouse.TryAdd(s.Warehouse, data, "WPN01", 1);
            Assert.IsTrue(PlayerEquipment.TryEquip(s, data, "MEL01"));
            Assert.IsTrue(PlayerEquipment.TryEquip(s, data, "WPN01"));
            Assert.AreEqual("MEL01", PlayerEquipment.Equipped(s, "Melee"));
            Assert.AreEqual("WPN01", PlayerEquipment.Equipped(s, "Weapon"));
            Assert.AreEqual(0, Warehouse.CountOf(s.Warehouse, "MEL01"));
        }

        [Test]
        public void OldPlayerSaveDefaultsAreSafe()
        {
            var s = new NewtonsoftJsonCodec().Deserialize<GameSave>("{\"Player\":{\"Level\":7,\"Exp\":400}}");
            Assert.AreEqual(100, s.Player.Hp);
            Assert.AreEqual(1, s.Player.CharacterLevel);
            Assert.AreEqual(7, s.Player.Level);
        }

        [Test]
        public void DispatchBlocksBothDirections()
        {
            var s = new GameSave();
            s.Player.Money = 10000000;
            s.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
            s.Player.Equipment["Melee"] = "MEL01";
            var scav = new ScavState{Uid = "valid_scav", Name = "대기 인원", Status = ScavStatus.Idle};
            scav.Equipment["Weapon"] = "WPN01";
            s.Scavs.Add(scav);
            var team = new[]{scav.Uid};
            Assert.IsNull(AfterSeoul.Expedition.ExpeditionSystem.DepartBlockReason(s, data, "GURO_FACTORY", team));
            Assert.IsTrue(ExplorationSystem.Start(s, data, "GURO_FACTORY"));
            Assert.IsNotNull(AfterSeoul.Expedition.ExpeditionSystem.DepartBlockReason(s, data, "GURO_FACTORY", team));
            Assert.IsNull(AfterSeoul.Expedition.ExpeditionSystem.DepartBlockReason(s, data, "MYEONGDONG", team));
            var s2 = new GameSave();
            s2.SurvivedExplorationMapIds.AddRange(ExplorationSystem.MainRoute);
            s2.Player.Equipment["Melee"] = "MEL01";
            Assert.IsNull(ExplorationSystem.StartBlockReason(s2, data, "GURO_FACTORY"));
            s2.Expeditions.Add(new ExpeditionState{MapId = "GURO_FACTORY"});
            Assert.IsFalse(ExplorationSystem.Start(s2, data, "GURO_FACTORY"));
            Assert.IsNull(ExplorationSystem.StartBlockReason(s2, data, "MYEONGDONG"));
        }

        [Test]
        public void DeathRecordsKillerAndLosesOnlyLoot()
        {
            var s = Start();
            s.Player.Hp = 1;
            s.Exploration.Phase = ExplorationPhase.Combat;
            s.Exploration.Enemy = new ExplorationEnemy{Name = "killer", Kind = "PMC", WeaponId = "WPN01", Hp = 80, Action = EnemyAction.Aiming, Remaining = .01};
            s.Exploration.Loot.Add(new ItemStack("JUNK03", 2));
            s.Exploration.Supplies.Add(new ItemStack("FOOD01", 1));
            ExplorationSystem.Tick(s, data, 30);
            Assert.AreEqual(ExplorationOutcome.Death, s.Exploration.Result.Outcome);
            Assert.AreEqual("killer", s.Exploration.Result.KillerName);
            Assert.AreEqual(2, s.Exploration.Result.LostLoot[0].Count);
            Assert.AreEqual(1, Warehouse.CountOf(s.Warehouse, "FOOD01"));
            Assert.AreEqual(0, Warehouse.CountOf(s.Warehouse, "JUNK03"));
            Assert.Greater(s.Player.Hp, 0);
        }

        [Test]
        public void ReloadPreservesCombatDecisions()
        {
            var s = Start();
            s.Exploration.Phase = ExplorationPhase.Combat;
            s.Exploration.Enemy = new ExplorationEnemy{Kind = "Scav", Hp = 80, Action = EnemyAction.Aiming, Remaining = .5};
            var codec = new NewtonsoftJsonCodec();
            var copy = codec.Deserialize<GameSave>(codec.Serialize(s));
            ExplorationSystem.Tick(s, data, 5);
            ExplorationSystem.Tick(copy, data, 5);
            Assert.AreEqual(codec.Serialize(s), codec.Serialize(copy));
        }

        [Test]
        public void UnsupportedInputDoesNotMutate()
        {
            var s = Start();
            string before = new NewtonsoftJsonCodec().Serialize(s);
            Assert.IsFalse(ExplorationSystem.Move(s, data, 99));
            Assert.IsFalse(ExplorationSystem.Attack(s, data, FireMode.Auto));
            Assert.AreEqual(before, new NewtonsoftJsonCodec().Serialize(s));
        }

        [Test]
        public void FoodPreservesMainlineRestoreEffects()
        {
            var s = new GameSave();
            s.Player.Energy = 20;
            s.Player.Hydration = 30;
            Warehouse.TryAdd(s.Warehouse, data, "FOOD01", 1);
            Assert.IsTrue(ExplorationSystem.Use(s, data, "FOOD01"));
            Assert.AreEqual(70, s.Player.Energy);
            Assert.AreEqual(25, s.Player.Hydration);
        }

        [Test]
        public void ProfilesRestrictModesByIdentity()
        {
            CollectionAssert.DoesNotContain(CombatProfiles.For("WPN03").Modes, FireMode.Auto);
            CollectionAssert.Contains(CombatProfiles.For("WPN01").Modes, FireMode.Auto);
            Assert.IsNull(CombatProfiles.For("MEL01"));
        }
    }
}
