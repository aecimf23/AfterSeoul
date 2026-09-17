using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>Shared framing and a single progress meter.</summary>
    internal static class MissionUi
    {
        internal static RectTransform Panel(RectTransform host, string title, string subtitle)
        {
            var panel = Ui.Surface("MissionPanel", host, Theme.Bg, Theme.AccentDim);
            var heading = Ui.Label("MissionTitle", panel, title, Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Top(heading.rectTransform, 40, 18);
            var note = Ui.Label("MissionSubtitle", panel, subtitle, 21, TextAnchor.MiddleLeft, Theme.TextDim);
            Band(note.rectTransform, 42, 30);
            return panel;
        }
        internal static void Band(RectTransform rt, float top, float height, float inset = 18)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(.5f, 1);
            rt.offsetMin = new Vector2(inset, -top - height);
            rt.offsetMax = new Vector2(-inset, -top);
        }
        internal static Image Meter(RectTransform host, float top, string name, Color color)
        {
            var track = Ui.Surface(name, host, Theme.PanelAlt, Theme.Line); Band(track, top, 8);
            var fill = Ui.Rect("Fill", track); var image = fill.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false; return image;
        }
        internal static void Fill(Image fill, float value)
        {
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        }
    }
}
