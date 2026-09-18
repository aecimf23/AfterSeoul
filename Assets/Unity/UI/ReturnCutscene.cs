using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 돌아온 순간의 연출 (GDD §29).
    ///
    /// <para><b>왜 필요한가:</b> 방치형에서 플레이어가 실제로 기다린 대상은 보고서다.
    /// 그런데 그게 홈 화면 카드 안에 조용히 들어 있으면, 몇 시간을 기다린 결과가
    /// "앱을 켜니 글자가 이미 적혀 있더라"가 된다. 결과가 <b>도착하는 걸 보는 것</b>과
    /// 이미 도착해 있는 걸 읽는 것은 다른 경험이다.</para>
    ///
    /// <para><b>그런데 연출이 길면 그게 곧 벌이다.</b> 매일 켜는 게임이라 이 화면은
    /// 수백 번 본다. 그래서 전체 0.1초 간격, 아무리 길어도 1.6초 안에 다 뜨고,
    /// <b>아무 데나 누르면 즉시 전부</b> 나온다. 건너뛰기를 숨겨두지 않는다.</para>
    ///
    /// <para>문장은 만들지 않는다 — <see cref="ReportLines"/> 가 만든 것을 그대로 받는다.
    /// 홈의 "복귀 보고" 카드와 글자가 갈라지면 안 된다.</para>
    /// </summary>
    public sealed class ReturnCutscene
    {
        /// <summary>줄 사이 간격의 상한. 이보다 촘촘하면 한 덩어리로 보여서 연출이 무의미해진다.</summary>
        private const float MaxInterval = 0.1f;

        /// <summary>전부 뜨는 데 걸리는 최대 시간. 줄이 많으면 간격을 줄여 이 안에 맞춘다.</summary>
        private const float MaxTotal = 1.6f;

        private readonly RectTransform _root;
        private readonly List<CanvasGroup> _lines = new List<CanvasGroup>();
        private readonly Action _onClosed;

        private bool _revealed;
        private bool _closing;

        /// <summary>
        /// 이 보고가 화면을 덮을 값어치가 있는가.
        ///
        /// <para>제작 하나가 끝날 때마다 뜨면 재앙이다 — 앱이 켜져 있는 동안 5초마다 정산이
        /// 돌기 때문이다. 그래서 <b>파견이 돌아왔거나</b>(파견은 정산당 한 번뿐이다)
        /// <b>한동안 자리를 비웠을 때</b>만 띄운다.</para>
        /// </summary>
        public static bool Worth(ResolveReport report)
        {
            if (report == null || report.IsEmpty) return false;

            // 시계가 거꾸로 간 건 소식이 아니라 경고다. 연출로 감쌀 일이 아니다.
            if (report.ClockWentBackwards) return false;

            if (report.Expeditions.Count > 0) return true;
            if (report.RescueSignals.Count > 0) return true;
            if (report.Overflowed.Count > 0) return true;

            return report.OfflineDuration.TotalMinutes >= 3.0;
        }

        public ReturnCutscene(Transform parent, ResolveReport report, GameSession session, Action onClosed)
        {
            _onClosed = onClosed;

            var lines = ReportLines.Build(report, session.Save, session.Data);

            _root = Ui.Rect("ReturnCutscene", parent);
            Ui.Stretch(_root);

            var scrim = _root.gameObject.AddComponent<Image>();
            scrim.color = Theme.Scrim;
            scrim.raycastTarget = true;

            // 바깥을 누르면 닫는 게 아니라 <b>전부 펼친다</b>. 기다리던 소식을 실수로
            // 건드려 날려버리는 일이 있으면 안 된다 — 닫기는 아래 버튼 하나뿐이다.
            var skip = _root.gameObject.AddComponent<Button>();
            skip.targetGraphic = scrim;
            skip.transition = Selectable.Transition.None;
            skip.onClick.AddListener(RevealAll);

            var panel = BuildPanel(lines.Count);
            var body = BuildHead(panel, report);

            float interval = lines.Count <= 1
                ? MaxInterval
                : Mathf.Min(MaxInterval, MaxTotal / lines.Count);

            for (int i = 0; i < lines.Count; i++) BuildLine(body, lines[i], i * interval);

            BuildFooter(panel);

            // 배경이 먼저 어두워지고 판이 올라온다. 반대로 하면 판이 허공에 떠 있다가
            // 뒤늦게 배경이 깔려서 두 번 움직이는 것처럼 보인다.
            var group = Tween.GroupOf(_root);
            group.alpha = 0f;
            Tween.Play(group, "alpha", 0.16f, t => group.alpha = t, Tween.Ease.OutQuad);
            Tween.SlideIn(panel, new Vector2(0f, -40f), 0.28f);

            Sfx.Tap();
        }

        private RectTransform BuildPanel(int lineCount)
        {
            var safe = Ui.Rect("SafeArea", _root);
            Ui.Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();
            var panel = Ui.Rect("Panel", safe);

            // 줄이 적으면 판도 작게. 세 줄짜리 보고를 화면 전체로 띄우면 과장으로 보인다.
            float height = Mathf.Clamp(360f + lineCount * 52f, 520f, 1320f);
            panel.anchorMin = new Vector2(0f, 0.5f);
            panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(-Theme.Gutter * 2f, height);
            panel.anchoredPosition = Vector2.zero;

            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = Skin.Panel;
            bg.type = Image.Type.Sliced;
            bg.color = Theme.Bg;
            // 판을 눌러도 펼쳐지게 둔다. "아무 데나"가 정말 아무 데나여야 한다.
            bg.raycastTarget = false;

            Ui.SetEdge(panel, Theme.EdgeLive);
            Ui.Brackets(panel, Theme.Accent, 38f, 12f);
            Ui.Column(panel, 12f, new RectOffset(26, 26, 22, 20));
            return panel;
        }

        private RectTransform BuildHead(RectTransform panel, ResolveReport report)
        {
            var title = Ui.Label("Title", panel, AfterSeoul.Core.Loc.Text("복귀 보고"), Theme.FontTitle,
                TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Size(title.gameObject, 62f);

            var stamp = Ui.Label("Stamp", panel,
                report.To.ToOffset(GameTime.GameZoneOffset).ToString("yyyy-MM-dd  HH:mm") + "  KST",
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextFaint);
            Ui.Size(stamp.gameObject, 34f);

            // 제목 아래를 훑고 지나가는 선. 줄이 다 뜨는 동안 같이 차서
            // "아직 더 나온다 / 다 나왔다"를 글자를 안 세고도 알 수 있다.
            var rule = ProgressBar.Create(panel, 3f, Theme.Accent);
            rule.Set(0f);
            Tween.Play(rule.Root, "wipe", MaxTotal * 0.9f, t => rule.Set(t), Tween.Ease.OutQuad);
            _rule = rule;

            // 보고가 길면 — 밤새 두 팀이 돌아오고 창고가 넘쳤다면 — 스무 줄이 넘는다.
            // 고정 높이 안에 밀어 넣으면 아래쪽이 판 밖으로 새어 나간다.
            ScrollRect scroll;
            var body = Ui.ScrollList("Lines", panel, out scroll, 6f);
            Ui.Size(scroll.gameObject, flexHeight: 1f);
            return body;
        }

        private ProgressBar _rule;

        private void BuildLine(RectTransform body, ReportLines.Line line, float delay)
        {
            var text = Ui.Paragraph("L", body,
                (line.Indent > 0 ? "    " : "") + line.Text, Theme.FontSmall, line.Color);

            // 높이를 고정하지 않는다. Column 의 childControlHeight 가 Text 의 preferredHeight 를
            // 그대로 쓰므로, 두 줄로 접히는 문장도 다음 줄과 겹치지 않는다.
            var group = text.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            _lines.Add(group);

            Tween.Play(group, "alpha", 0.22f, t => group.alpha = t, Tween.Ease.OutQuad, delay);

            // 소리는 제목 격인 줄에만. 전리품 열두 줄이 전부 딸깍거리면 그건 연출이 아니라 소음이다.
            if (line.Notable)
                Tween.Play(group, "tick", 0f, _ => { }, Tween.Ease.Linear, delay, Sfx.Step);
        }

        private void BuildFooter(RectTransform panel)
        {
            var close = Ui.Button("Close", panel, AfterSeoul.Core.Loc.Text("확인"), Close, Theme.AccentDim);
            Ui.Size(close.gameObject, 92f);
        }

        /// <summary>기다리지 않고 전부 본다. 이미 다 떴으면 아무 일도 안 한다.</summary>
        public void RevealAll()
        {
            if (_revealed || _closing) return;
            _revealed = true;

            foreach (var group in _lines)
            {
                if (group == null) continue;
                Tween.CancelAll(group);
                group.alpha = 1f;
            }

            if (_rule != null && _rule.Alive)
            {
                Tween.Cancel(_rule.Root, "wipe");
                _rule.Set(1f);
            }
        }

        private void Close()
        {
            if (_closing) return;
            _closing = true;

            Sfx.Tap();
            Tween.FadeOut(_root, 0.16f, () =>
            {
                if (_root != null)
                {
                    // 먼저 계층에서 떼고 파괴한다 — 한 프레임 겹쳐 그려지는 걸 막는다.
                    _root.SetParent(null, false);
                    UnityEngine.Object.Destroy(_root.gameObject);
                }
                _onClosed?.Invoke();
            });
        }
    }
}
