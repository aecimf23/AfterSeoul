using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
            if (_themes || _growth)
            {
                _session.ChooseEmployer("HWANG");
                UnityEngine.Object.DestroyImmediate(_shell.transform.Find("Canvas/EmployerHost").gameObject);
                typeof(AppShell).GetMethod("OnEmployerChosen", Private).Invoke(_shell, null);
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
                if (_growth) AdvanceGrowth();
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
