using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;

namespace AfterSeoul.Tests
{
    public class ItemArtworkTests
    {
        [Test] public void AllArtworkCellsHaveTransparentCornersAndVisibleItems()
        {
            var texture = new Texture2D(2, 2);
            try {
                Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath, "Resources", ItemArtwork.ResourcePath + ".png"))));
                Assert.AreEqual(0, texture.width % 6); Assert.AreEqual(0, texture.height % 4);
                Assert.AreEqual(texture.width / 6, texture.height / 4);
                foreach (ItemArtworkKind kind in Enum.GetValues(typeof(ItemArtworkKind))) {
                    var sprite = ItemArtwork.For(kind); Assert.IsNotNull(sprite, kind.ToString());
                    Assert.AreSame(sprite, ItemArtwork.For(kind), "Repeated rows should reuse cached sprites");
                    var rect = sprite.rect;
                    var pixels = texture.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height);
                    Assert.Greater(pixels.Count(c => c.a > .5f), pixels.Length / 12, "Missing item: " + kind);
                    Assert.Less(pixels.Count(c => c.a > .5f), pixels.Length * .9, "Opaque background: " + kind);
                    Assert.Less(pixels[0].a, .1f, "Corner bleed: " + kind);
                    Assert.Less(pixels[pixels.Length - 1].a, .1f, "Corner bleed: " + kind);
                }
            } finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test] public void RepresentativeItemsGetTheirOwnShapeAndEveryCatalogItemHasArtwork()
        {
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            Assert.AreEqual(ItemArtworkKind.Pistol, ItemArtwork.KindOf(data.GetItem("WPN04")));
            Assert.AreEqual(ItemArtworkKind.Rifle, ItemArtwork.KindOf(data.GetItem("WPN01")));
            Assert.AreEqual(ItemArtworkKind.Knife, ItemArtwork.KindOf(data.GetItem("MEL01")));
            Assert.AreEqual(ItemArtworkKind.Bandage, ItemArtwork.KindOf(data.GetItem("MED16")));
            Assert.AreEqual(ItemArtworkKind.WaterBottle, ItemArtwork.KindOf(data.GetItem("FOOD05")));
            Assert.AreEqual(ItemArtworkKind.Valuables, ItemArtwork.KindOf(data.GetItem("JUNK14")));
            foreach (var item in ((IDataRegistry)data).AllItems)
                Assert.IsNotNull(ItemArtwork.For(ItemArtwork.KindOf(item)), item.Id);
        }
    }
}
