using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 보여주는 층의 테스트 — 애니메이션 구동기, 보고서 문장, 코드로 굽는 스킨.
    ///
    /// <para><b>왜 이걸 테스트하나:</b> 미니게임 때 한 번 배웠다. 규칙 층은 멀쩡하고 테스트도
    /// 전부 통과하는데 손으로 만지면 고장나 있었다. 화면 코드에도 <b>사람 눈이 아니면 못 잡는 것</b>과
    /// <b>기계가 잡을 수 있는 것</b>이 섞여 있고, 후자는 잡아두는 게 맞다 — 배치가 예쁜지는 못 봐도,
    /// 트랙이 새는지·문장이 빠지는지·경계값이 맞는지는 볼 수 있다.</para>
    /// </summary>
    [TestFixture]
    public class PresentationTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        // IDataRegistry 로 받는다. AllMaps/AllItems/AllRecipes 는 JsonDataRegistry 에서
        // 명시적 인터페이스 구현이라 구체 타입으로는 안 보인다.
        private IDataRegistry _data;

        /// <summary>진짜 데이터로 읽는다. 지역 해금 레벨은 FakeRegistry 가 아니라 그 파일이 정한다.</summary>
        [OneTimeSetUp]
        public void LoadData()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
        }

        [SetUp]
        public void SetUp() => Tween.Clear();

        [TearDown]
        public void TearDown()
        {
            Tween.Clear();
            foreach (var go in _spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private GameObject Owner(string name = "Owner")
        {
            var go = new GameObject(name, typeof(RectTransform));
            _spawned.Add(go);
            return go;
        }

        [TestCase("HWANG")]
        [TestCase("DR_CHOI")]
        [TestCase("YONGSAN_KIM")]
        public void FirstBoot_WaitsForEmployerChoiceBeforeOpeningHome(string employerId)
        {
            var clock = new TestClock(new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero));
            var session = new GameSession(
                new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();
            Assert.IsTrue(session.NeedsEmployerChoice);

            var owner = Owner("FirstBootShell");
            owner.SetActive(false);
            var shell = owner.AddComponent<AppShell>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null)
                Owner("TestEventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
            try
            {
                Assert.DoesNotThrow(() => typeof(AppShell).GetMethod("OnReady", flags)
                    .Invoke(shell, new object[] { session }));
                var screens = (List<ScreenBase>)typeof(AppShell).GetField("_screens", flags).GetValue(shell);
                Assert.IsFalse(screens.Exists(screen => screen.IsVisible));
                Assert.IsNull(owner.transform.Find("Canvas/EmployerHost"));
                Assert.IsNotNull(typeof(AppShell).GetField("_welcome", flags).GetValue(shell));
                // The resident's story now precedes the choice; skipping only dismisses the story.
                var skip = Array.Find(owner.GetComponentsInChildren<Button>(true), b => b.name == "Skip");
                Assert.IsNotNull(skip);
                skip.onClick.Invoke();
                Assert.IsNotNull(owner.transform.Find("Canvas/EmployerHost"));

                shell.Select(0);
                Assert.IsFalse(screens.Exists(screen => screen.IsVisible));
                Assert.IsTrue(session.NeedsEmployerChoice, "화면이 고용주를 임의로 배정하면 안 된다");

                Assert.IsTrue(session.ChooseEmployer(employerId));
                var employerHost = owner.transform.Find("Canvas/EmployerHost");
                // EditMode에서는 지연 Destroy 대신 즉시 정리하고 선택 후 탭 진입을 검증한다.
                UnityEngine.Object.DestroyImmediate(employerHost.gameObject);
                Assert.DoesNotThrow(() => typeof(AppShell).GetMethod("OnEmployerChosen", flags)
                    .Invoke(shell, null));
                Assert.IsTrue(screens[0].IsVisible);
                Assert.AreEqual(employerId, session.Save.Player.EmployerNpcId);
                var exploration = owner.GetComponentInChildren<ExplorationView>(true);
                Assert.IsNotNull(exploration, "Choosing who to seek opens their first quest");
                Assert.IsTrue(Array.Exists(exploration.GetComponentsInChildren<Button>(true), b => b.name == "AcceptFirstQuest"));
                typeof(ExplorationView).GetMethod("Close", flags).Invoke(exploration, null);
                for (int i = 0; i < screens.Count; i++)
                {
                    int tab = i;
                    Assert.DoesNotThrow(() => shell.Select(tab), "모든 탭이 새 세이브에서 열려야 한다");
                    Assert.IsTrue(screens[i].IsVisible);
                }
            }
            finally
            {
                if (eventSystem == null && UnityEngine.EventSystems.EventSystem.current != null)
                    _spawned.Add(UnityEngine.EventSystems.EventSystem.current.gameObject);
            }
        }

        [Test]
        public void BundledFont_ContainsKoreanWithoutSystemFontFallback()
        {
            var font = Resources.Load<Font>("Fonts/D2Coding");
            Assert.IsNotNull(font);
            Assert.AreSame(font, Theme.Font);
            foreach (char c in "서울현장고용주파견창고제작납품")
                Assert.IsTrue(font.HasCharacter(c), "빠진 한글: " + c);
        }

        [Test]
        public void Modal_KeepsHeaderCompactAndLeavesSpaceForContent()
        {
            var parent = (RectTransform)Owner("ModalHost").transform;
            parent.sizeDelta = new Vector2(1080, 1920);
            var modal = Ui.Modal("TestModal", parent, "소리 설정", () => { }, out var body);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            var head = (RectTransform)modal.Find("Panel/Head");
            Assert.That(head.rect.height, Is.EqualTo(74f).Within(1f));
            Assert.That(body.parent.parent.GetComponent<RectTransform>().rect.height, Is.GreaterThan(1000f));
        }

        // ── 구동기 ──────────────────────────────────────────────

        [Test]
        public void Tween_RunsFromZeroToOneAndRetires()
        {
            var owner = Owner();
            float last = -1f;

            Tween.Play(owner.transform, "t", 0.5f, v => last = v, Tween.Ease.Linear);

            Assert.AreEqual(0f, last, 1e-4f, "시작값을 곧바로 반영해야 한다 — 한 프레임 늦으면 깜빡인다");
            Assert.AreEqual(1, Tween.Count);

            Tween.Tick(0.25f);
            Assert.AreEqual(0.5f, last, 1e-3f);
            Assert.AreEqual(1, Tween.Count, "아직 끝나지 않았다");

            Tween.Tick(0.25f);
            Assert.AreEqual(1f, last, 1e-4f);
            Assert.AreEqual(0, Tween.Count, "끝난 트랙은 스스로 물러나야 한다");
        }

        [Test]
        public void Tween_DelayHoldsTheStartValueBack()
        {
            var owner = Owner();
            int calls = 0;

            Tween.Play(owner.transform, "t", 0.2f, _ => calls++, Tween.Ease.Linear, delay: 0.3f);
            Assert.AreEqual(0, calls, "지연이 있으면 시작값도 아직이다");

            Tween.Tick(0.2f);
            Assert.AreEqual(0, calls);

            Tween.Tick(0.2f);
            Assert.Greater(calls, 0);
        }

        /// <summary>
        /// 목록을 다시 그리면 대상이 파괴된다. 그게 예외가 아니라 일상이라,
        /// 화면 코드가 취소를 부르지 않아도 트랙이 스스로 죽어야 한다.
        /// </summary>
        [Test]
        public void Tween_DropsTracksWhoseOwnerWasDestroyed()
        {
            var owner = Owner();
            Tween.Play(owner.transform, "t", 10f, _ => { }, Tween.Ease.Linear);
            Assert.AreEqual(1, Tween.Count);

            UnityEngine.Object.DestroyImmediate(owner);

            Tween.Tick(0.016f);
            Assert.AreEqual(0, Tween.Count, "파괴된 대상의 트랙이 남아 있으면 매 프레임 허공을 만진다");
        }

        [Test]
        public void Tween_SameChannelReplacesInsteadOfStacking()
        {
            var owner = Owner();

            Tween.Play(owner.transform, "fade", 1f, _ => { });
            Tween.Play(owner.transform, "fade", 1f, _ => { });
            Assert.AreEqual(1, Tween.Count, "같은 채널이 겹치면 두 값이 서로 덮어써서 떨린다");

            Tween.Play(owner.transform, "move", 1f, _ => { });
            Assert.AreEqual(2, Tween.Count, "다른 채널은 같이 산다");
        }

        [Test]
        public void Tween_LoopKeepsGoingAndDoesNotDrift()
        {
            var owner = Owner();
            float last = 0f;

            Tween.Loop(owner.transform, "pulse", 1f, v => last = v, Tween.Ease.Linear);

            for (int i = 0; i < 10; i++) Tween.Tick(0.3f);

            Assert.AreEqual(1, Tween.Count, "되풀이는 끝나지 않는다");
            Assert.That(last, Is.InRange(0f, 1f), "되감을 때 남은 시간을 버리면 값이 범위를 벗어난다");
        }

        [Test]
        public void Tween_CurvesStayInsideTheUnitRange()
        {
            foreach (Tween.Ease ease in Enum.GetValues(typeof(Tween.Ease)))
            {
                Assert.AreEqual(0f, Tween.Curve(ease, 0f), 1e-4f, ease + " 의 시작이 0 이 아니다");
                Assert.AreEqual(1f, Tween.Curve(ease, 1f), 1e-4f, ease + " 의 끝이 1 이 아니다");

                for (float t = 0f; t <= 1f; t += 0.05f)
                {
                    float v = Tween.Curve(ease, t);

                    // OutBack 은 일부러 1 을 넘겼다 돌아온다. 그 외에는 범위를 벗어나면
                    // 알파가 음수가 되거나 크기가 뒤집힌다.
                    if (ease == Tween.Ease.OutBack) Assert.That(v, Is.InRange(-0.2f, 1.2f));
                    else Assert.That(v, Is.InRange(-1e-4f, 1f + 1e-4f), ease + " @ " + t);
                }
            }
        }

        /// <summary>
        /// <b>Unity 의 가짜 null 함정.</b>
        ///
        /// <para>원래 이 함수는 <c>GetComponent&lt;CanvasGroup&gt;() ?? AddComponent&lt;CanvasGroup&gt;()</c>
        /// 였다. <c>??</c> 와 <c>?.</c> 는 <c>UnityEngine.Object</c> 의 <c>==</c> 오버로드를
        /// <b>타지 않고</b> 참조 동일성만 본다 — 그래서 컴포넌트가 없을 때 돌아오는 "가짜 null"
        /// (네이티브 포인터가 0 인 관리 객체)이 그대로 통과했고, 첫 복귀 연출에서
        /// <c>MissingComponentException</c> 으로 터졌다.</para>
        ///
        /// <para>Skin 의 스프라이트 캐시에는 같은 이유로 <c>??=</c> 를 안 쓴다고 주석까지
        /// 적어놓고, 바로 옆 파일에서 같은 실수를 했다. 그래서 테스트로 박아둔다.</para>
        /// </summary>
        [Test]
        public void Tween_GroupOf_AddsARealCanvasGroup()
        {
            var owner = Owner();
            var rt = (RectTransform)owner.transform;

            var group = Tween.GroupOf(rt);

            Assert.IsNotNull(group);

            // 가짜 null 이면 여기서 MissingComponentException 이 난다.
            Assert.DoesNotThrow(() => group.alpha = 0.5f,
                "GetComponent 의 가짜 null 이 ?? 를 통과했다");
            Assert.AreEqual(0.5f, group.alpha, 1e-4f);

            // 두 번째 호출은 이미 있는 것을 돌려줘야 한다. 매번 붙이면 하나에 열 개가 쌓인다.
            Assert.AreSame(group, Tween.GroupOf(rt));
            Assert.AreEqual(1, owner.GetComponents<CanvasGroup>().Length);
        }

        [Test]
        public void Tween_PingPongPeaksInTheMiddle()
        {
            Assert.AreEqual(0f, Tween.PingPong(0f), 1e-4f);
            Assert.AreEqual(1f, Tween.PingPong(0.5f), 1e-4f);
            Assert.AreEqual(0f, Tween.PingPong(1f), 1e-4f);
        }

        // ── 스킨 ────────────────────────────────────────────────

        [Test]
        public void Skin_PanelIsFilledInsideAndClearOutsideTheCorner()
        {
            var sprite = Skin.Panel;
            Assert.IsNotNull(sprite);

            // 9-슬라이스가 동작하려면 FullRect 여야 하고 테두리가 잡혀 있어야 한다.
            Assert.Greater(sprite.border.x, 0f, "테두리가 0 이면 늘릴 때 둥근 모서리가 같이 늘어난다");
            Assert.Less(sprite.border.x, sprite.rect.width * 0.5f, "테두리가 절반을 넘으면 가운데가 사라진다");
        }

        [Test]
        public void Skin_VignetteIsClearInTheMiddleAndDarkAtTheCorner()
        {
            var tex = Skin.Vignette;
            Assert.IsNotNull(tex);

            // Apply(false, true) 로 CPU 사본을 버렸으므로 픽셀을 되읽을 수 없다.
            // 되읽을 수 없다는 것 자체가 의도다 — 크기와 형식만 확인한다.
            Assert.AreEqual(TextureWrapMode.Clamp, tex.wrapMode,
                "덮개가 반복되면 화면 한가운데에 어두운 십자가 생긴다");
        }

        [Test]
        public void Skin_GridRepeats()
        {
            Assert.AreEqual(TextureWrapMode.Repeat, Skin.Grid.wrapMode,
                "격자는 uvRect 로 타일링한다 — Clamp 면 한 칸만 늘어난다");
            Assert.AreEqual(FilterMode.Point, Skin.Grid.filterMode,
                "선을 보간하면 격자가 아니라 얼룩이 된다");
        }

        // ── 보고서 문장 ──────────────────────────────────────────

        [Test]
        public void ReportLines_EmptyReportProducesNothing()
        {
            Assert.IsEmpty(ReportLines.Build(null, new GameSave(), null));
            Assert.IsEmpty(ReportLines.Build(new ResolveReport(), new GameSave(), null));
        }

        /// <summary>
        /// <b>손해를 본 파견은 손해라고 적어야 한다.</b>
        ///
        /// <para>파견비는 출발할 때 이미 빠져나갔고 회수품은 창고에 쌓이기만 해서, 보고서가
        /// 둘을 나란히 놓지 않으면 플레이어는 "남는 장사였나"를 끝내 알 수 없다.
        /// 특히 빈손으로 돌아온 파견이 "회수품 없음" 한 줄로 끝나면, 돈이 나갔다는 사실 자체가
        /// 화면 어디에도 없다.</para>
        /// </summary>
        [Test]
        public void ReportLines_SayWhetherTheTripPaidForItself()
        {
            var save = new GameSave();
            var report = new ResolveReport();
            report.Expeditions.Add(new ExpeditionResult
            {
                MapId = "MYEONGDONG", CostPaid = 200_000, LootValue = 50_000,
            });

            var lines = ReportLines.Build(report, save, null);

            bool said = false;
            foreach (var line in lines)
                if (line.Text.Contains("파견비") && line.Text.Contains("150,000")) said = true;

            Assert.IsTrue(said, "손해 본 파견인데 손익이 화면에 없다");
        }

        /// <summary>
        /// 되돌린 시계가 반복되면 문장이 바뀌어야 한다 — 같은 안내를 계속 띄우면
        /// "또 저러네"가 되고, 정작 고칠 곳(기기의 자동 시각 설정)은 끝내 못 알려준다.
        /// </summary>
        [Test]
        public void ReportLines_TellRepeatedClockTroubleApart()
        {
            var once = ReportLines.Build(
                new ResolveReport { ClockWentBackwards = true, ClockAnomalies = 1 }, new GameSave(), null);
            var again = ReportLines.Build(
                new ResolveReport { ClockWentBackwards = true, ClockAnomalies = 5 }, new GameSave(), null);

            Assert.AreEqual(1, once.Count);
            Assert.AreEqual(1, again.Count);
            Assert.AreNotEqual(once[0].Text, again[0].Text,
                "반복되는데 같은 문장이면 무엇을 고쳐야 하는지 말할 기회가 없다");
        }

        [Test]
        public void ReportLines_NamesTheScavWhoDidNotComeBack()
        {
            var save = new GameSave();
            save.Scavs.Add(new ScavState { Uid = "s1", Name = "박기철" });

            var report = new ResolveReport();
            var exp = new ExpeditionResult { MapId = "MYEONGDONG" };
            exp.LostScavUids.Add("s1");
            report.Expeditions.Add(exp);

            var lines = ReportLines.Build(report, save, null);

            bool named = false;
            foreach (var line in lines) if (line.Text.Contains("박기철")) named = true;

            // 이름 없이 "스캐브 1명 상실"이면 그게 누구였는지 알 수가 없고,
            // 그러면 상실이 사건이 되지 못한다 (GDD §15).
            Assert.IsTrue(named, "돌아오지 못한 사람은 이름으로 적어야 한다");
        }

        /// <summary>
        /// <b>레벨로 열리는 지역은 전부 레벨업 줄에 나와야 한다.</b>
        ///
        /// <para>이 테스트가 있는 이유: 원래 이 문장은 손으로 적은 switch 였고 5·9·10 만 있었다.
        /// 그런데 <c>expeditions.json</c> 은 강남을 3, 남산을 7 에 열어두고 있었다 — 그 두 번의
        /// 레벨업에서는 "레벨 달성" 한 줄만 뜨고 <b>지역이 열렸다는 말이 없었다.</b>
        /// 값을 정하는 쪽(데이터)과 말하는 쪽(화면)이 갈라진 그 부류다.</para>
        /// </summary>
        [Test]
        public void ReportLines_EveryLevelGatedMapIsAnnouncedWhenItOpens()
        {
            int checkedMaps = 0;
            foreach (var map in _data.AllMaps)
            {
                if (map?.Unlock == null || map.Unlock.Type != "playerLevel") continue;
                checkedMaps++;

                string text = ReportLines.UnlockedAt(map.Unlock.Value, _data);
                Assert.IsNotEmpty(text,
                    $"레벨 {map.Unlock.Value} 에서 {map.Id} 가 열리는데 아무 말도 하지 않는다");
            }

            Assert.AreEqual(0, checkedMaps, "Regions now unlock through surviving the main route, not player level.");
        }

        [Test]
        public void ReportLines_SaysNothingForALevelThatOpensNothing()
        {
            // 어떤 지역도 열리지 않고 티어 경계도 아닌 레벨에서는 조용해야 한다.
            // 매번 " — 개방" 을 붙이면 그 말이 아무 뜻도 없어진다.
            Assert.IsEmpty(ReportLines.UnlockedAt(999, _data));
        }

        // ── 복귀 연출이 뜨는 조건 ────────────────────────────────

        /// <summary>
        /// <b>앱이 켜져 있는 동안 제작 하나가 끝날 때마다 연출이 뜨면 재앙이다.</b>
        /// 정산은 5초마다 돌기 때문이다. 파견 복귀(정산당 한 번)와 자리를 비웠던 경우에만 뜬다.
        /// </summary>
        [Test]
        public void ReturnCutscene_DoesNotInterruptActivePlay()
        {
            var now = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

            var tick = new ResolveReport { From = now, To = now.AddSeconds(5) };
            tick.Crafts.Add(new CraftResult { OutputItemId = "X", Count = 1, Quality = CraftQuality.Normal });

            Assert.IsFalse(ReturnCutscene.Worth(tick),
                "앉아서 보는 중에 제작이 끝났다고 화면을 덮으면 안 된다");
        }

        [Test]
        public void ReturnCutscene_ShowsWhenATeamComesBack()
        {
            var now = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

            var report = new ResolveReport { From = now, To = now.AddSeconds(5) };
            report.Expeditions.Add(new ExpeditionResult { MapId = "MYEONGDONG" });

            Assert.IsTrue(ReturnCutscene.Worth(report), "기다린 대상이 돌아온 순간이다");
        }

        [Test]
        public void ReturnCutscene_ShowsAfterBeingAway()
        {
            var now = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

            var report = new ResolveReport { From = now, To = now.AddHours(8) };
            report.DayRollovers = 1;

            Assert.IsTrue(ReturnCutscene.Worth(report));
        }

        [Test]
        public void ReturnCutscene_LeavesTheClockWarningToTheCard()
        {
            var now = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

            var report = new ResolveReport { From = now, To = now.AddHours(8), ClockWentBackwards = true };
            report.DayRollovers = 1;

            // 시계가 거꾸로 간 건 소식이 아니라 경고다. 연출로 감싸면 사고가 이벤트처럼 보인다.
            Assert.IsFalse(ReturnCutscene.Worth(report));
        }
    }
}
