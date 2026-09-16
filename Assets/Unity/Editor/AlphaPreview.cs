using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using AfterSeoul.Core;
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
        private static bool _starter;
        private static bool _production;
        private static bool _expeditionNavigation;
        private static bool _launchArt;
        private static object _launchPreview;
        public static void CaptureLaunchArt()
        {
            _launchArt=true;
            _directory="Logs/retro-launch-preview";
            Names=new[] {"00-original-ascii","01-crossfade","02-mobile-map","03-title","04-hwang","05-choi","06-kim"};
            Capture();
        }
        private static void TickLaunch(float delta)
        {
            _launchPreview.GetType().GetMethod("Tick",Private).Invoke(_launchPreview,new object[]{delta});
        }
        private static void AdvanceLaunchArt()
        {
            switch(_step) {
                case 1: TickLaunch(1.8f); break;
                case 2: TickLaunch(1.8f); break;
                case 3: TickLaunch(1.1f); break;
                case 4: ClickPreview("LaunchPresentation"); PreviewGreeting("HWANG"); break;
                case 5: PreviewGreeting("DR_CHOI"); break;
                case 6: PreviewGreeting("YONGSAN_KIM"); break;
            }
        }
        public static void CaptureExpeditionNavigation()
        {
            _expeditionNavigation=true;
            _directory="Logs/expedition-navigation-preview";
            Names=new[] {"00-open-regions","01-region-details","02-team-selection","03-dispatched","04-all-open-regions","05-guro-details","06-regular-team","07-regular-dispatched","08-active-region-detail","09-live-progress"};
            Capture();
        }
        private static void AdvanceExpeditionNavigation()
        {
            switch(_step) {
                case 1: ClickPreview("SelectMap_MYEONGDONG"); break;
                case 2: ClickPreview("PrepareExpedition"); ClickPreview("Pick_"+_session.Save.Scavs[0].Uid); break;
                case 3: ClickPreview("OrientationDepart"); break;
                case 4:
                    _session.Save.Player.Level=9; _session.Save.NpcTrust["HWANG"]=10; _session.Save.Player.Money=500000;
                    _session.Hire(_session.Save.Market.Offers.First(o=>!o.Hired).OfferId);
                    _shell.AfterAction(); break;
                case 5: ClickPreview("SelectMap_GURO_FACTORY"); break;
                case 6: ClickPreview("PrepareExpedition"); ClickPreview("Pick_"+_session.Save.Scavs.First(s=>s.Status==ScavStatus.Idle).Uid); break;
                case 7: ClickPreview("Depart_GURO_FACTORY"); break;
                case 8: ClickPreview("SelectMap_GURO_FACTORY"); break;
                case 9:
                    _growthClock.Advance(TimeSpan.FromMinutes(17.5));
                    typeof(AppShell).GetMethod("TickClock",Private).Invoke(_shell,null);
                    ((List<ScreenBase>)typeof(AppShell).GetField("_screens",Private).GetValue(_shell)).Find(s=>s.TabName=="탐색").Tick(.1f);
                    break;
            }
            typeof(AppShell).GetMethod("HideToast",Private).Invoke(_shell,null);
        }
        public static void CaptureProduction()
        {
            _production = true;
            _directory = "Logs/anime-factory-preview";
            Names = new[] { "00-first-shift", "01-making-p17", "02-first-delivery", "03-equipment", "04-kim-offer", "05-prototype", "06-ready-to-deliver", "07-approved", "08-crafted-tools", "09-crew-assigned", "10-auto-production", "11-offline-wages", "12-parts-workshop", "13-three-workers", "14-workers-impact", "15-hwang-greeting", "16-choi-greeting", "17-kim-greeting", "18-touch-dialogue", "19-idle-dialogue", "20-reset-confirmation", "21-reset-employer", "22-reset-tutorial", "23-reset-first-shift", "24-tutorial-replay", "25-inline-upgrades", "26-inline-tools" };
            Capture();
        }
        private static void AdvanceProduction()
        {
            switch (_step) {
                case 1:
                    for (int i=0; i<5; i++) HitProduction();
                    break;
                case 2:
                    for (int i=0; i<5; i++) HitProduction();
                    break;
                case 3:
                    for (int i=0; i<40; i++) HitProduction();
                    _shell.AfterAction();
                    ClickPreview("FactoryEquipment");
                    ClickPreview("EquipmentTools");
                    break;
                case 4:
                    ClickPreview("ProductionUpgrade"); ClickPreview("EquipmentCommissions"); ClickPreview("ProductionUnlock");
                    break;
                case 5:
                    ClickPreview("CommissionAccept");
                    for (int i=0; i<5; i++) HitProduction();
                    break;
                case 6:
                    int guard=0;
                    while (!AfterSeoul.Factory.ProductionWork.TrialReady(_session.Save) && guard++<1000) HitProduction();
                    break;
                case 7:
                    ClickPreview("ProductionDeliver");
                    break;
                case 8:
                    ClickPreview("CommissionApproved"); ClickPreview("FactoryEquipment"); ClickPreview("EquipmentTools");
                    ClickPreview("Equipment_AssemblyJig"); ClickPreview("Equipment_PowerTools");
                    break;
                case 9:
                    foreach (var offer in _session.Save.Market.Offers) if (offer.Tier==1 && !offer.Hired) { _session.Hire(offer.OfferId); break; }
                    ClickPreview("EquipmentCrew");
                    ClickPreview("Assign_" + _session.Save.Scavs[0].Uid);
                    break;
                case 10:
                    ClickPreview("FactoryProduction");
                    WaitForProductionPart();
                    break;
                case 11:
                    _growthClock.Advance(TimeSpan.FromHours(1)); _session.Tick(); _shell.AfterAction();
                    break;
                case 12:
                    var cutscene = typeof(AppShell).GetField("_cutscene", Private).GetValue(_shell);
                    if (cutscene != null) typeof(ReturnCutscene).GetMethod("Close", Private).Invoke(cutscene, null);
                    ClickPreview("FactoryEquipment"); ClickPreview("FactoryParts");
                    break;
                case 13:
                    _session.Save.Player.Money=200000;
                    _session.UpgradeProductionEquipment(AfterSeoul.Factory.ProductionEquipment.ExtraBench);
                    _session.UpgradeProductionEquipment(AfterSeoul.Factory.ProductionEquipment.ExtraBench);
                    for (int i=0; i<2; i++) {
                        var scav=new ScavState {Uid="previewWorker"+i, Name=i==0?"불곰":"까치", Status=ScavStatus.Idle};
                        _session.Save.Scavs.Add(scav); _session.AssignProductionScav(scav.Uid);
                    }
                    ClickPreview("FactoryProduction");
                    PreviewFactory().Tick(.1f);
                    break;
                case 14:
                    PreviewFactory().Tick(.65f);
                    break;
                case 15: PreviewGreeting("HWANG"); break;
                case 16: PreviewGreeting("DR_CHOI"); break;
                case 17: PreviewGreeting("YONGSAN_KIM"); break;
                case 18:
                    ClickPreview("EmployerScene");
                    for(int i=0;i<18;i++) typeof(AppShell).GetMethod("TickGreeting",Private).Invoke(_shell,new object[]{.2f});
                    break;
                case 19:
                    for(int i=0;i<47;i++) typeof(AppShell).GetMethod("TickGreeting",Private).Invoke(_shell,new object[]{1f});
                    break;
                case 20:
                    typeof(AppShell).GetMethod("OpenSettings",Private).Invoke(_shell,null); ClickPreview("ResetProgress");
                    break;
                case 21:
                    ClickPreview("ConfirmProgressReset");
                    var canvas=_shell.GetComponentInChildren<Canvas>();
                    canvas.GetComponent<CanvasScaler>().enabled=false; canvas.scaleFactor=1;
                    canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=_camera; canvas.planeDistance=10;
                    break;
                case 22: ClickPreview("E_HWANG"); break;
                case 23: ClickPreview("StartPractice"); break;
                case 24:
                    typeof(AppShell).GetMethod("OpenSettings",Private).Invoke(_shell,null); ClickPreview("ReplayWelcome");
                    break;
                case 25:
                    ClickPreview("Skip");
                    _session.Save.Player.Money=100000;
                    ClickPreview("FactoryProduction");
                    _shell.SelectByName("공장"); _shell.AfterAction();
                    ClickPreview("ProductionToEquipment");
                    break;
                case 26:
                    _shell.GetComponentsInChildren<ScrollRect>().Single(s=>s.name=="ProductionScroll").verticalNormalizedPosition=0;
                    break;
            }
            var factoryView=typeof(AfterSeoul.Unity.UI.Screens.FactoryScreen).GetField("_production",Private).GetValue(PreviewFactory());
            var speech=factoryView.GetType().GetField("_dialogSpeech",Private).GetValue(factoryView) as NpcSpeech;
            speech?.Finish();
            // AppShell.Update is disabled in captures; expire its transient toast explicitly.
            typeof(AppShell).GetMethod("HideToast", Private).Invoke(_shell, null);
        }
        private static ScreenBase PreviewFactory() => ((List<ScreenBase>)typeof(AppShell).GetField("_screens",Private).GetValue(_shell)).Find(s=>s.TabName=="공장");
        private static void PreviewGreeting(string npc)
        {
            _session.Save.Player.EmployerNpcId=npc;
            _session.Save.Quests.ActiveGameDate=null;
            _growthClock.Advance(TimeSpan.FromSeconds(1));
            _session.Tick();
            _shell.SelectByName("기지"); _shell.RefreshHeader();
            typeof(AppShell).GetField("_greetingPending",Private).SetValue(_shell,true);
            typeof(AppShell).GetField("_lastGreeting",Private).SetValue(_shell,-100f);
            var tick=typeof(AppShell).GetMethod("TickGreeting",Private);
            for(int i=0;i<15;i++) tick.Invoke(_shell,new object[]{.2f});
        }
        private static void WaitForProductionPart()
        {
            var screens=(List<ScreenBase>)typeof(AppShell).GetField("_screens",Private).GetValue(_shell);
            var factory=screens.Find(s=>s.TabName=="공장");
            for(int i=0;i<200;i++) {
                if(_session.Conveyor.CooldownRemaining<=0 && _session.Conveyor.Parts.Any(p=>p.Position>=_session.Conveyor.HitStart && p.Position<=_session.Conveyor.HitEnd)) return;
                factory.Tick(.05f);
            }
            throw new InvalidOperationException("No part arrived for preview");
        }
        private static void HitProduction()
        {
            WaitForProductionPart();
            var press=_shell.GetComponentsInChildren<PressButton>().First(p=>p.name=="ProductionTap");
            press.OnPointerDown(new PointerEventData(null));
        }
        public static void CaptureStarter()
        {
            _starter = true;
            _directory = "Logs/starter-preview";
            Names = new[] { "00-briefing", "01-practice-entry", "02-paused-preview", "03-held-signal",
                "04-practice-complete", "05-first-sale", "06-support-ready", "07-supported-contract", "08-first-departure" };
            Capture();
        }

        private static void AdvanceStarter()
        {
            switch (_step) {
                case 1:
                    ClickPreview("StartPractice");
                    ClickPreview("FactoryEquipment"); ClickPreview("FactoryParts");
                    break;
                case 2: ClickPreview("StarterNext"); break;
                case 3:
                    foreach (var press in _shell.GetComponentsInChildren<PressButton>())
                        if (press.name == "Action") { press.OnPointerDown(new PointerEventData(null)); break; }
                    var screens = (List<ScreenBase>)typeof(AppShell).GetField("_screens",Private).GetValue(_shell);
                    screens.Find(s => s.TabName == "공장").Tick(.1f);
                    break;
                case 4: _session.AdvanceWork(0); _shell.AfterAction(); break;
                case 5: case 6: case 7: ClickPreview("StarterNext"); break;
                case 8: ClickPreview("Hire"); break;
            }
        }
        private static TestClock _growthClock;
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
                ClickPreview("SelectMap_MYEONGDONG"); ClickPreview("PrepareExpedition");
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
            if (_themes || _growth || _starter || _production || _expeditionNavigation || _launchArt)
            {
                _session.ChooseEmployer("HWANG");
                UnityEngine.Object.DestroyImmediate(_shell.transform.Find("Canvas/EmployerHost").gameObject);
                typeof(AppShell).GetMethod("OnEmployerChosen", Private).Invoke(_shell, null);
            }
            if (_production) {
                ClickPreview("StartPractice");
                _shell.SelectByName("공장");
            }
            if (_expeditionNavigation) {
                ClickPreview("StartPractice");
                _session.Save.Player.Money=500000;
                _session.Hire(_session.Save.Market.Offers[0].OfferId);
                _shell.SelectByName("탐색"); _shell.AfterAction();
            }
            if (_launchArt) {
                // Finish each navigation before simulating the next user action.
                Tween.Tick(1);
                ClickPreview("StartPractice"); Tween.Tick(1); _shell.SelectByName("기지");
                var type=typeof(AppShell).Assembly.GetType("AfterSeoul.Unity.UI.LaunchPresentation");
                _launchPreview=Activator.CreateInstance(type,Private,null,new object[]{canvas.transform,(Action)(()=>{}),null},null);
                TickLaunch(.8f);
            }
            if (_growth) {
                _session.Save.Player.Money = 500000;
                _session.Hire(_session.Save.Market.Offers[0].OfferId);
                _shell.AfterAction();
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
                if (_starter) foreach (var label in _shell.GetComponentsInChildren<Text>()) {
                    if (label.name != "StarterHint" && label.name != "StarterExplanation" && label.name != "StarterTitle") continue;
                    if (label.preferredHeight > label.rectTransform.rect.height + 2)
                        throw new InvalidOperationException("Clipped starter guidance: " + label.name + " needs " + label.preferredHeight + ", has " + label.rectTransform.rect.height);
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
                if (_launchArt) AdvanceLaunchArt();
                else if (_expeditionNavigation) AdvanceExpeditionNavigation();
                else if (_production) AdvanceProduction();
                else if (_starter) AdvanceStarter();
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
