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
        private static readonly string[] Art = {
            "    .      /\\      .\n __||__  _/  \\_  __||__\n| [] []||  SEOUL ||[] [] |\n|______||_______||______|",
            "+--- WORKBENCH ---+\n| [SIGNAL] [VAULT]|\n|   > > >  [BOX] |\n+----------------+",
            "+-- WAREHOUSE --+\n| [BOX] [BOX]   |\n|      |       |\n|      v   WON |\n+--------------+",
            "    O       O\n   /|\\     /|\\\n   / \\     / \\\n [TEAM] + [GEAR]",
            "[BASE] -- -- > [MYEONGDONG]\n          03:00\n[BASE] < -- -- [SUPPLIES]",
            "[SUPPLIES] --> [EMPLOYER]\n                 |\n                 v\n             50,000 WON"
        };

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
            if (_saveError != null) {
                var error = Ui.Paragraph("SaveError", _body, _saveError, 27, Theme.Danger);
                Ui.Size(error.gameObject, 110);
            }
            var progress = Ui.Label("Progress", _body, $"{_page + 1} / {PageCount}", 27, TextAnchor.MiddleRight, Theme.Info);
            Ui.Size(progress.gameObject, 42);
            var art = Ui.Label("Art", _body, Art[_page], 30, TextAnchor.MiddleCenter, Theme.Accent);
            art.font = Theme.ArtFont;
            art.resizeTextForBestFit = true; art.resizeTextMinSize = 16; art.resizeTextMaxSize = 30;
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
            UnityEngine.Object.Destroy(_root.gameObject);
            _done();
        }
    }
}
