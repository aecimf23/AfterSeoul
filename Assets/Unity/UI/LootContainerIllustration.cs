using UnityEngine;
namespace AfterSeoul.Unity.UI
{
    internal static class LootContainerIllustration
    {
        internal static void Draw(Transform parent, string kind)
        {
            int index = kind == "Tool" ? 0 : kind == "Medical" ? 1 : kind == "Weapon" ? 2 : kind == "Ammo" ? 3 : kind == "Safe" ? 5 : 4;
            var art = GameArt.Draw("ContainerIllustration", parent, GameArt.Cell("containers", index, 3, 2));
            Ui.Size(art.gameObject, 280);
        }
    }
}
