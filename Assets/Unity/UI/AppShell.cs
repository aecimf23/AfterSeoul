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
            Bootstrap.Ready -= OnReady;
            if (_session != null) _session.Resolved -= OnResolved;
        }

        private void OnReady(GameSession session)
        {
            Bootstrap.Ready -= OnReady;
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
        }

        /// <summary>
        /// 돌아온 소식을 연출로 띄운다.
        ///
        /// <para>고용주를 아직 안 골랐으면 띄우지 않는다 — 첫 화면 위에 보고서가 겹치면
        /// 무엇을 먼저 해야 하는지가 사라진다. 이미 하나 떠 있어도 띄우지 않는다.</para>
        /// </summary>
        private void MaybeShowReturn(ResolveReport report)
        {
            if (_cutscene != null || _employerHost != null) return;
            if (ReferenceEquals(report, _shownReport)) return;
            if (!ReturnCutscene.Worth(report)) return;

            _shownReport = report;
            _cutscene = new ReturnCutscene(transform.GetChild(0), report, _session, () => _cutscene = null);
        }

        private void Update()
        {
            // 화면 코드보다 먼저 돈다. 이 프레임의 보간값이 반영된 뒤에 화면이 읽어야
            // 한 프레임 늦은 값을 그리지 않는다.
            Tween.Tick(Time.unscaledDeltaTime);

            if (_active >= 0 && _active < _screens.Count)
                _screens[_active].Tick(Time.unscaledDeltaTime);

            TickClock();

            if (_toastRoot != null && _toastRoot.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                HideToast();
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
                ? $"{kst:HH:mm} KST   ·   교대까지 {(int)left.TotalMinutes}분"
                : $"{kst:HH:mm} KST";
            _headerClock.color = soon ? Theme.Warn : Theme.TextFaint;
        }

        // ── 만들기 ──────────────────────────────────────────────

        private void BuildUi()
        {
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
            if (_session.NeedsEmployerChoice) ShowEmployerChoice();

            // 부팅 정산은 이 셸이 구독하기 전에 이미 끝나 있다 — Bootstrap 이 Boot() 를 부르고
            // 나서야 Ready 가 울리기 때문이다. 그래서 이벤트를 기다리면 <b>가장 중요한 경우</b>
            // (밤새 자리를 비웠다가 켠 순간)에만 연출이 안 뜬다. 여기서 직접 집어온다.
            MaybeShowReturn(_session.LastReport);

            if (Bootstrap.BootError != null)
                Toast("부팅 오류: " + Bootstrap.BootError, 8f);
        }

        /// <summary>
        /// 씬에 EventSystem 이 없으면 만든다. 없으면 버튼이 눌리지 않는데,
        /// 원인이 화면에 아무 표시도 안 나서 찾기 어렵다.
        /// </summary>
        private static void EnsureEventSystem()
        {
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

            var bg = Ui.Panel("HeaderBg", header, Theme.Panel);
            Ui.Stretch(bg.rectTransform);

            // 아주 흐린 격자. 계측기 눈금처럼 보이되 글자를 읽는 데 걸리지 않는 세기여야 한다.
            var grid = Ui.Rect("HeaderGrid", header);
            Ui.Stretch(grid);
            var gridImg = grid.gameObject.AddComponent<RawImage>();
            gridImg.texture = Skin.Grid;
            gridImg.color = new Color(1f, 1f, 1f, 0.5f);
            gridImg.raycastTarget = false;
            gridImg.uvRect = new Rect(0f, 0f,
                Theme.ReferenceResolution.x / 24f, Theme.HeaderHeight / 24f);

            var line = Ui.Panel("HeaderLine", header, Theme.Edge);
            Ui.Bottom(line.rectTransform, 2f);

            // 제목 앞의 짧은 막대. 글자가 바뀌어도 시선의 출발점이 고정된다.
            var pip = Ui.Rect("Pip", header);
            pip.anchorMin = new Vector2(0f, 0.5f);
            pip.anchorMax = new Vector2(0f, 0.5f);
            pip.pivot = new Vector2(0f, 0.5f);
            pip.sizeDelta = new Vector2(6f, 40f);
            pip.anchoredPosition = new Vector2(Theme.Gutter, 6f);
            var pipImg = pip.gameObject.AddComponent<Image>();
            pipImg.sprite = Skin.Pill;
            pipImg.type = Image.Type.Sliced;
            pipImg.color = Theme.Accent;
            pipImg.raycastTarget = false;

            _headerTitle = Ui.Label("Title", header, "", Theme.FontHeading, TextAnchor.MiddleLeft);
            Ui.Stretch(_headerTitle.rectTransform, Theme.Gutter + 20f, 0f, 0f, 22f);

            _headerMoney = Ui.Label("Money", header, "", Theme.FontHeading, TextAnchor.MiddleRight, Theme.Accent);
            Ui.Stretch(_headerMoney.rectTransform, 0f, Theme.Gutter, 0f, 22f);

            _headerClock = Ui.Label("Clock", header, "", Theme.FontTab, TextAnchor.MiddleRight, Theme.TextFaint);
            Ui.Stretch(_headerClock.rectTransform, 0f, Theme.Gutter, 62f, 6f);
        }

        private void BuildTabBar(Transform parent)
        {
            var bar = Ui.Rect("TabBar", parent);
            Ui.Bottom(bar, Theme.TabBarHeight);

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

                var label = Ui.Label("Label", col, screen.TabName, Theme.FontTab,
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
            RefreshHeader();
        }

        /// <summary>고용주 선택 화면. 다 고르면 스스로 사라진다.</summary>
        private RectTransform _employerHost;

        private void ShowEmployerChoice()
        {
            if (_employerHost != null) return;

            _employerHost = Ui.Rect("EmployerHost", transform.GetChild(0));
            Ui.Stretch(_employerHost);
            new Screens.EmployerScreen(this, _session, _employerHost);
        }

        /// <summary>고용주를 골랐다. 화면을 걷고 전부 다시 그린다.</summary>
        internal void OnEmployerChosen()
        {
            if (_employerHost != null)
            {
                _employerHost.SetParent(null, false);
                Destroy(_employerHost.gameObject);
                _employerHost = null;
            }

            AfterAction();

            // 고용주 화면에 가려 못 띄운 보고가 있으면 이제 띄운다.
            MaybeShowReturn(_session.LastReport);
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
            _headerTitle.text = _active >= 0 && _active < _screens.Count ? _screens[_active].Title : "";

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
                Toast($"레벨 {_session.Save.Player.Level} 달성", 3f);

            RefreshHeader();
            if (_active >= 0 && _active < _screens.Count) _screens[_active].Refresh();
        }
    }
}
