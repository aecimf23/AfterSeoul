using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>A shared city/location scene for selection and the persistent employer header.</summary>
    internal sealed class EmployerSceneView
    {
        internal RectTransform Root { get; }
        private readonly Text _location, _background, _portrait;
        private readonly RectTransform _portraitFrame;
        private string _shown;
        private bool _initialized;

        internal EmployerSceneView(Transform parent)
        {
            Root = Ui.Surface("EmployerScene", parent, Theme.Bg, Theme.AccentDim);
            _location = Ui.Label("Location", Root, "", 22, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Top(_location.rectTransform, 32, 16);
            _background = Ui.Label("LocationAscii", Root, "", 23, TextAnchor.MiddleCenter, Theme.TextDim);
            Ui.Stretch(_background.rectTransform, 12, 12, 36, 8);
            _background.font = Theme.ArtFont;
            _location.resizeTextForBestFit = true; _location.resizeTextMinSize = 16; _location.resizeTextMaxSize = 22;
            _background.lineSpacing = .88f;
            _background.resizeTextForBestFit = true;
            _background.resizeTextMinSize = 12; _background.resizeTextMaxSize = 23;
            _background.verticalOverflow = VerticalWrapMode.Truncate;
            _portraitFrame = Ui.Surface("PortraitFrame", Root, Theme.Panel, Theme.AccentDim);
            _portraitFrame.anchorMin = new Vector2(.76f, 0);
            _portraitFrame.anchorMax = new Vector2(1, 1);
            _portraitFrame.offsetMin = new Vector2(4, 10);
            _portraitFrame.offsetMax = new Vector2(-12, -10);
            _portrait = Ui.Label("EmployerAscii", _portraitFrame, "", 23, TextAnchor.MiddleCenter, Theme.Accent);
            Ui.Stretch(_portrait.rectTransform, 6, 6, 6, 6);
            _portrait.font = Theme.ArtFont;
            _portrait.lineSpacing = .9f;
            _portrait.resizeTextForBestFit = true;
            _portrait.resizeTextMinSize = 12; _portrait.resizeTextMaxSize = 23;
        }

        internal void SetEmployer(string id)
        {
            if (_initialized && _shown == id) return;
            _initialized = true; _shown = id;
            bool chosen = id == "HWANG" || id == "DR_CHOI" || id == "YONGSAN_KIM";
            _portraitFrame.gameObject.SetActive(chosen);
            _background.rectTransform.anchorMax = new Vector2(chosen ? .75f : 1, 1);
            _location.rectTransform.anchorMax = new Vector2(chosen ? .75f : 1, 1);
            _portrait.text = chosen ? EmployerPortrait.Art(id) : "";
            _background.text = Background(id);
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

        internal static string Background(string id)
        {
            switch (id)
            {
                case "HWANG": return @"    ______________________________
   /_____________________________/|
  | [====] [====] |  SUPPLY  |    |
  | [____] [____] |__________|    |
  |    ___    ___     ___         |
  |   /__/|  /__/|   /__/|        |
  |___|__|/__|__|/___|__|/________|
      [ AMMO ]       [ RATIONS ]";
                case "DR_CHOI": return @"    ______________________________
   |  [+]     FIELD CLINIC       |
   |______   ____________________|
   | [++] | |   o        o       |
   | [++] | | _/|_____ _/|_____   |
   |______| | |______| |______|   |
   |  ()    | |      | |      |   |
   |________|_____________________|";
                case "YONGSAN_KIM": return @"        /          /          /
   ____/__________/__________/___
  |  .--------.    .--------.    |
  |  | >_ ... |    | SIGNAL |    |
  |  '--------'    '--------'    |
  |____[::::]________[::::]______|
  |  [PCB]   /==/    (O) [___]  |
  |_________/_/_________________|";
                default: return @"              .          |       .
       ___             __|__
   ___|:::|___    ____ |[] []| ___
  | []|:::|[] |__| [] ||[] []||[] |
  |___|___|___|__|____||_____||___|
      .      ____        .
  __________| ## |________________
             /  \
  __________/____\_______________";
            }
        }
    }
}
