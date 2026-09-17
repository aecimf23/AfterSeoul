using System;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>The resident's saved four-page prologue, before choosing who to seek.</summary>
    internal sealed class WelcomeBriefing
    {
        internal const int PageCount = 4;
        private readonly GameSession _session;
        private readonly bool _replay;
        private readonly Action _done;
        private readonly RectTransform _root, _body;
        private int _page;
        private bool _closed;
        private string _saveError;
        private static readonly string[] Titles = { "내가 살던 서울", "이곳에 남기로 했다", "혼자서는 버틸 수 없다", "누군가를 찾아가야 한다" };
        private static readonly string[] Stories = {
            "익숙한 거리의 불빛이 하나둘 꺼졌다. 연락은 끊기고, 서울의 일상은 무너졌다. 나는 이 도시에서 살아온 평범한 주민이다.",
            "도시가 다시 안정을 찾을 때까지, 나는 서울에 남기로 했다. 오늘을 버티고 내일도 살아남는 것. 지금은 그것부터다.",
            "남은 물과 식량은 얼마 없다. 약도, 고장 난 물건을 고칠 공구도 필요하다. 혼자 모을 수 있는 물자에는 한계가 있다.",
            "골목에서 만난 약탈자들에게 소문을 들었다. 군수 창고의 황 상사, 진료소의 최씨, 용산의 김씨. 이 도시에서 힘을 가진 사람들이다. 누구를 먼저 찾아갈까?"
        };

        internal WelcomeBriefing(Transform parent, GameSession session, bool replay, Action done)
        {
            _session = session; _replay = replay; _done = done;
            _page = replay ? 0 : Mathf.Clamp(session.Save.WelcomePage, 0, PageCount - 1);
            _root = Ui.Modal("WelcomeBriefing", parent, Loc.Text("서울에 남은 이유"), () => Finish(), out _body);
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
            _body.anchoredPosition = new Vector2(_body.anchoredPosition.x, 0);
            if (_saveError != null) {
                var error = Ui.Paragraph("SaveError", _body, _saveError, 27, Theme.Danger);
                Ui.Size(error.gameObject, 110);
            }
            var progress = Ui.Label("Progress", _body, $"{_page + 1} / {PageCount}", 27, TextAnchor.MiddleRight, Theme.Info);
            Ui.Size(progress.gameObject, 42);
            var art = new StoryIllustration(_body, _page);
            Ui.Size(art.Root.gameObject, 320);
            var heading = Ui.Paragraph("Heading", _body, Loc.Text(Titles[_page]), 38, Theme.Text);
            Ui.Size(heading.gameObject, 90);
            var text = Ui.Paragraph("Explanation", _body, Loc.Text(Stories[_page]), 30, Theme.TextDim);
            Ui.Size(text.gameObject, 260);
            var next = Ui.Button("Next", _body, _page == PageCount - 1 ? Loc.Text("찾아갈 사람 정하기") : Loc.Get("GUIDE_NEXT"), () => {
                if (_closed) return;
                if (_page == PageCount - 1) { Finish(); return; }
                if (!Persist(_page + 1)) return;
                _saveError = null; _page++; Draw();
            }, Theme.AccentDim, 30);
            Ui.Size(next.gameObject, 96);
            if (_page > 0) {
                var back = Ui.Button("Back", _body, Loc.Text("이전 이야기"), () => {
                    if (_closed || !Persist(_page - 1)) return;
                    _saveError = null; _page--; Draw();
                }, Theme.PanelAlt, 26);
                Ui.Size(back.gameObject, 78);
            }
            var skip = Ui.Button("Skip", _body, Loc.Get("GUIDE_SKIP"), Finish, Theme.PanelAlt, 26);
            Ui.Size(skip.gameObject, 78);
        }

        private void Finish()
        {
            if (_closed || !Persist(-1)) return;
            _closed = true;
            _root.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
            else UnityEngine.Object.DestroyImmediate(_root.gameObject);
            _done();
        }
    }
}
