using UnityEngine;
namespace AfterSeoul.Unity.UI
{
    internal sealed class StoryIllustration
    {
        internal RectTransform Root { get; }
        internal StoryIllustration(Transform parent, int page)
        {
            Root = Ui.Rect("StoryIllustration", parent);
            var art = GameArt.Draw("StoryArtwork", Root, GameArt.World(11 + Mathf.Clamp(page, 0, 3)), false);
            Ui.Stretch(art.rectTransform);
        }
    }
}
