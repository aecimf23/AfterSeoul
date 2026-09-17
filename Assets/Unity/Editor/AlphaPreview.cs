using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Unity.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AfterSeoul.Unity.Editor
{
    /// <summary>Renders actual uGUI with an isolated in-memory save. Never opens the player's save.</summary>
    public static class AlphaPreview
    {
        private static AppShell _shell;
        private static GameSession _session;
        private static Camera _camera;
        private static RenderTexture _target;
        private static int _step, _wait;
        private static string[] Names = { "00-employer", "01-home", "02-factory", "03-expedition", "04-personnel", "05-warehouse", "06-audio" };
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static bool _themes, _hadTheme;
        private static string _originalTheme;
        private static string _directory = "Logs/alpha-preview";
        private static bool _growth;
        private static TestClock _growthClock;
        private static bool _interaction;
        private static bool _simple;
        private static bool _direct;
        private static bool _scavenge;
        private static bool _itemArtwork;
        private static bool _expandedArtwork;
        private static bool _regional;
        private static bool _mapNavigation;
        public static void CaptureMapNavigation()
        {
            _mapNavigation = _direct = true;
            _directory = "Logs/map-navigation-preview";
            Names = new[] { "00-home", "01-direct-map", "02-map-detail", "03-safe-entry", "04-dispatch-map", "05-dispatch-detail", "06-all-regions" };
            Capture();
        }
        private static void AdvanceMapNavigation()
        {
            if (_step == 1) {
                _session.Save.SurvivedExplorationMapIds.Clear();
                _session.Save.ExplorationTutorialSeen = 15;
                AfterSeoul.Exploration.ExplorationSystem.PrepareStarter(_session.Save, _session.Data);
                _shell.OpenExploration();
            }
            else if (_step == 2) ClickPreview("Explore_YONGSAN_MARKET");
            else if (_step == 3) ClickPreview("EnterSelectedMap");
            else if (_step == 4) {
                typeof(ExplorationView).GetMethod("Close", Private).Invoke(DirectView, null);
                _session.Save.Exploration = null;
                _shell.Select(2);
            }
            else if (_step == 5) ClickPreview("MapSelect_YONGSAN_MARKET");
            else {
                ClickPreview("Close");
                _session.Save.SurvivedExplorationMapIds.AddRange(new[] { "YONGSAN_MARKET", "GURO_FACTORY", "HAN_RIVER", "NAMSAN_WOODS", "GANGNAM_STREETS", "YONGSAN_BASE" });
                _shell.AfterAction();
            }
        }
        public static void CaptureRegionalContacts()
        {
            _regional = _direct = true;
            _directory = "Logs/regional-contacts-preview";
            Names = new[] { "00-home", "01-introduction", "02-dongdaemun", "03-accepted", "04-dokkaebi", "05-wildman", "06-liaison", "07-broker", "08-victory-routes" };
            Capture();
        }
        private static void AdvanceRegional()
        {
            if (_step == 1) {
                var save = _session.Save;
                save.ExplorationTutorialSeen = 15;
                AfterSeoul.Exploration.ExplorationSystem.PrepareStarter(save, _session.Data);
                save.SurvivedExplorationMapIds.AddRange(new[] { "YONGSAN_MARKET", "GURO_FACTORY", "HAN_RIVER", "NAMSAN_WOODS", "GANGNAM_STREETS", "YONGSAN_BASE" });
                _shell.OpenExploration();
                typeof(ExplorationView).GetMethod("ShowRegionalIntroduction", Private).Invoke(DirectView, new object[] { "GURO_FACTORY" });
            }
            else if (_step == 2) ClickPreview("MeetRegionalNpc");
            else if (_step == 3) ClickPreview("AcceptRegionalQuest");
            else if (_step <= 7) {
                typeof(ExplorationView).GetMethod("CloseModal", Private).Invoke(DirectView, null);
                var map = new[] { "HAN_RIVER", "NAMSAN_WOODS", "YONGSAN_BASE", "MYEONGDONG" }[_step - 4];
                typeof(ExplorationView).GetMethod("ShowRegionalQuest", Private).Invoke(DirectView, new object[] { map, false });
            }
            else {
                typeof(ExplorationView).GetMethod("CloseModal", Private).Invoke(DirectView, null);
                _session.Save.Exploration = new AfterSeoul.Exploration.ExplorationState {
                    MapId = "YONGSAN_MARKET", Location = "1층 전자 매장", NodeCount = 8, NodeIndex = 2, IntermediateExitIndex = 4, Phase = AfterSeoul.Exploration.ExplorationPhase.Routes, Detected = true,
                    Enemy = new AfterSeoul.Exploration.ExplorationEnemy { Hp = 0, Kind = "PMC", Action = AfterSeoul.Exploration.EnemyAction.Aiming },
                    Routes = new[] { "매장 안쪽으로 들어간다", "주차장으로 간다" }
                };
                RenderDirect();
            }
        }
        public static void CaptureExpandedItemArtwork()
        {
            _expandedArtwork = _itemArtwork = _direct = true;
            _directory = "Logs/expanded-item-artwork-preview";
            Names = new[] { "00-warehouse", "01-ak-detail", "02-carbine-detail", "03-equipment-choice", "04-equipment-loot" };
            Capture();
        }
        public static void CaptureItemArtwork()
        {
            _itemArtwork = _direct = true;
            _directory = "Logs/item-artwork-preview";
            Names = new[] { "00-warehouse", "01-bandage-detail", "02-pistol-detail", "03-loot-choice", "04-loot-receipt" };
            Capture();
        }
        private static void AdvanceItemArtwork()
        {
            if (_step == 1) ClickPreview(_expandedArtwork ? "Item_WPN01" : "Item_MED16");
            else if (_step == 2) { ClickPreview("Close"); ClickPreview(_expandedArtwork ? "Item_WPN02" : "Item_WPN04"); }
            else if (_step == 3) {
                ClickPreview("Close"); _session.Save.ExplorationStarterPrepared = true;
                _session.Save.ExplorationTutorialSeen = 15;
                _session.Save.Exploration = new AfterSeoul.Exploration.ExplorationState {
                    Phase = AfterSeoul.Exploration.ExplorationPhase.LootChoice, MapId = "YONGSAN_MARKET", ContainerKind = _expandedArtwork ? "Weapon" : "Medical",
                    Location = "1층 전자 매장", LootOptions = _expandedArtwork ?
                        new List<ItemStack> { new ItemStack("AMR01", 1), new ItemStack("HDW01", 1) } :
                        new List<ItemStack> { new ItemStack("MED16", 2), new ItemStack("MED05", 1) }
                };
                _shell.OpenExploration();
            }
            else if (_step == 4) ClickPreview("LootChoice_0");
        }
        public static void CaptureScavengePrologue()
        {
            _scavenge = true;
            _directory = "Logs/scavenge-prologue-preview";
            Names = new[] { "00-seoul", "01-resident", "02-supplies", "03-seek-help", "04-people", "05-first-quest", "06-pistol", "07-preparation", "08-medical-box", "09-loot-choice", "10-loot-receipt", "11-routes", "12-extraction", "13-quest-report" };
            Capture();
        }

        private static void AdvanceScavenge()
        {
            if (_step <= 4) ClickPreview("Next");
            else if (_step == 5) ClickPreview("E_DR_CHOI");
            else if (_step == 6) ClickPreview("AcceptFirstQuest");
            else if (_step == 7) { _session.Save.ExplorationTutorialSeen = 15; ClickPreview("ReceiveStarterPistol"); }
            else if (_step == 8) { ClickPreview("Explore_YONGSAN_MARKET"); ClickPreview("EnterSelectedMap"); ClickPreview("Route0"); }
            else if (_step == 9) ClickPreview("EncounterPrimary");
            else if (_step == 10) ClickPreview("LootChoice_1");
            else if (_step == 11) ClickPreview("ContinueEncounter");
            else if (_step == 12) {
                _session.Save.Exploration.NodeIndex = _session.Save.Exploration.IntermediateExitIndex;
                AfterSeoul.Exploration.ExplorationSystem.Extract(_session.Save, _session.Data); RenderDirect();
            }
            else if (_step == 13) {
                AfterSeoul.Exploration.ExplorationSystem.Acknowledge(_session.Save);
                typeof(ExplorationView).GetMethod("Close", Private).Invoke(DirectView, null);
                _shell.OpenExploration();
            }
        }
        public static void CaptureDirectExploration()
        {
            _direct = true;
            _directory = "Logs/direct-exploration-preview";
            Names = new[] { "00-home", "01-preparation", "02-first-guide", "03-encounter", "04-combat-rain", "05-exit-fog", "06-success", "07-death", "08-rescue", "09-warehouse", "10-item-detail" };
            Capture();
        }

        private static ExplorationView DirectView => _shell.GetComponentInChildren<ExplorationView>(true);
        private static void RenderDirect() => typeof(ExplorationView).GetMethod("Render", Private).Invoke(DirectView, null);
        private static void AdvanceDirect()
        {
            var save = _session.Save;
            if (_step == 1) {
                AfterSeoul.Exploration.ExplorationSystem.ClaimStarterKit(save, _session.Data);
                AfterSeoul.Exploration.PlayerEquipment.TryEquip(save, _session.Data, "WPN04");
                AfterSeoul.Exploration.PlayerEquipment.TryEquip(save, _session.Data, "MEL01");
                save.ExplorationTutorialSeen = 15;
                _shell.OpenExploration();
            }
            else if (_step == 2) {
                ClickPreview("ReceiveStarterPistol");
                save.ExplorationTutorialSeen = 0; RenderDirect();
            }
            else if (_step == 3) {
                ClickPreview("ExplorationGuideContinue"); save.ExplorationTutorialSeen = 15;
                save.NpcTrust["HWANG"] = 20;
                AfterSeoul.Exploration.ExplorationSystem.Start(save, _session.Data, "YONGSAN_MARKET", new[] { new ItemStack("AMO05", 40), new ItemStack("MED05", 2) });
                save.Exploration.Phase = AfterSeoul.Exploration.ExplorationPhase.Encounter;
                save.Exploration.EncounterKind = "PMC"; save.Exploration.Detected = false;
                save.Exploration.Location = "1층 전자 매장";
                save.Exploration.Enemy = new AfterSeoul.Exploration.ExplorationEnemy { Name = "정찰 중인 PMC", Kind = "PMC", WeaponId = "WPN01" };
                RenderDirect();
            }
            else if (_step == 4) {
                AfterSeoul.Exploration.ExplorationSystem.Choose(save, _session.Data, AfterSeoul.Exploration.EncounterChoice.Fight);
                save.Exploration.Weather = AfterSeoul.Exploration.ExplorationWeather.Rain;
                save.Exploration.Enemy.Action = AfterSeoul.Exploration.EnemyAction.Aiming;
                RenderDirect();
            }
            else if (_step == 5) {
                save.Exploration.Phase = AfterSeoul.Exploration.ExplorationPhase.Routes;
                save.Exploration.NodeIndex = save.Exploration.IntermediateExitIndex;
                save.Exploration.Weather = AfterSeoul.Exploration.ExplorationWeather.Fog;
                save.Exploration.Location = "지하 주차장";
                save.Exploration.Loot.Add(new ItemStack("JUNK03", 3));
                save.Exploration.Loot.Add(new ItemStack("MED01", 1)); RenderDirect();
            }
            else if (_step == 6) { AfterSeoul.Exploration.ExplorationSystem.Extract(save, _session.Data); RenderDirect(); }
            else if (_step == 7) {
                save.Exploration.Result.Outcome = AfterSeoul.Exploration.ExplorationOutcome.Death;
                save.Exploration.Result.KillerName = "정찰 중인 PMC"; save.Exploration.Result.KillerKind = "PMC"; save.Exploration.Result.KillerWeaponId = "WPN01";
                save.Exploration.Result.LostLoot = new List<ItemStack>(save.Exploration.Result.Items); RenderDirect();
            }
            else if (_step == 8) ClickPreview("ReturnToBase");
            else if (_step == 9) { ClickPreview("RescueContinue"); _shell.Select(4); }
            else if (_step == 10) ClickPreview("Item_MED05");
        }
        public static void CaptureSimpleInteraction()
        {
            _interaction = _simple = true;
            _directory = "Logs/simple-interaction-preview";
            Names = new[] { "00-home", "01-signal-guide", "02-signal-play", "03-reward-choice",
                "04-vault-guide", "05-vault-play", "06-number-clue", "07-collect", "08-complete" };
            Capture();
        }
        public static void CaptureInteraction()
        {
            _interaction = true;
            _directory = "Logs/interaction-preview";
            Names = new[] { "00-home", "01-playing", "02-upgraded", "03-vault", "04-complete", "05-npc" };
            Capture();
        }

        private static object FactoryScreen => ((System.Collections.IList)typeof(AppShell)
            .GetField("_screens", Private).GetValue(_shell))[1];

        private static void FactoryCall(string name, params object[] arguments) =>
            FactoryScreen.GetType().GetMethod(name, Private).Invoke(FactoryScreen, arguments);

        private static void AdvanceInteraction()
        {
            if (_simple) { AdvanceSimpleInteraction(); return; }
            if (_step == 1) {
                _shell.SelectByName("공장");
                FactoryCall("OnPick", "RCP_SALVAGE");
                FactoryCall("OnPress");
                ClickPreview("StartMinigame");
            }
            else if (_step == 2) {
                var game = FactoryScreen.GetType().GetField("_game", Private).GetValue(FactoryScreen);
                long money = _session.Save.Player.Money;
                long cost = AfterSeoul.Factory.Station.UpgradeCost(_session.Save, _session.Data);
                ClickPreview("QuickUpgrade");
                if (!ReferenceEquals(game, FactoryScreen.GetType().GetField("_game", Private).GetValue(FactoryScreen)) ||
                    _session.Save.Factory.StationLevel != 2 || _session.Save.Player.Money != money - cost)
                    throw new InvalidOperationException("Upgrade interrupted gameplay or charged an incorrect amount");
            }
            else if (_step == 3) {
                FactoryCall("OnCancel"); FactoryCall("OnPick", "RCP_VAULT"); FactoryCall("OnPress");
                ClickPreview("StartMinigame");
            }
            else if (_step == 4) {
                FactoryCall("OnCancel");
                FactoryCall("ShowCompletion", new AfterSeoul.Factory.WorkStepResult {
                    Completed = true, Quality = CraftQuality.Good,
                    Output = new ItemStack { ItemId = _session.Data.GetRecipe("RCP_SALVAGE").OutputItemId, Count = 4 }
                });
            }
            else if (_step == 5) {
                _shell.SelectByName("기지");
                typeof(AppShell).GetMethod("ShowStepPrompt", Private).Invoke(_shell, new object[] { "pick_work" });
            }
        }

        private static void AdvanceSimpleInteraction()
        {
            if (_step == 1) {
                _shell.SelectByName("공장"); FactoryCall("OnPick", "RCP_SALVAGE"); FactoryCall("OnPress");
            }
            else if (_step == 2 || _step == 5) ClickPreview("StartMinigame");
            else if (_step == 3) {
                var game = (Minigame)FactoryScreen.GetType().GetField("_game", Private).GetValue(FactoryScreen);
                var signal = (DeliverySignal)game.GetType().GetField("_signal", Private).GetValue(game);
                for (int i = 0; i < 1000 && !signal.Choosing; i++) {
                    if (signal.Cursor < signal.Target) game.Press(); else game.Release();
                    game.Tick(.02f);
                }
                game.Release();
                if (!signal.Choosing) throw new InvalidOperationException("Signal never offered a reward choice");
                ((ScreenBase)FactoryScreen).Tick(0);
                foreach (var button in _shell.GetComponentsInChildren<Button>())
                    if (button.name == "Action" && button.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("Movement button must hide during reward choice");
            }
            else if (_step == 4) {
                FactoryCall("OnCancel"); FactoryCall("OnPick", "RCP_VAULT"); FactoryCall("OnPress");
            }
            else if (_step == 6) ClickPreview("Sector0");
            else if (_step == 7) { ClickPreview("Scan"); ClickPreview("Scan"); }
            else if (_step == 8) {
                ClickPreview("Extract"); ((ScreenBase)FactoryScreen).Tick(1.2f);
                if (!_session.Save.Factory.Workbench.IsIdle) throw new InvalidOperationException("Vault did not complete");
            }
        }
        public static void CaptureGrowth()
        {
            _growth = true;
            _directory = "Logs/growth-preview";
            Names = new[] { "00-first-mission", "01-departure", "02-outbound", "03-return",
                "04-delivery", "05-equipment", "06-regular-departure", "07-quest", "08-region" };
            Capture();
        }
        private static void ClickPreview(string name)
        {
            foreach (var button in _shell.GetComponentsInChildren<Button>())
                if (button.name == name && button.gameObject.activeInHierarchy && button.interactable)
                { button.onClick.Invoke(); return; }
            throw new InvalidOperationException("Missing actionable button: " + name);
        }
        private static void AdvanceGrowth()
        {
            var scav = _session.Save.Scavs[0];
            if (_step == 1) {
                _shell.SelectByName("탐색");
                ClickPreview("Pick_" + scav.Uid);
            }
            else if (_step == 2) { ClickPreview("OrientationDepart"); _shell.SelectByName("기지"); }
            else if (_step == 3) { _growthClock.Advance(TimeSpan.FromMinutes(3)); _session.Tick(); }
            else if (_step == 4) {
                var cutscene = typeof(AppShell).GetField("_cutscene", Private).GetValue(_shell);
                if (cutscene != null) typeof(ReturnCutscene).GetMethod("Close", Private).Invoke(cutscene, null);
                _shell.AfterAction();
            }
            else if (_step == 5) {
                ClickPreview("OrientationNext");
                if (_session.Save.Orientation.Stage != OrientationStage.Completed)
                    throw new InvalidOperationException("Delivery button did not complete orientation");
            }
            else if (_step == 6) {
                var goal = GrowthGuide.Current(_session.Save, _session.Data);
                if (!_session.Buy(goal.ItemId) || !_session.Equip(scav.Uid, goal.ItemId))
                    throw new InvalidOperationException("Suggested equipment unavailable");
                _shell.AfterAction();
            }
            else if (_step == 7) {
                var exp = _session.Depart("MYEONGDONG", new[] { scav.Uid });
                exp.Resolved = true; scav.Status = ScavStatus.Idle;
                var pool = _session.Data.GetQuestPool(Employers.QuestPoolId(_session.Data, "HWANG"));
                var q = pool[0];
                _session.Save.Quests.Active.Clear();
                _session.Save.Quests.Active.Add(new ActiveQuest { QuestId = q.Id });
                foreach (var req in q.Requires) {
                    string id = req.ItemId;
                    if (string.IsNullOrEmpty(id))
                        foreach (var item in _session.Data.AllItems)
                            if (Array.IndexOf(item.Tags, req.Tag) >= 0) { id = item.Id; break; }
                    _session.Save.Warehouse.Stacks.Add(new ItemStack { ItemId = id, Count = req.Count });
                }
                _shell.AfterAction();
            }
            else if (_step == 8) {
                ClickPreview("GrowthNext");
                _session.Save.Player.Level = 5;
                _session.Save.NpcTrust["HWANG"] = 10;
                _shell.AfterAction();
            }
        }

        public static void CaptureThemes()
        {
            _themes = true;
            _directory = "Logs/theme-preview";
            Names = new[] { "00-night-home", "01-night-settings", "02-military-home", "03-military-settings", "04-shelter-home", "05-shelter-settings" };
            _hadTheme = PlayerPrefs.HasKey("AfterSeoul.UI.Theme");
            _originalTheme = PlayerPrefs.GetString("AfterSeoul.UI.Theme");
            Theme.Select("night");
            Capture();
        }
        private static void RestoreTheme()
        {
            if (!_themes) return;
            if (_hadTheme) PlayerPrefs.SetString("AfterSeoul.UI.Theme", _originalTheme);
            else PlayerPrefs.DeleteKey("AfterSeoul.UI.Theme");
            PlayerPrefs.Save();
            Theme.Reload();
        }
        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("PreviewEventSystem").AddComponent<EventSystem>();
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            var locale = Resources.Load<TextAsset>("Locales/ko");
            var mobile = Resources.Load<TextAsset>("Locales/mobile/ko");
            Loc.Load("ko", locale.text, locale.text, mobile != null ? mobile.text : null);
            var clock = new TestClock(new DateTimeOffset(2026, 9, 15, 1, 0, 0, TimeSpan.Zero));
            _growthClock = clock;
            _session = new GameSession(new SaveService(new MemoryFiles(), new NewtonsoftJsonCodec(), clock), data, clock);
            _session.Boot();
            if (!_scavenge) _session.Save.WelcomePage = -1;
            var host = new GameObject("PreviewShell");
            host.SetActive(false);
            _shell = host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady", Private).Invoke(_shell, new object[] { _session });
            typeof(AppShell).GetMethod("TickClock", Private).Invoke(_shell, null);
            _shell.enabled = false;
            host.SetActive(true);
            var canvas = host.GetComponentInChildren<Canvas>();
            _camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Theme.Bg;
            _camera.orthographic = true;
            _target = new RenderTexture(1080, 1920, 24);
            _camera.targetTexture = _target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _camera;
            canvas.planeDistance = 10;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            canvas.scaleFactor = 1;
            Directory.CreateDirectory(_directory);
            if (_themes || _growth || _interaction || _direct)
            {
                _session.Save.FirstExplorationQuest.Completed = true;
                _session.ChooseEmployer("HWANG");
                UnityEngine.Object.DestroyImmediate(_shell.transform.Find("Canvas/EmployerHost").gameObject);
                typeof(AppShell).GetMethod("OnEmployerChosen", Private).Invoke(_shell, null);
                if (DirectView != null) typeof(ExplorationView).GetMethod("Close", Private).Invoke(DirectView, null);
            }
            if (_growth) {
                _session.Save.Player.Money = 500000;
                _session.Hire(_session.Save.Market.Offers[0].OfferId);
                _shell.AfterAction();
            }
            if (_interaction) { _session.Save.Player.Money = 500000; _shell.AfterAction(); }
            if (_itemArtwork) {
                if (_expandedArtwork) Warehouse.TryAdd(_session.Save.Warehouse, _session.Data, "WPN02", 1);
                foreach (string id in new[] { "WPN04", "WPN01", "MEL01", "AMO05", "MED05", "MED16", "MED04", "FOOD01", "FOOD05", "FOOD02", "JUNK29", "JUNK03", "JUNK23", "JUNK21", "JUNK20", "JUNK16", "JUNK39", "JUNK18" })
                    Warehouse.TryAdd(_session.Save.Warehouse, _session.Data, id, 2);
                _shell.Select(4); _shell.AfterAction();
            }
            _step = 0;
            _wait = 0;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (_wait++ < 12) { Canvas.ForceUpdateCanvases(); Tween.Tick(.1f); return; }
                Canvas.ForceUpdateCanvases();
                if (_interaction && _step >= 1 && _step <= 4)
                {
                    var screen = (RectTransform)typeof(AppShell).GetField("_screenHost", Private).GetValue(_shell);
                    foreach (var button in _shell.GetComponentsInChildren<Button>())
                        if (button.name == "QuickUpgrade" && button.gameObject.activeInHierarchy) {
                            var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                            foreach (var corner in corners)
                                if (!screen.rect.Contains((Vector2)screen.InverseTransformPoint(corner)))
                                    throw new InvalidOperationException("Upgrade button escaped the visible gameplay screen");
                        }
                }
                if (_growth) foreach (var label in _shell.GetComponentsInChildren<Text>()) {
                    if (label.name != "Brief" && label.name != "GrowthHint" && label.name != "GrowthRegion" && label.name != "OrientationHint") continue;
                    if (label.preferredHeight > label.rectTransform.rect.height + 2)
                        throw new InvalidOperationException("Clipped guidance: " + label.name + " needs " + label.preferredHeight + ", has " + label.rectTransform.rect.height);
                }
                _camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = _target;
                var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(_directory + "/" + Names[_step] + ".png", pixels.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(pixels);
                RenderTexture.active = previous;
                if (++_step == Names.Length)
                {
                    EditorApplication.update -= Tick;
                    _camera.targetTexture = null;
                    _target.Release();
                    UnityEngine.Object.DestroyImmediate(_target);
                    RestoreTheme();
                    EditorApplication.Exit(0);
                    return;
                }
                if (_mapNavigation) AdvanceMapNavigation();
                else if (_itemArtwork) AdvanceItemArtwork();
                else if (_scavenge) AdvanceScavenge();
                else if (_regional) AdvanceRegional();
                else if (_direct) AdvanceDirect();
                else if (_interaction) AdvanceInteraction();
                else if (_growth) AdvanceGrowth();
                else if (_themes)
                {
                    if (_step % 2 == 1) typeof(AppShell).GetMethod("OpenAudioSettings", Private).Invoke(_shell, null);
                    else
                    {
                        typeof(AppShell).GetMethod("CloseSettings", Private).Invoke(_shell, null);
                        Theme.Select(_step == 2 ? "military" : "shelter");
                    }
                }
                else if (_step == 1)
                {
                    _session.ChooseEmployer("HWANG");
                    UnityEngine.Object.DestroyImmediate(_shell.transform.Find("Canvas/EmployerHost").gameObject);
                    typeof(AppShell).GetMethod("OnEmployerChosen", Private).Invoke(_shell, null);
                }
                else if (_step == 6) typeof(AppShell).GetMethod("OpenAudioSettings", Private).Invoke(_shell, null);
                else _shell.Select(_step - 1);
                _wait = 0;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.update -= Tick;
                RestoreTheme();
                EditorApplication.Exit(1);
            }
        }

        private sealed class MemoryFiles : IFileStore
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
            public bool Exists(string path) => _files.ContainsKey(path);
            public string ReadAllText(string path) => _files[path];
            public void WriteAllText(string path, string content) => _files[path] = content;
            public void Replace(string from, string to) { _files[to] = _files[from]; _files.Remove(from); }
            public bool TryMove(string from, string to) { if (!Exists(from)) return false; Replace(from, to); return true; }
        }
    }
}
