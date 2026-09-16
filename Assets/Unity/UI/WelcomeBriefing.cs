using System;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>A saved introduction to the real starter loop; it grants no extra rewards.</summary>
    internal sealed class WelcomeBriefing
    {
        internal const int PageCount = 6;
        private readonly GameSession _session;
        private readonly bool _replay;
        private readonly Action _done;
        private readonly RectTransform _root, _body;
        private int _page;
        private bool _closed;
        private string _saveError;

        internal WelcomeBriefing(Transform parent, GameSession session, bool replay, Action done)
        {
            _session = session; _replay = replay; _done = done;
            _page = replay ? 0 : Mathf.Clamp(session.Save.WelcomePage, 0, PageCount - 1);
            _root = Ui.Modal("WelcomeBriefing", parent, Loc.Get("GUIDE_TITLE"), () => Finish(), out _body);
            _root.gameObject.AddComponent<SafeArea>();
            Draw();
        }

        private bool Persist(int page)
        {
            if (_replay) return true;
            int before = _session.Save.WelcomePage;
            try { _session.Save.WelcomePage = page; _session.Commit(); return true; }
            catch (Exception) {
                _session.Save.WelcomePage = before;
                _saveError = Loc.Get("GUIDE_SAVE_ERROR");
                Draw();
                return false;
            }
        }

        private void Draw()
        {
            Ui.Clear(_body);
            if (!_replay && StarterSupport.Active(_session.Save)) {
                if (_saveError != null) {
                    var error = Ui.Paragraph("SaveError", _body, _saveError, 27, Theme.Danger);
                    Ui.Size(error.gameObject, 110);
                }
                var starterHeading = Ui.Paragraph("StarterTitle", _body, Loc.Text("첫 동료는 고용주가 지원합니다"), 38, Theme.Text);
                Ui.Size(starterHeading.gameObject, 116);
                var firstWork=GameArt.Place("StarterWorkImage",_body,GameArt.Worker(1));
                Ui.Size(firstWork.gameObject,220);
                var starterText = Ui.Paragraph("StarterExplanation", _body,
                    Loc.Text("총기 한 정 제작 → 용산킴에게 자동 납품 → 첫 동료 선택\n\n재료는 무료이며 작업비를 받습니다. 첫 1티어 동료의 계약금은 전액 지원되며, 이어서 3분 무료 안전 파견을 체험합니다."), 30, Theme.TextDim);
                Ui.Size(starterText.gameObject, 320);
                var start = Ui.Button("StartPractice", _body, Loc.Text("직접 해보기"), Finish, Theme.Accent, 30);
                Ui.Size(start.gameObject, 96);
                return;
            }
            if (_saveError != null) {
                var error = Ui.Paragraph("SaveError", _body, _saveError, 27, Theme.Danger);
                Ui.Size(error.gameObject, 110);
            }
            var progress = Ui.Label("Progress", _body, $"{_page + 1} / {PageCount}", 27, TextAnchor.MiddleRight, Theme.Info);
            Ui.Size(progress.gameObject, 42);
            Sprite picture = _page == 0 || _page == 4 ? GameArt.City() : _page == 1 ? GameArt.Location("YONGSAN_KIM")
                : _page == 2 ? GameArt.Location("HWANG") : _page == 3 ? GameArt.Worker(0, 0) : GameArt.Portrait(_session.Save.Player.EmployerNpcId);
            var art = GameArt.Place("Art", _body, picture);
            Ui.Size(art.gameObject, 250);
            var heading = Ui.Paragraph("Heading", _body, Loc.Get("GUIDE_" + _page + "_TITLE"), 38, Theme.Text);
            Ui.Size(heading.gameObject, 116);
            var text = Ui.Paragraph("Explanation", _body, Loc.Get("GUIDE_" + _page + "_BODY"), 30, Theme.TextDim);
            Ui.Size(text.gameObject, 320);
            var tip = Ui.Paragraph("Tip", _body, Loc.Get("GUIDE_" + _page + "_TIP"), 26, Theme.Info);
            Ui.Size(tip.gameObject, 160);
            var next = Ui.Button("Next", _body, Loc.Get(_page == PageCount - 1 ? "GUIDE_START" : "GUIDE_NEXT"), () => {
                if (_closed) return;
                if (_page == PageCount - 1) { Finish(); return; }
                if (!Persist(_page + 1)) return;
                _saveError = null; _page++; Draw();
            }, Theme.AccentDim, 30);
            Ui.Size(next.gameObject, 96);
            var skip = Ui.Button("Skip", _body, Loc.Get("GUIDE_SKIP"), Finish, Theme.PanelAlt, 26);
            Ui.Size(skip.gameObject, 78);
        }

        private void Finish()
        {
            if (_closed || !Persist(-1)) return;
            _closed = true;
            _root.gameObject.SetActive(false);
            if(Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
            else UnityEngine.Object.DestroyImmediate(_root.gameObject);
            _done();
        }
    }
}
