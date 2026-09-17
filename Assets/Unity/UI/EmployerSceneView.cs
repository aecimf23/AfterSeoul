using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>A shared city/location scene for selection and the persistent employer header.</summary>
    internal sealed class EmployerSceneView
    {
        internal RectTransform Root { get; }
        private readonly Text _location;
        private readonly Image _background, _portrait;
        private readonly RectTransform _portraitFrame;
        private string _shown;
        private bool _initialized;

        internal EmployerSceneView(Transform parent)
        {
            Root = Ui.Surface("EmployerScene", parent, Theme.Bg, Theme.AccentDim);
            _location = Ui.Label("Location", Root, "", 22, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Top(_location.rectTransform, 32, 16);
            _background = GameArt.Draw("LocationArtwork", Root, GameArt.World(11), false);
            Ui.Stretch(_background.rectTransform, 12, 12, 36, 8);
            _location.resizeTextForBestFit = true; _location.resizeTextMinSize = 16; _location.resizeTextMaxSize = 22;
            _portraitFrame = Ui.Surface("PortraitFrame", Root, Theme.Panel, Theme.AccentDim);
            _portraitFrame.anchorMin = new Vector2(.76f, 0);
            _portraitFrame.anchorMax = new Vector2(1, 1);
            _portraitFrame.offsetMin = new Vector2(4, 10);
            _portraitFrame.offsetMax = new Vector2(-12, -10);
            _portrait = GameArt.Draw("EmployerPortrait", _portraitFrame, null);
            Ui.Stretch(_portrait.rectTransform, 4, 4, 4, 4);
        }

        internal void SetEmployer(string id)
        {
            if (_initialized && _shown == id) return;
            _initialized = true; _shown = id;
            bool chosen = id == "HWANG" || id == "DR_CHOI" || id == "YONGSAN_KIM";
            _portraitFrame.gameObject.SetActive(chosen);
            _background.rectTransform.anchorMax = new Vector2(chosen ? .75f : 1, 1);
            _location.rectTransform.anchorMax = new Vector2(chosen ? .75f : 1, 1);
            _portrait.sprite = chosen ? GameArt.Cell("npcs", GameArt.NpcIndex(id), 3, 1) : null;
            _background.sprite = GameArt.World(chosen ? 8 + GameArt.NpcIndex(id) : 11);
            _location.text = Location(id);
        }

        internal static string Location(string id)
        {
            switch (id)
            {
                case "HWANG": return Loc.Text("군수 창고 / SUPPLY DEPOT");
                case "DR_CHOI": return Loc.Text("작은 진료소 / FIELD CLINIC");
                case "YONGSAN_KIM": return Loc.Text("용산 작업실 / ELECTRONICS");
                default: return Loc.Text("서울 / 아직 불빛이 남아 있는 도시");
            }
        }

    }
}
