using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI.Screens;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 앱의 UI 껍데기. 캔버스·헤더·하단 5탭을 만들고 화면을 갈아 끼운다 (GDD §28).
    ///
    /// <para><see cref="Bootstrap"/> 과 같은 방식으로 씬 배치 없이 스스로 뜬다.
    /// 세션이 준비되면 그때 UI 를 만든다 — 부팅 전에는 그릴 데이터가 없다.</para>
    /// </summary>
    public sealed class AppShell : MonoBehaviour
    {
        public static AppShell Instance { get; private set; }

        private GameSession _session;
        private QuestJournalWindow _questJournal;
        private bool _questBriefed;

        internal void OpenQuestJournal(bool daily = false)
        {
            if (!_enteredGame || _session.NeedsEmployerChoice || _explorationView != null && AfterSeoul.Exploration.ExplorationSystem.IsActive(_session.Save)) return;
            if (_questJournal != null) return;
            _questBriefed = true;
            _questJournal = new QuestJournalWindow(this, _session, transform.GetChild(0), daily);
        }

        internal void CloseQuestJournal(bool navigating = false)
        {
            _questJournal?.Destroy(); _questJournal = null;
            if (!navigating) ShowNextReturnReport();
        }

        internal bool TrackQuest(string id)
        {
            try {
                if (!_session.ExecuteSavedAction(s => {
                    if (!QuestJournalData.Build(_session).Exists(q => q.Id == id && !q.Completed)) return false;
                    s.TrackedQuestId = id; return true;
                })) return false;
                AfterAction(); _questJournal?.Refresh(); return true;
            } catch (Exception) { Toast(Loc.Text("목표를 저장하지 못했습니다. 다시 시도해 주세요."), 4); return false; }
        }

        internal void ActOnQuest(string id)
        {
            // Settle a possible 05:00 rollover before looking up daily entries.
            try { _session.Tick(); }
            catch (Exception) { Toast(Loc.Text("진행을 저장하지 못했습니다. 다시 시도해 주세요."), 4); return; }
            var entry = QuestJournalData.Build(_session).Find(q => q.Id == id && !q.Completed);
            if (entry == null) { _questJournal?.Refresh(Loc.Text("의뢰가 갱신되었습니다. 현재 목록을 확인하세요."), false); return; }
            var run = _session.Save.Exploration;
            if (run != null && (run.Result == null || !run.Result.Acknowledged)) {
                CloseQuestJournal(true); OpenExploration(); return;
            }
            if (!TrackQuest(id)) return;
            if (entry.Ready) {
                try {
                    bool paid = _session.ExecuteSavedAction(s => entry.Daily ? _session.Quests.TryDeliver(s, _session.Data, entry.QuestId)
                        : id == "main:first" ? FirstExplorationQuest.Report(s) : RegionalExplorationQuest.Report(s, entry.Map));
                    if (!paid) { _questJournal?.Refresh(Loc.Text("조건이 바뀌었습니다. 목표와 보유량을 확인하세요."), false); return; }
                    AfterAction(); Sfx.Complete();
                    _questJournal?.Refresh(Loc.Text("완료! 받은 보상 · {0}", entry.Reward));
                } catch (Exception) { _questJournal?.Refresh(Loc.Text("저장하지 못했습니다. 보상과 물자는 변경되지 않았습니다. 다시 시도해 주세요."), false); }
                return;
            }
            if (entry.Daily) { _questJournal?.ShowSupplies(); return; }
            CloseQuestJournal(true); OpenExploration(); _explorationView?.FocusQuest(entry.Map);
        }
        private ExplorationView _explorationView;

        public void OpenExploration()
        {
            if (_explorationView != null || !_enteredGame) return;
            if (_stepPrompt != null) { _stepPrompt.gameObject.SetActive(false); Destroy(_stepPrompt.gameObject); _stepPrompt = null; }
            _explorationView = ExplorationView.Open(this, _session, _screenHost.parent);
        }

        internal void ExplorationClosed()
        {
            _explorationView = null;
            AfterAction();
            ShowNextReturnReport();
        }
        private LaunchPresentation _launch;
        private WelcomeBriefing _welcome;
        private RectTransform _languageMenu;
        private bool _enteredGame;
        private RectTransform _stepPrompt;
        private string _lastStepPrompt;
        private bool _homeBriefed;
        private float _nextGuideCheck;
        private readonly Queue<ResolveReport> _pendingReturns = new Queue<ResolveReport>();
        private Text _employerName, _employerRole;
        private EmployerSceneView _employerScene;
        private bool _greetingPending;
        private float _lastGreeting = -100;
        private NpcBanter _banter = new NpcBanter();
        private readonly List<ModalState> _modalStates = new List<ModalState>();
        private bool _skipLaunchOnce;
        private readonly List<ScreenBase> _screens = new List<ScreenBase>();
        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<Image> _tabIcons = new List<Image>();
        private readonly List<Text> _tabLabels = new List<Text>();

        /// <summary>선택된 탭 위에 붙는 띠. 색 차이만으로는 곁눈질로 안 잡힌다.</summary>
        private readonly List<Image> _tabMarks = new List<Image>();

        private int _active = -1;

        private Text _headerTitle;
        private Text _headerMoney;

        /// <summary>헤더의 현지 시각. 게임의 하루 경계가 KST 05:00 이라 시계가 장식이 아니다.</summary>
        private Text _headerClock;
        private int _clockMinute = -1;

        /// <summary>화면에 지금 적혀 있는 소지금. 숫자를 굴리려면 "어디서부터"가 필요하다.</summary>
        private long _shownMoney;
        private bool _moneyKnown;

        private RectTransform _screenHost;
        private RectTransform _toastRoot;
        private Text _toast;
        private float _toastUntil;

        /// <summary>알림이 앉는 자리. 밀려 올라오는 연출의 출발점이 아니라 <b>도착점</b>이다.</summary>
        private Vector2 _toastHome;
        private bool _toastHiding;

        /// <summary>복귀 연출. 한 번에 하나만 뜬다.</summary>
        private ReturnCutscene _cutscene;

        /// <summary>이미 연출로 보여준 보고. 부팅 보고를 켜자마자 두 번 띄우지 않기 위한 것.</summary>
        private ResolveReport _shownReport;
        private RectTransform _audioSettings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            Instance = null;
            var go = new GameObject("[AppShell]");
            DontDestroyOnLoad(go);
            go.AddComponent<AppShell>();
        }

        private void Awake()
        {
            Instance = this;

            // 에디터에서 도메인 리로드를 끈 채로 재생을 반복하면 이전 판의 트랙이 static 에 남는다.
            // 주인이 파괴돼 있어서 위험하진 않지만, 첫 프레임에 한 번 허공을 도는 건 지저분하다.
            Tween.Clear();

            if (Bootstrap.Session != null) OnReady(Bootstrap.Session);
            else Bootstrap.Ready += OnReady;
        }

        private void OnDestroy()
        {
            Theme.Changed -= OnThemeChanged;
            Bootstrap.Ready -= OnReady;
            if (_session != null) _session.Resolved -= OnResolved;
        }

        private void OnReady(GameSession session)
        {
            Bootstrap.Ready -= OnReady;
            Theme.Changed -= OnThemeChanged;
            Theme.Changed += OnThemeChanged;
            _session = session;
            _session.Resolved += OnResolved;
            BuildUi();
        }

        /// <summary>정산이 끝나면 보이는 화면만 다시 그린다. 안 보이는 화면은 켜질 때 그린다.</summary>
        private void OnResolved(ResolveReport report)
        {
            RefreshHeader();
            if (_active >= 0 && _active < _screens.Count) _screens[_active].Refresh();
            MaybeShowReturn(report);
            if (report.DayRollovers > 0) _questJournal?.Refresh();
        }

        /// <summary>
        /// 돌아온 소식을 연출로 띄운다.
        ///
        /// <para>고용주를 아직 안 골랐으면 띄우지 않는다 — 첫 화면 위에 보고서가 겹치면
        /// 무엇을 먼저 해야 하는지가 사라진다. 이미 하나 떠 있어도 띄우지 않는다.</para>
        /// </summary>
        private void MaybeShowReturn(ResolveReport report)
        {
            if (ReferenceEquals(report, _shownReport) || !ReturnCutscene.Worth(report)) return;
            if (!_enteredGame || _explorationView != null || _cutscene != null || _employerHost != null || _welcome != null || _stepPrompt != null || _questJournal != null)
            {
                if (!_pendingReturns.Contains(report)) _pendingReturns.Enqueue(report);
                return;
            }
            _shownReport = report;
            _cutscene = new ReturnCutscene(transform.GetChild(0), report, _session, () =>
            {
                _cutscene = null;
                ShowNextReturnReport();
            });
        }

        private void ShowNextReturnReport()
        {
            if (!_enteredGame || _explorationView != null || _cutscene != null || _employerHost != null || _welcome != null || _stepPrompt != null || _questJournal != null) return;
            while (_pendingReturns.Count > 0 && _cutscene == null)
                MaybeShowReturn(_pendingReturns.Dequeue());
        }

        private void EnterGame()
        {
            if (_enteredGame) return;
            _enteredGame = true;
            _launch = null;
            if (_session.NeedsEmployerChoice && _session.Save.WelcomePage >= 0) ShowWelcome();
            else if (_session.NeedsEmployerChoice) ShowEmployerChoice();
            else { Select(0); ShowNextReturnReport(); }
            RefreshHeader();
        }

        private void Update()
        {
            // 화면 코드보다 먼저 돈다. 이 프레임의 보간값이 반영된 뒤에 화면이 읽어야
            // 한 프레임 늦은 값을 그리지 않는다.
            Tween.Tick(Time.unscaledDeltaTime);
            _launch?.Tick(Time.unscaledDeltaTime);

            if (_enteredGame && _explorationView == null && _welcome == null && _stepPrompt == null && _questJournal == null && _cutscene == null && _languageMenu == null && _audioSettings == null && _active >= 0 && _active < _screens.Count)
                _screens[_active].Tick(Time.unscaledDeltaTime);

            if (Time.unscaledTime >= _nextGuideCheck)
            {
                _nextGuideCheck = Time.unscaledTime + .5f;
                MaybeShowStepPrompt();
            }
            if (HasInputActivity()) _banter.Activity();
            TickGreeting(Time.unscaledDeltaTime);

            TickClock();

            if (_toastRoot != null && _toastRoot.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                HideToast();
        }

        private void TickGreeting(float delta)
        {
            if (!CanNpcTalk()) { _banter.Activity(); return; }
            if (_greetingPending && _active == 0)
            {
                _greetingPending = false;
                if (Time.unscaledTime - _lastGreeting >= 30)
                {
                    _lastGreeting = Time.unscaledTime;
                    _employerScene.BeginGreeting(_session.Save.Player.EmployerNpcId);
                }
            }
            _employerScene.TickGreeting(delta);
            if (_banter.Tick(delta,true)) SpeakToEmployer();
        }

        private bool CanNpcTalk()
        {
            if (!_enteredGame || _session == null || _session.NeedsEmployerChoice || _active < 0 || _welcome != null || _cutscene != null || _employerHost != null || _audioSettings != null || _languageMenu != null) return false;
            if (_screens[_active] is FactoryScreen factory && factory.HasOpenDialogue) return false;
            GetComponentsInChildren(true,_modalStates);
            foreach(var modal in _modalStates) if(modal.VisibleWithin(transform)) return false;
            var reset=transform.GetChild(0).Find("ProgressResetDialog");
            return reset == null || !reset.gameObject.activeSelf;
        }
        private void SpeakToEmployer()
        {
            if (!CanNpcTalk()) return;
            _greetingPending=false; _lastGreeting=Time.unscaledTime;
            string npc=_session.Save.Player.EmployerNpcId;
            _employerScene.BeginLine(npc,_banter.Next(npc));
        }
        private static bool HasInputActivity()
        {
#if ENABLE_INPUT_SYSTEM
            var pointer=UnityEngine.InputSystem.Pointer.current;
            var keyboard=UnityEngine.InputSystem.Keyboard.current;
            var mouse=UnityEngine.InputSystem.Mouse.current;
            return (pointer!=null && pointer.press.isPressed)
                || (keyboard!=null && keyboard.anyKey.isPressed)
                || (mouse!=null && mouse.scroll.ReadValue().sqrMagnitude>0);
#else
            return Input.anyKey || Input.touchCount>0 || Input.mouseScrollDelta.sqrMagnitude>0;
#endif
        }

        /// <summary>
        /// 헤더 시계. 분이 바뀔 때만 글자를 건드린다 — Text 는 값을 넣을 때마다 메시를 다시 만든다.
        ///
        /// <para>게임의 하루가 KST 05:00 에 넘어가므로(GDD §7) 기기 시각이 아니라 KST 를 적고,
        /// 05:00 이 가까우면 그걸 같이 알린다. 일일 의뢰를 놓치는 건 대부분 "몇 시인지 몰라서"다.</para>
        /// </summary>
        private void TickClock()
        {
            if (_headerClock == null || _session == null) return;

            var kst = _session.Clock.UtcNow.ToOffset(GameTime.GameZoneOffset);
            if (kst.Minute == _clockMinute) return;
            _clockMinute = kst.Minute;

            // 05:00 경계까지 남은 시간. 지나갔으면 다음 날 05:00.
            var boundary = new DateTimeOffset(kst.Year, kst.Month, kst.Day, 5, 0, 0, GameTime.GameZoneOffset);
            if (kst >= boundary) boundary = boundary.AddDays(1);

            var left = boundary - kst;
            bool soon = left.TotalHours < 2.0;

            _headerClock.text = soon
                ? Loc.Text("{0:HH:mm} KST   ·   교대까지 {1}분", kst, (int)left.TotalMinutes)
                : $"{kst:HH:mm} KST";
            _headerClock.color = soon ? Theme.Warn : Theme.TextFaint;
        }

        // ── 만들기 ──────────────────────────────────────────────

        private void BuildUi()
        {
            _questJournal = null; _questBriefed = false;
            EnsureEventSystem();

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Theme.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // 0.5 = 가로·세로를 반반 맞춘다. 0 이면 좁은 폰에서 세로로 잘리고,
            // 1 이면 태블릿에서 가로가 터진다.
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // 바탕은 안전 영역 바깥까지 칠한다. 노치 옆이 하얗게 뜨면 싸구려로 보인다.
            var bg = Ui.Panel("Background", canvasGo.transform, Theme.Bg);
            Ui.Stretch(bg.rectTransform);

            var safe = Ui.Rect("SafeArea", canvasGo.transform);
            Ui.Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();

            BuildHeader(safe);

            _screenHost = Ui.Rect("Screens", safe);
            Ui.Stretch(_screenHost, 0f, 0f, Theme.HeaderHeight, Theme.TabBarHeight);

            _screens.Add(new HomeScreen());
            _screens.Add(new FactoryScreen());
            _screens.Add(new ExpeditionScreen());
            _screens.Add(new PersonnelScreen());
            _screens.Add(new WarehouseScreen());
            foreach (var s in _screens) s.Create(this, _session, _screenHost);

            BuildTabBar(safe);
            BuildVignette(canvasGo.transform);
            BuildToast(safe);

            Select(0);
            RefreshHeader();

            // 고용주를 아직 안 골랐으면 그 화면이 먼저다 (GDD §4). 탭 위에 덮어씌운다 —
            // 탭을 하나 내주면 한 번 고르고 나서 그 자리가 영영 죽는다.
            // Employer choice follows the explicit title-screen tap.

            // 부팅 정산은 이 셸이 구독하기 전에 이미 끝나 있다 — Bootstrap 이 Boot() 를 부르고
            // 나서야 Ready 가 울리기 때문이다. 그래서 이벤트를 기다리면 <b>가장 중요한 경우</b>
            // (밤새 자리를 비웠다가 켠 순간)에만 연출이 안 뜬다. 여기서 직접 집어온다.
            MaybeShowReturn(_session.LastReport);
            if (Application.isPlaying && !_skipLaunchOnce) _launch = new LaunchPresentation(canvasGo.transform, EnterGame, OpenLanguageMenu);
            else EnterGame(); // Existing editor render tools preview the app directly.
            _skipLaunchOnce=false;

            if (Bootstrap.BootError != null)
                Toast(Loc.Text("부팅 오류: ") + Bootstrap.BootError, 8f);
        }

        /// <summary>
        /// 씬에 EventSystem 이 없으면 만든다. 없으면 버튼이 눌리지 않는데,
        /// 원인이 화면에 아무 표시도 안 나서 찾기 어렵다.
        /// </summary>
        private static void EnsureEventSystem()
        {
            // 에디터의 실제 UI 렌더 검증에는 입력 시스템과 씬 수명 처리가 필요 없다.
            if (!Application.isPlaying) return;
            if (EventSystem.current != null) return;

            var go = new GameObject("[EventSystem]");
            DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        /// <summary>
        /// 화면 가장자리를 어둡게 덮는다 (GDD §26).
        ///
        /// <para>이것 하나로 "웹페이지"가 "케이스에 든 화면"이 된다. 반복 질감(스캔라인)을
        /// 쓰지 않은 이유는 기기 해상도에 따라 물결이 생기는데 그게 감성이 아니라 고장처럼
        /// 보이기 때문이다 — 한 장을 늘려 쓰면 어느 해상도에서도 같은 모양이 나온다.</para>
        ///
        /// <para>안전 영역 <b>바깥</b>까지 덮는다. 노치 주변만 밝게 남으면 덮개가 아니라 얼룩이다.</para>
        /// </summary>
        private static void BuildVignette(Transform canvas)
        {
            var rt = Ui.Rect("Vignette", canvas);
            Ui.Stretch(rt);

            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = Skin.Vignette;
            raw.color = Color.white;
            // 이걸 켜두면 화면 전체가 버튼을 삼킨다. 덮개는 보이기만 해야 한다.
            raw.raycastTarget = false;
        }

        /// <summary>
        /// 헤더 = 계기판. 왼쪽에 지금 보는 곳, 오른쪽에 소지금과 현지 시각.
        ///
        /// <para>시계를 넣은 건 장식이 아니다. 하루가 KST 05:00 에 넘어가서(GDD §7)
        /// 일일 의뢰와 무전 시한이 그 선을 기준으로 움직인다 — 몇 시인지 모르면
        /// "왜 갑자기 의뢰가 바뀌었지"가 된다.</para>
        /// </summary>
        private void BuildHeader(Transform parent)
        {
            var header = Ui.Rect("Header", parent);
            Ui.Top(header, Theme.HeaderHeight);
            Ui.Panel("HeaderBg", header, Theme.Panel);
            var line = Ui.Panel("HeaderLine", header, Theme.AccentDim);
            Ui.Bottom(line.rectTransform, 2);
            var brand = Ui.Label("TerminalBrand", header, Loc.Text("AFTER SEOUL / 현장 운영망"), 23, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Top(brand.rectTransform, 36, Theme.Gutter);
            var sound = Ui.Button("AudioSettings", header, Loc.Text("설정"), OpenAudioSettings, Theme.PanelAlt, 24);
            var soundRect = (RectTransform)sound.transform;
            soundRect.anchorMin = soundRect.anchorMax = Vector2.one;
            soundRect.pivot = Vector2.one;
            soundRect.sizeDelta = new Vector2(132, 54);
            soundRect.anchoredPosition = new Vector2(-Theme.Gutter, -8);

            _employerScene = new EmployerSceneView(header);
            _employerScene.EnableTalk(SpeakToEmployer);
            var scene = _employerScene.Root;
            scene.anchorMin = new Vector2(0,1); scene.anchorMax = Vector2.one;
            scene.offsetMin = new Vector2(Theme.Gutter, -268);
            scene.offsetMax = new Vector2(-Theme.Gutter, -54);
            _employerName = HeaderText(header, "EmployerName", 278, 44, Theme.FontHeading, Theme.Text);
            _employerRole = HeaderText(header, "EmployerRole", 326, 32, Theme.FontSmall, Theme.TextDim);
            _employerRole.horizontalOverflow = HorizontalWrapMode.Wrap;
            _headerClock = HeaderText(header, "Clock", 358, 26, 22, Theme.TextFaint);
            _headerClock.alignment = TextAnchor.MiddleRight;
            _headerTitle = Ui.Label("Title", header, "", 30, TextAnchor.MiddleLeft, Theme.Text);
            Ui.Bottom(_headerTitle.rectTransform, 42, Theme.Gutter);
            _headerTitle.rectTransform.anchorMax = new Vector2(.5f,0);
            _headerMoney = Ui.Label("Money", header, "", 32, TextAnchor.MiddleRight, Theme.Accent);
            Ui.Bottom(_headerMoney.rectTransform, 42, Theme.Gutter);
            _headerMoney.rectTransform.anchorMin = new Vector2(.5f,0);
            _headerMoney.resizeTextForBestFit = true;
            _headerMoney.resizeTextMinSize = 20; _headerMoney.resizeTextMaxSize = 32;
        }

        private static Text HeaderText(Transform parent, string name, float top, float height, int size, Color color)
        {
            var text = Ui.Label(name, parent, "", size, TextAnchor.MiddleLeft, color);
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0,1); rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(Theme.Gutter, -top-height);
            rt.offsetMax = new Vector2(-Theme.Gutter, -top);
            return text;
        }

        // Kept as an entry point for the existing editor capture harness.
        private void OpenAudioSettings() => OpenSettings();

        private void OnThemeChanged(Color[] before, Color[] after)
        {
            if (this == null) { Theme.Changed -= OnThemeChanged; return; }
            Theme.ApplyTo(transform, before, after);
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool active = i == _active;
                _tabButtons[i].targetGraphic.color = active ? Theme.AccentDim : Theme.Panel;
                _tabLabels[i].color = active ? Theme.Text : Theme.TextDim;
                _tabIcons[i].color = active ? Theme.Accent : Theme.TextFaint;
                _tabMarks[i].color = active ? Theme.Accent : Color.clear;
            }
        }

        private void CloseSettings()
        {
            if (_audioSettings == null) return;
            var go = _audioSettings.gameObject;
            _audioSettings = null;
            go.SetActive(false);
            go.transform.SetParent(null, false);
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private void ShowWelcome(bool replay = false)
        {
            // Existing residents are not forced through a newly introduced prologue.
            if (_welcome != null || (!replay && (!_session.NeedsEmployerChoice || _session.Save.WelcomePage < 0))) return;
            _welcome = new WelcomeBriefing(transform.GetChild(0), _session, replay, () =>
            {
                _welcome = null;
                if (_session.NeedsEmployerChoice) ShowEmployerChoice();
                ShowNextReturnReport();
            });
        }

        private void MaybeShowStepPrompt()
        {
            if (_explorationView != null || _questJournal != null || _questBriefed || _active != 0) return;
            if (!_enteredGame || _session == null || _session.NeedsEmployerChoice ||
                _welcome != null || _stepPrompt != null || _cutscene != null ||
                _employerHost != null || _languageMenu != null || _audioSettings != null) return;
            // Never interrupt an active minigame or another equipment/settings modal.
            if (_active == 1 && ((FactoryScreen)_screens[1]).IsWorking) return;
            foreach (var button in GetComponentsInChildren<Button>())
                if (button.name == "Close" && button.gameObject.activeInHierarchy) return;
            OpenQuestJournal();
        }

        internal void ShowStepPrompt(string key)
        {
            if (_stepPrompt != null) return;
            if (FirstExplorationQuest.IsPending(_session.Save) || !_session.Save.ExplorationStarterClaimed) key = "direct_exploration";
            bool direct = key == "direct_exploration";
            Action close = () => {
                if (_stepPrompt == null) return;
                _stepPrompt.gameObject.SetActive(false);
                Destroy(_stepPrompt.gameObject); _stepPrompt = null;
                ShowNextReturnReport();
            };
            _stepPrompt = Ui.Modal("StepPrompt", transform.GetChild(0),
                Loc.TraderName(_session.Save.Player.EmployerNpcId), close, out var body);
            _stepPrompt.gameObject.AddComponent<SafeArea>();
            var panel = _stepPrompt.Find("Panel") as RectTransform;
            panel.anchorMin = new Vector2(0, .3f); panel.anchorMax = new Vector2(1, .7f);
            panel.offsetMin = new Vector2(36, 0); panel.offsetMax = new Vector2(-36, 0);
            GameArt.Portrait("Npc", body, _session.Save.Player.EmployerNpcId, 170);
            var title = Ui.Label("ActionTitle", body, direct ? Loc.Text("첫 탐색을 준비해 봅시다") : Tutorial.ActionTitle(key), 28, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Size(title.gameObject, 42);
            string directHint = FirstExplorationQuest.IsPending(_session.Save)
                ? (_session.Save.FirstExplorationQuest?.Accepted == true ? FirstExplorationQuest.Objective(_session.Save) : ExplorationDialogue.Invitation(_session.Save.Player.EmployerNpcId))
                : ExplorationDialogue.Invitation(_session.Save.Player.EmployerNpcId);
            var hint = Ui.Paragraph("NextAction", body, direct ? directHint : Tutorial.ActionHint(key), 32, Theme.Text);
            Ui.Size(hint.gameObject, 180);
            var go = Ui.Button("FollowGuide", body, direct ? Loc.Text("직접 탐색하기  →") : key == "deliver_first" ? Loc.Text("꾸러미 납품 · 50,000원 수령") :
                Loc.Text("{0}(으)로 가기", Loc.Text(Tutorial.ActionTab(key))), () => {
                    close();
                    if (direct) { OpenExploration(); return; }
                    if (key == "deliver_first") {
                        if (_session.DeliverOrientation()) { Sfx.Complete(); Toast(Loc.Text("초도 납품 완료 · 50,000원 지급")); }
                        AfterAction();
                    }
                    SelectByName(Tutorial.ActionTab(key));
                    if (key == "Deliver") ((HomeScreen)_screens[0]).OpenTasks();
                    for (int i = 0; i < _screens.Count; i++)
                        if (_screens[i].TabName == Tutorial.ActionTab(key)) Tween.Punch(_tabButtons[i].transform, .1f, .35f);
                    string target = key == "pick_work" ? "R_RCP_SALVAGE" : key == "play_work" ? "Action" :
                        key == "equip_first" ? "Loadout" : key == "HireScav" ? "Hire" :
                        key == "depart_first" ? "OrientationDepart" : key == "Treat" ? "Treat" : null;
                    if (target != null) foreach (var button in _screenHost.GetComponentsInChildren<Button>())
                        if (button.name == target && button.gameObject.activeInHierarchy) {
                            Ui.SetEdge((RectTransform)button.transform, Theme.Accent);
                            Tween.Punch(button.transform, .08f, .4f);
                            break;
                        }
                }, Theme.AccentDim);
            Ui.Size(go.gameObject, 90);
        }

        private void OpenLanguageMenu()
        {
            if (_languageMenu != null) return;
            Action close = () => { if (_languageMenu != null) { _languageMenu.gameObject.SetActive(false); Destroy(_languageMenu.gameObject); _languageMenu = null; } };
            _languageMenu = Ui.Modal("LanguageMenu", transform.GetChild(0), "LANGUAGE", close, out var body);
            for (int i = 0; i < Bootstrap.Languages.Length; i++)
            {
                string code = Bootstrap.Languages[i];
                var option = Ui.Button("Language_" + code, body, Bootstrap.LanguageNames[i], () =>
                {
                    PlayerPrefs.SetString("AfterSeoul.Language", code); PlayerPrefs.Save();
                    close();
                    if (_enteredGame) { Toast(Loc.Text("선택한 언어는 다음 실행부터 적용됩니다."), 5); return; }
                    // No gameplay yet: all screens may be reconstructed safely.
                    Bootstrap.SelectLanguage(code);
                    Tween.Clear();
                    var old = transform.GetChild(0).gameObject; old.SetActive(false); old.transform.SetParent(null, false); Destroy(old);
                    _screens.Clear(); _tabButtons.Clear(); _tabIcons.Clear(); _tabLabels.Clear(); _tabMarks.Clear();
                    _screenHost = null; _toastRoot = null; _toast = null; _active = -1; _clockMinute = -1;
                    _launch = null; _moneyKnown = false;
                    BuildUi();
                }, code == PlayerPrefs.GetString("AfterSeoul.Language", Loc.CurrentLanguage) ? Theme.AccentDim : Theme.PanelAlt, 30);
                Ui.Size(option.gameObject, 96);
                option.GetComponentInChildren<Text>().font = Resources.Load<Font>("Fonts/NotoSansCJK");
            }
            var hint = Ui.Paragraph("LanguageHint", body, Loc.Text("게임 중 변경한 언어는 다음 실행부터 적용됩니다. 진행 기록은 유지됩니다."), 27, Theme.TextDim);
            Ui.Size(hint.gameObject, 120);
        }

        private void OpenSettings()
        {
            if (!_enteredGame || _audioSettings != null) return;
            _audioSettings = Ui.Modal("AudioSettings", transform.GetChild(0), Loc.Text("설정"), CloseSettings, out var body);
            var language = Ui.Button("Language", body, "LANGUAGE / " + Bootstrap.LanguageNames[Array.IndexOf(Bootstrap.Languages, Loc.CurrentLanguage)], OpenLanguageMenu, Theme.AccentDim, 28);
            Ui.Size(language.gameObject, 76);
            var replay = Ui.Button("ReplayWelcome", body, Loc.Text("처음 안내 다시 보기"), () => { CloseSettings(); ShowWelcome(true); }, Theme.PanelAlt, 28);
            Ui.Size(replay.gameObject, 76);
            var intro = Ui.Paragraph("Intro", body, Loc.Text("당신의 서울, 당신의 분위기.\n선택한 테마와 소리는 다음 접속에도 유지됩니다."), 27, Theme.TextDim);
            Ui.Size(intro.gameObject, 78);
            var heading = Ui.Label("ThemeHeading", body, Loc.Text("화면 테마"), 32, TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Size(heading.gameObject, 44);
            foreach (var themeId in Theme.Ids)
            {
                var id = themeId;
                var colors = Theme.Palette(id);
                var option = Ui.Button("ThemeOption_" + id, body, "", () =>
                {
                    CloseSettings();
                    Theme.Select(id);
                    OpenSettings();
                }, colors[1], withLabel: false);
                Ui.Size(option.gameObject, 158, flexHeight: 0);
                Ui.SetEdge((RectTransform)option.transform, Theme.Id == id ? colors[7] : colors[3]);
                var picture = Ui.Rect("Preview", option.transform);
                picture.anchorMax = new Vector2(.32f, 1);
                picture.offsetMin = new Vector2(8, 8); picture.offsetMax = new Vector2(-8, -8);
                var art = picture.gameObject.AddComponent<ThemeScene>();
                art.PreviewThemeId = id; art.raycastTarget = false;
                var title = Ui.Label("Name", option.transform, Theme.Name(id), 32, TextAnchor.MiddleLeft, colors[4]);
                title.rectTransform.anchorMin = new Vector2(.35f, .48f);
                title.rectTransform.anchorMax = new Vector2(.80f, .92f);
                title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
                var desc = Ui.Label("Description", option.transform, Theme.Description(id), 24, TextAnchor.MiddleLeft, colors[5]);
                desc.rectTransform.anchorMin = new Vector2(.35f, .06f);
                desc.rectTransform.anchorMax = new Vector2(.98f, .48f);
                desc.rectTransform.offsetMin = desc.rectTransform.offsetMax = Vector2.zero;
                var selected = Ui.Label("Selected", option.transform, Theme.Id == id ? Loc.Text("사용 중") : Loc.Text("적용"), 24, TextAnchor.MiddleRight, colors[7]);
                selected.rectTransform.anchorMin = new Vector2(.80f, .5f);
                selected.rectTransform.anchorMax = new Vector2(.98f, .92f);
                selected.rectTransform.offsetMin = selected.rectTransform.offsetMax = Vector2.zero;
            }
            var audioTitle = Ui.Label("SoundHeading", body, Loc.Text("소리"), 32, TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Size(audioTitle.gameObject, 54);
            AddVolumeSlider(body, Loc.Text("효과음"), () => Sfx.EffectsVolume, v => Sfx.EffectsVolume = v,
                () => Sfx.EffectsMuted, v => Sfx.EffectsMuted = v);
            AddVolumeSlider(body, Loc.Text("배경 음악"), () => Sfx.MusicVolume, v => Sfx.MusicVolume = v,
                () => Sfx.MusicMuted, v => Sfx.MusicMuted = v);
            var motion = Ui.Button("ReducedMotion", body, "", null, Theme.PanelAlt, 28);
            Ui.Size(motion.gameObject, 76);
            System.Action motionLabel = () => Ui.SetButtonLabel(motion, Loc.Text("연출 줄이기   ") + (PresentationSettings.ReducedMotion ? Loc.Text("켜짐") : Loc.Text("꺼짐")));
            motionLabel();
            motion.onClick.AddListener(() =>
            {
                PresentationSettings.ReducedMotion = !PresentationSettings.ReducedMotion;
                Tween.CompletePresentation();
                motionLabel();
            });
            var note = Ui.Paragraph("MotionNote", body, Loc.Text("화면 전환·버튼 확대·점멸을 줄입니다. 제작 조작은 유지됩니다."), 24, Theme.TextDim);
            Ui.Size(note.gameObject, 65);
            bool confirmReset = false;
            var reset = Ui.Button("ResetPreferences", body, Loc.Text("설정 초기화"), null, Theme.PanelAlt, 27);
            Ui.Size(reset.gameObject, 70);
            reset.onClick.AddListener(() =>
            {
                if (!confirmReset) { confirmReset = true; Ui.SetButtonLabel(reset, Loc.Text("한 번 더 눌러 초기화 · 진행 기록은 유지")); return; }
                CloseSettings(); PresentationSettings.Reset(); OpenSettings();
            });
            var accountReset = Ui.Button("ResetAccount", body, Loc.Text("계정 초기화 · 처음부터 시작"), ConfirmAccountReset, Theme.Danger, 27);
            Ui.Size(accountReset.gameObject, 82);
        }

        private void ConfirmAccountReset()
        {
            CloseSettings();
            _audioSettings = Ui.Modal("AccountResetConfirmation", transform.GetChild(0),
                Loc.Text("처음부터 다시 시작할까요?"), ReturnToSettings, out var body);
            var explanation = Ui.Paragraph("ResetConsequences", body,
                Loc.Text("이 기기의 캐릭터, 돈, 장비, 창고, 파견, 퀘스트와 튜토리얼 진행을 모두 지웁니다. 되돌릴 수 없습니다.\n\n프롤로그와 NPC 선택부터 다시 시작합니다.\n소리·화면 설정과 본편 계정 및 서버에 이미 발송된 물건은 삭제하지 않습니다."), 29, Theme.Text);
            Ui.Size(explanation.gameObject, 350);
            var cancel = Ui.Button("CancelAccountReset", body, Loc.Text("취소 · 진행 유지"), ReturnToSettings, Theme.PanelAlt);
            Ui.Size(cancel.gameObject, 86);
            var confirm = Ui.Button("ConfirmAccountReset", body, Loc.Text("진행을 지우고 처음부터 시작"), ResetAccount, Theme.Danger);
            Ui.Size(confirm.gameObject, 86);
        }

        private void ReturnToSettings() { CloseSettings(); OpenSettings(); }

        private void ResetAccount()
        {
            var previousSave = _session.Save;
            try { _session.ResetProgress(); }
            catch (Exception error) {
                if (!ReferenceEquals(previousSave, _session.Save)) { RestartUiAfterReset(); return; }
                Toast(Loc.Text("초기화하지 못했습니다. 기존 진행은 유지됩니다.") + "\n" + error.Message, 5);
                return;
            }
            AndroidNotifications.CancelAll();
            CloseSettings(); StopAllCoroutines(); Tween.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--) {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            _screens.Clear(); _tabButtons.Clear(); _tabIcons.Clear(); _tabLabels.Clear(); _tabMarks.Clear();
            _pendingReturns.Clear(); _shownReport = null; _cutscene = null;
            _explorationView = null; _launch = null; _welcome = null;
            _languageMenu = null; _stepPrompt = null; _employerHost = null;
            _enteredGame = false; _active = -1;
            _moneyKnown = false; _clockMinute = -1; _toastUntil = 0; _toastHiding = false; _nextGuideCheck = 0;
            _lastGreeting = -100; _banter = new NpcBanter(); _skipLaunchOnce = true;
            Sfx.SetFactoryMusic(false);
            BuildUi();
        }

        private void RestartUiAfterReset()
        {
            Tween.Clear();
            var old=transform.GetChild(0).gameObject;
            old.SetActive(false); old.transform.SetParent(null,false);
            if(Application.isPlaying)Destroy(old);else DestroyImmediate(old);
            _screens.Clear(); _tabButtons.Clear(); _tabIcons.Clear(); _tabLabels.Clear(); _tabMarks.Clear();
            _pendingReturns.Clear(); _shownReport=null; _cutscene=null; _welcome=null;
            _audioSettings=null; _languageMenu=null; _employerHost=null; _launch=null;
            _screenHost=null; _toastRoot=null; _toast=null; _active=-1; _clockMinute=-1;
            _moneyKnown=false; _shownMoney=0; _enteredGame=false; _greetingPending=false;
            _lastGreeting=-100; _banter=new NpcBanter(); _skipLaunchOnce=true;
            Sfx.SetFactoryMusic(false);
            BuildUi();
        }

        private static void AddVolumeSlider(Transform body, string title, Func<float> get, Action<float> set,
            Func<bool> muted, Action<bool> mute)
        {
            var row = Ui.Rect(title, body);
            Ui.Size(row.gameObject, 124, flexHeight: 0);
            var label = Ui.Label("Value", row, "", 28, TextAnchor.MiddleLeft);
            Ui.Top(label.rectTransform, 44);
            label.rectTransform.anchorMax = new Vector2(.66f, 1);
            var muteButton = Ui.Button("Mute", row, "", null, Theme.PanelAlt, 24);
            var mr = (RectTransform)muteButton.transform;
            mr.anchorMin = new Vector2(.72f, 1); mr.anchorMax = Vector2.one;
            mr.offsetMin = new Vector2(0, -46); mr.offsetMax = Vector2.zero;
            Action update = () =>
            {
                label.text = title + "  " + Mathf.RoundToInt(get() * 100) + "%" + (muted() ? Loc.Text(" · 음소거") : "");
                Ui.SetButtonLabel(muteButton, muted() ? Loc.Text("소리 켜기") : Loc.Text("음소거"));
            };
            var track = Ui.Rect("Slider", row);
            track.anchorMin = Vector2.zero; track.anchorMax = new Vector2(1, 0);
            track.pivot = new Vector2(.5f, 0); track.offsetMin = new Vector2(0, 8); track.offsetMax = new Vector2(0, 70);
            var hit = track.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var slider = track.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1;
            var rail = Ui.Panel("Rail", track, Theme.Line);
            Ui.Stretch(rail.rectTransform, 18, 18, 26, 26);
            var fillArea = Ui.Rect("FillArea", track); Ui.Stretch(fillArea, 18, 18, 26, 26);
            var fill = Ui.Panel("Fill", fillArea, Theme.Accent);
            slider.fillRect = fill.rectTransform;
            var handleArea = Ui.Rect("HandleArea", track); Ui.Stretch(handleArea, 18, 18, 0, 0);
            var handle = Ui.Panel("Handle", handleArea, Theme.Text);
            handle.sprite = Skin.Pill; handle.type = Image.Type.Sliced;
            handle.rectTransform.sizeDelta = new Vector2(34, 0);
            slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            slider.SetValueWithoutNotify(get());
            slider.onValueChanged.AddListener(v => { set(v); update(); });
            muteButton.onClick.AddListener(() => { mute(!muted()); update(); });
            update();
        }


        private void BuildTabBar(Transform parent)
        {
            var bar = Ui.Rect("TabBar", parent);
            Ui.Bottom(bar, Theme.TabBarHeight);
            // Fixed to the safe area's bottom; never participates in a content layout group.
            bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var bg = Ui.Panel("TabBg", bar, Theme.Panel);
            Ui.Stretch(bg.rectTransform);

            var line = Ui.Rect("TabLine", bar);
            var lineImg = line.gameObject.AddComponent<Image>();
            lineImg.color = Theme.Line;
            lineImg.raycastTarget = false;
            Ui.Top(line, 2f);

            var row = Ui.Rect("Tabs", bar);
            Ui.Stretch(row, 8f, 8f, 6f, 8f);
            Ui.Row(row, 6f);

            for (int i = 0; i < _screens.Count; i++)
            {
                int index = i;
                var screen = _screens[i];

                // 라벨을 직접 쌓는다 — 아이콘 위, 이름 아래. 자동 라벨은 가운데 한 줄이라 못 얹는다.
                var btn = Ui.Button("Tab_" + screen.TabName, row, "",
                    () => Select(index), Theme.Panel, Theme.FontTab, withLabel: false);
                Ui.Size(btn.gameObject, flexWidth: 1f);   // 5개가 폭을 똑같이 나눠 갖는다

                // 선택된 탭 위에 붙는 띠. 배경색 차이만으로는 곁눈질로 안 잡힌다.
                var mark = Ui.Rect("Mark", btn.transform);
                Ui.Top(mark, 5f);
                var markImg = mark.gameObject.AddComponent<Image>();
                markImg.raycastTarget = false;
                _tabMarks.Add(markImg);

                var col = Ui.Rect("Col", btn.transform);
                Ui.Stretch(col, 4f, 4f, 12f, 8f);
                Ui.Column(col, 2f);

                var icon = Ui.Rect("Icon", col);
                Ui.Size(icon.gameObject, 52f);
                var iconImg = icon.gameObject.AddComponent<Image>();
                iconImg.sprite = IconSet.ForTab(screen.Glyph);
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                _tabIcons.Add(iconImg);

                var label = Ui.Label("Label", col, Loc.Text(screen.TabName), Theme.FontTab,
                    TextAnchor.MiddleCenter, Theme.TextDim);
                Ui.Size(label.gameObject, 34f);
                _tabLabels.Add(label);

                _tabButtons.Add(btn);
            }
        }

        private void BuildToast(Transform parent)
        {
            var rt = Ui.Rect("Toast", parent);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, Theme.TabBarHeight + 24f);
            rt.sizeDelta = new Vector2(Theme.ReferenceResolution.x - Theme.Gutter * 2f, 92f);
            _toastHome = rt.anchoredPosition;

            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Skin.Panel;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.04f, 0.05f, 0.06f, 0.94f);
            img.raycastTarget = false;

            Ui.SetEdge(rt, Theme.Edge);

            _toast = Ui.Label("ToastText", rt, "", Theme.FontSmall, TextAnchor.MiddleCenter);
            Ui.Stretch(_toast.rectTransform, 20f, 20f, 0f, 0f);
            _toast.horizontalOverflow = HorizontalWrapMode.Wrap;

            _toastRoot = rt;
            rt.gameObject.SetActive(false);
        }

        // ── 조작 ────────────────────────────────────────────────

        public void Select(int index)
        {
            if (index < 0 || index >= _screens.Count) return;
            // 새 세이브의 고용주 ID는 선택 전까지 비어 있다. 홈을 포함한 탭은 선택 후 연다.
            if (!_enteredGame || _session == null || _session.NeedsEmployerChoice) return;

            // 같은 탭을 다시 누르면 전환 애니메이션을 다시 틀지 않는다. 목록을 훑다가
            // 실수로 두 번 누르는 일이 흔한데, 그때마다 화면이 미끄러지면 오작동처럼 보인다.
            bool changed = _active != index;
            int direction = _active < 0 ? 1 : (index > _active ? 1 : -1);

            for (int i = 0; i < _screens.Count; i++)
            {
                bool on = i == index;
                _screens[i].SetVisible(on);

                if (i < _tabButtons.Count)
                {
                    var img = _tabButtons[i].targetGraphic as Image;
                    if (img != null) Tween.Tint(img, on ? Theme.AccentDim : Theme.Panel, 0.14f);
                }
                if (i < _tabLabels.Count) Tween.Tint(_tabLabels[i], on ? Theme.Text : Theme.TextDim, 0.14f);
                if (i < _tabIcons.Count) Tween.Tint(_tabIcons[i], on ? Theme.Accent : Theme.TextFaint, 0.14f);

                // 꺼진 탭의 띠는 투명하게 둔다 — 어두운 색으로 칠하면 그것도 선으로 보인다.
                if (i < _tabMarks.Count)
                    Tween.Tint(_tabMarks[i], on ? Theme.Accent : new Color(0f, 0f, 0f, 0f), 0.14f);
            }

            if (changed)
            {
                _screens[index].PlayEnter(direction);
                if (index < _tabIcons.Count) Tween.Punch(_tabIcons[index].transform, 0.14f, 0.22f);
            }

            _active = index;
            bool compact = index == 1 || _screens[index] is WarehouseScreen;
            var header = _headerTitle.transform.parent as RectTransform;
            Ui.Top(header, compact ? 130 : Theme.HeaderHeight);
            _employerScene.Root.gameObject.SetActive(!compact);
            _employerName.gameObject.SetActive(!compact);
            _employerRole.gameObject.SetActive(!compact);
            _headerClock.gameObject.SetActive(!compact);
            Ui.Stretch(_screenHost, 0, 0, compact ? 130 : Theme.HeaderHeight, Theme.TabBarHeight);
            Sfx.SetFactoryMusic(_screens[index].TabName == "공장");
            if (changed) {
                _greetingPending = index == 0;
                if (index != 0) _employerScene.StopGreeting();
            }
            RefreshHeader();
        }

        /// <summary>고용주 선택 화면. 다 고르면 스스로 사라진다.</summary>
        private RectTransform _employerHost;

        private void ShowEmployerChoice()
        {
            if (_employerHost != null) return;

            _employerHost = Ui.Rect("EmployerHost", transform.GetChild(0));
            Ui.Stretch(_employerHost);
            _employerHost.gameObject.AddComponent<SafeArea>();
            new Screens.EmployerScreen(this, _session, _employerHost);
        }

        /// <summary>고용주를 골랐다. 화면을 걷고 전부 다시 그린다.</summary>
        internal void OnEmployerChosen()
        {
            if (_employerHost != null)
            {
                _employerHost.gameObject.SetActive(false);
                _employerHost.SetParent(null, false);
                if (Application.isPlaying) Destroy(_employerHost.gameObject);
                else DestroyImmediate(_employerHost.gameObject);
                _employerHost = null;
            }

            if (_active < 0) Select(0);
            AfterAction();

            OpenQuestJournal();
            // Deliver queued offline reports only after the player has entered.
            ShowNextReturnReport();
        }

        /// <summary>다른 화면으로 보낸다. 탭 이름으로 찾는다.</summary>
        public void SelectByName(string tabName)
        {
            for (int i = 0; i < _screens.Count; i++)
                if (_screens[i].TabName == tabName) { Select(i); return; }
        }

        public void RefreshHeader()
        {
            if (_headerTitle == null || _session == null) return;
            _headerTitle.text = _active >= 0 && _active < _screens.Count ? Loc.Text(_screens[_active].Title) : "";

            string employer = _session.Save.Player.EmployerNpcId;
            _employerScene.SetEmployer(employer);
            _employerName.text = string.IsNullOrEmpty(employer) ? Loc.Text("고용주 선택 대기") : Loc.TraderName(employer);
            _employerRole.text = EmployerPortrait.Role(employer);
            long money = _session.Save.Player.Money;

            // 숫자를 굴린다. 소지금이 소리 없이 바뀌면 방금 판 것이 얼마였는지 알 수가 없다 —
            // 움직이는 숫자 자체가 영수증이다. 첫 표시는 굴리지 않는다(0 에서 올라오면 그냥 이상하다).
            if (!_moneyKnown)
            {
                _moneyKnown = true;
                _headerMoney.text = Theme.Won(money);
            }
            else if (money != _shownMoney)
            {
                Tween.Number(_headerMoney, _shownMoney, money, Theme.Won);
                Tween.Tint(_headerMoney, money > _shownMoney ? Theme.Safe : Theme.Danger, 0.08f);
                // 0.5초 뒤 제 색으로. 색까지 계속 남으면 그건 상태 표시가 되어버린다.
                Tween.Play(_headerMoney.rectTransform, "moneyReset", 0f, _ => { },
                    Tween.Ease.Linear, 0.5f,
                    () => { if (_headerMoney != null) Tween.Tint(_headerMoney, Theme.Accent, 0.25f); });
            }

            _shownMoney = money;
        }

        /// <summary>화면 아래쪽에 잠깐 뜨는 알림. 실패 사유를 삼키지 않으려고 쓴다.</summary>
        public void Toast(string message, float seconds = 2.5f)
        {
            if (_toast == null || _toastRoot == null) return;

            _toast.text = message;
            _toastRoot.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + seconds;
            _toastHiding = false;

            // 제자리부터 다시 시작한다. 사라지던 중에 새 알림이 오면 지금 위치가 목표가 되어버려서,
            // 알림이 뜰 때마다 조금씩 아래로 내려앉는다.
            _toastRoot.anchoredPosition = _toastHome;

            // 아래에서 밀려 올라온다. 제자리에 툭 나타나면 원래 있던 것처럼 보여서
            // "방금 뭔가 일어났다"는 신호가 되지 못한다.
            Tween.FadeIn(_toastRoot, 0.14f);
            Tween.SlideIn(_toastRoot, new Vector2(0f, -28f), 0.22f);
        }

        /// <summary>
        /// 알림을 걷는다. <b>한 번만 시작한다</b> — 매 프레임 다시 걸면 알파가 그때마다
        /// 현재값에서 새로 출발해서 영영 사라지지 않는다.
        /// </summary>
        private void HideToast()
        {
            if (_toastRoot == null || _toastHiding) return;
            _toastHiding = true;

            Tween.FadeOut(_toastRoot, 0.18f, () =>
            {
                // 사라지는 동안 새 알림이 왔으면 그걸 지우면 안 된다.
                if (_toastRoot != null && _toastHiding) _toastRoot.gameObject.SetActive(false);
            });
        }

        /// <summary>화면들이 조작 후에 부른다. 헤더(소지금)와 현재 화면을 같이 갱신한다.</summary>
        public void AfterAction()
        {
            // 조작 중에 레벨이 올랐으면 여기서 알린다. 화면마다 따로 챙기게 하면
            // 어디선가 반드시 빠지고, 그러면 헤더의 숫자만 소리 없이 바뀐다.
            if (_session != null && _session.ConsumeLevelUps() > 0)
                Toast(Loc.Text("레벨 {0} 달성", _session.Save.Player.Level), 3f);

            RefreshHeader();
            if (_active >= 0 && _active < _screens.Count) _screens[_active].Refresh();
        }
    }
}
