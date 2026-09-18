using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using AfterSeoul.Unity.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AfterSeoul.Unity.Editor
{
    /// <summary>Actual uGUI render and hit-target bounds checks, using disposable memory saves.</summary>
    public static class EquipmentPreview
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static AppShell _shell;
        private static GameSession _session;
        private static bool _quests;
        private static bool _raids;
        private static bool _depth;
        private static Canvas _canvas;
        private static Camera _camera;
        private static RenderTexture _target;
        private static int _step, _wait;
        private static string[] Names = { "warehouse-16x9", "knife-detail", "knife-equipped", "warehouse-tall", "exploration-loadout" };
        private static string Folder = "Logs/equipment-preview";
        public static void CaptureDepth()
        {
            _depth=true;Folder="Logs/depth-preview";
            Names=new[]{"preparation","scav-dialogue","grenade-response","facilities","barter","recovery-tall","regional-routes"};
            Capture();_shell.OpenExploration();Click("Explore_YONGSAN_MARKET");
        }

        public static void CaptureRaids()
        {
            _raids = true; Folder = "Logs/raid-preview";
            Names = new[] { "departure", "equipment-comparison", "combat-feedback", "full-bag", "departure-tall" };
            Capture();
            Warehouse.TryAdd(_session.Save.Warehouse, _session.Data, "WPN01", 1);
            _shell.OpenExploration(); Click("Explore_YONGSAN_MARKET");
        }

        public static void CaptureQuests()
        {
            _quests = true; Folder = "Logs/quest-preview";
            Names = new[] { "home-goal", "main-quests", "daily-quests", "reward-and-next", "home-tall" };
            Capture();
        }

        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("PreviewEvents").AddComponent<EventSystem>();
            var locale = Resources.Load<TextAsset>("Locales/ko");
            var mobile = Resources.Load<TextAsset>("Locales/mobile/ko");
            Loc.Load("ko", locale.text, locale.text, mobile?.text);
            IDataRegistry data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            var clock = new TestClock(DateTimeOffset.Parse("2026-09-18T01:00:00Z"));
            var session = new GameSession(new SaveService(new MemoryFiles(), new NewtonsoftJsonCodec(), clock), data, clock);
            _session = session;
            session.Boot(); session.Save.Player.Name="서울 생존자"; session.ChooseEmployer("HWANG"); session.Save.WelcomePage = -1;
            session.Save.FirstExplorationQuest.Completed = !_quests;
            session.Save.ExplorationTutorialSeen = 15;
            ExplorationSystem.PrepareStarter(session.Save, data);
            foreach (string slot in PlayerEquipment.Slots) {
                if (slot == "Weapon" || slot == "Melee") continue;
                var item = data.AllItems.First(i => PlayerEquipment.SlotFor(i) == slot);
                Warehouse.TryAdd(session.Save.Warehouse, data, item.Id, 1);
                PlayerEquipment.TryEquip(session.Save, data, item.Id);
            }
            Warehouse.TryAdd(session.Save.Warehouse, data, "MEL01", 1);
            var host = new GameObject("EquipmentPreviewShell"); host.SetActive(false);
            _shell = host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady", Private).Invoke(_shell, new object[] { session });
            _shell.enabled = false; host.SetActive(true); _shell.SelectByName(_quests ? "기지" : "창고");
            _canvas = host.GetComponentInChildren<Canvas>();
            _canvas.GetComponent<CanvasScaler>().enabled = false;
            _camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = Theme.Bg;
            _camera.orthographic = true;
            _canvas.renderMode = RenderMode.ScreenSpaceCamera; _canvas.worldCamera = _camera; _canvas.planeDistance = 10;
            Resize(1920);
            Directory.CreateDirectory(Folder);
            EditorApplication.update += Tick;
        }

        private static void Resize(int height)
        {
            if (_target != null) { _camera.targetTexture = null; _target.Release(); UnityEngine.Object.DestroyImmediate(_target); }
            _target = new RenderTexture(1080, height, 24); _target.Create(); _camera.targetTexture = _target;
            _canvas.scaleFactor = Mathf.Sqrt(height / 1920f);
            var safe = _canvas.transform.Find("SafeArea") as RectTransform;
            safe.GetComponent<SafeArea>().enabled = false;
            safe.anchorMin = new Vector2(.015f, .015f); safe.anchorMax = new Vector2(.985f, .98f);
        }

        private static void Click(string name)
        {
            var button = _shell.GetComponentsInChildren<Button>().LastOrDefault(b => b.name == name && b.interactable);
            if (button == null) throw new InvalidOperationException("Missing button " + name);
            button.onClick.Invoke();
        }

        private static void Tick()
        {
            try {
                if (++_wait < 16) return;
                Tween.Tick(3); Canvas.ForceUpdateCanvases();
                foreach (var rect in _shell.GetComponentsInChildren<RectTransform>())
                    if (rect.GetComponent<LayoutGroup>() != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                Canvas.ForceUpdateCanvases();
                if (_quests) foreach (var tabs in _shell.GetComponentsInChildren<RectTransform>().Where(r => r.name == "QuestTabs"))
                    if (tabs.rect.height > 100) throw new InvalidOperationException("Quest category tabs consumed the content area.");
                foreach (var button in _shell.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Slot_") || _depth && new[]{"Dodge","ScavAid","ScavTrade","ScavLeave","ScavFight","EnterSelectedMap"}.Contains(b.name))) {
                    var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                    foreach (var corner in corners) {
                        var screen = _camera.WorldToViewportPoint(corner);
                        if (screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1)
                            throw new InvalidOperationException(button.name + " clipped at " + screen);
                    }
                }
                _camera.Render();
                var previous = RenderTexture.active; RenderTexture.active = _target;
                var pixels = new Texture2D(_target.width, _target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Folder + "/" + Names[_step] + ".png", pixels.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.active = previous;
                _step++; _wait = 0;
                if(_depth) {
                    var view=_shell.GetComponentInChildren<ExplorationView>();
                    switch(_step) {
                        case 1:
                            Click("EnterSelectedMap");var contact=_session.Save.Exploration;
                            contact.Phase=ExplorationPhase.Encounter;contact.EncounterKind="Scav";contact.ConversationOpen=true;contact.ScavAttitude="Friendly";contact.Indoors=false;contact.Weather=ExplorationWeather.Rain;
                            contact.Enemy=new ExplorationEnemy{Kind="Scav",Name="스캐브 소총수",Archetype="Rifleman"};
                            typeof(ExplorationView).GetMethod("Render",Private).Invoke(view,null);break;
                        case 2:
                            var battle=_session.Save.Exploration;battle.Phase=ExplorationPhase.Combat;battle.ConversationOpen=false;
                            battle.Enemy=new ExplorationEnemy{Kind="PMC",Name="PMC 척탄병",Archetype="Grenadier",Action=EnemyAction.Grenade,Remaining=1.9};
                            typeof(ExplorationView).GetMethod("Render",Private).Invoke(view,null);break;
                        case 3:
                            _session.Save.Exploration=null;typeof(ExplorationView).GetMethod("Close",Private).Invoke(view,null);
                            _shell.SelectByName("기지");Click("RaidWorkshop");break;
                        case 4:Click("BarterTab");break;
                        case 5:
                            Click("Close");_session.Save.Player.Money=0;_session.Save.Player.Equipment.Clear();_session.Save.Warehouse.Stacks.Clear();
                            _shell.OpenExploration();Click("Explore_YONGSAN_MARKET");Click("RecoveryKit");Resize(2400);break;
                        case 6:
                            Click("EnterSelectedMap");ExplorationSystem.Move(_session.Save,_session.Data,0);
                            _session.Save.Exploration.Phase=ExplorationPhase.Routes;
                            typeof(ExplorationView).GetMethod("Render",Private).Invoke(_shell.GetComponentInChildren<ExplorationView>(),null);break;
                        default:EditorApplication.update-=Tick;Debug.Log("Depth preview passed: contact, grenade, workshop, barter, recovery, routes.");EditorApplication.Exit(0);break;
                    }
                    return;
                }
                if (_raids) {
                    var view = _shell.GetComponentInChildren<ExplorationView>();
                    switch (_step) {
                        case 1: Click("QuickSlot_Weapon"); break;
                        case 2:
                            Click("BackToLoadout"); Click("EnterSelectedMap");
                            var combat = _session.Save.Exploration;
                            combat.AwaitingEntryChoice = false; combat.Phase = ExplorationPhase.Combat;
                            combat.Enemy = new ExplorationEnemy { Name = "PMC 정찰병", Kind = "PMC", Hp = 70, MaxHp = 90, Action = EnemyAction.Aiming, Remaining = 1.4 };
                            combat.PlayerFeedback = "명중! 적 HP −22"; combat.EnemyFeedback = "피격 · 내 HP −4 · 엄폐로 피해 80% 감소";
                            typeof(ExplorationView).GetMethod("Render", Private).Invoke(view, null); break;
                        case 3:
                            var loot = _session.Save.Exploration; loot.Phase = ExplorationPhase.EncounterResult;
                            loot.PendingLoot.Add(new ItemStack("WPN01", 1));
                            foreach (var item in _session.Data.AllItems.Take(8)) loot.Loot.Add(new ItemStack(item.Id, 1));
                            loot.LootCapacity = 8;
                            typeof(ExplorationView).GetMethod("Render", Private).Invoke(view, null); Click("ManageLoot"); break;
                        case 4:
                            _session.Save.Exploration = null;
                            typeof(ExplorationView).GetMethod("CloseModal", Private).Invoke(view, null);
                            typeof(ExplorationView).GetMethod("Render", Private).Invoke(view, null);
                            Click("Explore_YONGSAN_MARKET"); Resize(2400); break;
                        default: EditorApplication.update -= Tick; Debug.Log("Raid preview passed: departure, comparison, combat, bag, tall departure."); EditorApplication.Exit(0); break;
                    }
                    return;
                }
                if (_quests) {
                    switch (_step) {
                        case 1: Click("QuestJournal"); break;
                        case 2: Click("DailyQuests"); break;
                        case 3:
                            typeof(AppShell).GetMethod("CloseQuestJournal", Private).Invoke(_shell, new object[] { true });
                            FirstExplorationQuest.Accept(_session.Save);
                            _session.Save.FirstExplorationQuest.ReadyToReport = true;
                            _session.Save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
                            typeof(AppShell).GetMethod("OpenQuestJournal", Private).Invoke(_shell, new object[] { false }); Click("QuestAction_main:first"); break;
                        case 4: typeof(AppShell).GetMethod("CloseQuestJournal", Private).Invoke(_shell, new object[] { false }); _shell.AfterAction(); Resize(2400); break;
                        default: EditorApplication.update -= Tick; Debug.Log("Quest preview passed: home, main, daily, reward, next objective."); EditorApplication.Exit(0); break;
                    }
                    return;
                }
                switch (_step) {
                    case 1: Click("Item_MEL01"); break;
                    case 2: Click("WearItem"); break;
                    case 3: Click("Close"); Resize(2400); break;
                    case 4: _shell.OpenExploration(); Click("Loadout"); break;
                    default: EditorApplication.update -= Tick; Debug.Log("Equipment preview passed: five views and slot bounds."); EditorApplication.Exit(0); break;
                }
            } catch (Exception error) { Debug.LogException(error); EditorApplication.update -= Tick; EditorApplication.Exit(1); }
        }

        private sealed class MemoryFiles : IFileStore
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
            public bool Exists(string path) => _files.ContainsKey(path);
            public string ReadAllText(string path) => _files[path];
            public void WriteAllText(string path, string content) => _files[path] = content;
            public void Replace(string source, string destination) { _files[destination] = _files[source]; _files.Remove(source); }
            public bool TryMove(string source, string destination) { if (!Exists(source)) return false; Replace(source, destination); return true; }
        }
    }
}
