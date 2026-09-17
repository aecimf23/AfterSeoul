using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Unity.UI;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class ExpandedLootTests
    {
        private IDataRegistry Data() => JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));

        [Test] public void ContainersOfferBroadUsefulLootWithoutQuestOrModeSpecificObjects()
        {
            var data = Data(); var random = new System.Random(81); var found = new HashSet<string>();
            foreach (var kind in LootContainers.Kinds)
                for (int i = 0; i < 4000; i++)
                    foreach (var stack in LootContainers.RollChoices(data, kind, random.Next)) {
                        found.Add(stack.ItemId);
                        Assert.IsFalse(stack.ItemId.StartsWith("QUEST_") || stack.ItemId.StartsWith("UIJ_") || stack.ItemId.StartsWith("KEY"), stack.ItemId);
                        Assert.Greater(stack.Count, 0);
                        Assert.IsNotNull(data.GetItem(stack.ItemId));
                    }
            Assert.GreaterOrEqual(found.Count, 100);
            TestContext.WriteLine("Obtainable item kinds in seeded sampling: " + found.Count);
            foreach (var id in new[] { "AMR01", "HDW01", "BPK01", "EAR01", "WPN09", "MED03", "FOOD04", "JUNK01" })
                Assert.IsTrue(found.Contains(id), "Not obtainable: " + id);
        }

        [Test] public void DifferentRiflesHaveIndividualArtwork()
        {
            var data = Data(); var root = new GameObject("IconTest");
            try {
                var left = Ui.Icon("Left", root.transform, data.GetItem("WPN01"));
                var right = Ui.Icon("Right", root.transform, data.GetItem("WPN02"));
                Assert.AreNotSame(left.sprite, right.sprite);
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test] public void EveryRegisteredItemHasItsOwnSavedArtworkCell()
        {
            var data = Data(); var cells = new HashSet<string>();
            foreach (var item in data.AllItems) {
                var sprite = ItemArtwork.For(item);
                Assert.AreEqual("Item_" + item.Id, sprite.name, "Missing individual artwork: " + item.Id);
                Assert.IsTrue(cells.Add(sprite.texture.name + ":" + sprite.rect), "Shared cell: " + item.Id);
                Assert.AreSame(sprite, ItemArtwork.For(item));
            }
            Assert.GreaterOrEqual(cells.Count, 462);
        }

        [Test] public void EveryIndividualCellContainsVisibleArtworkOnTransparentBackground()
        {
            var textures = new Dictionary<string, Texture2D>();
            try {
                foreach (var item in Data().AllItems) {
                    var sprite = ItemArtwork.For(item);
                    Assert.AreEqual("Item_" + item.Id, sprite.name);
                    string name = sprite.texture.name;
                    if (!textures.TryGetValue(name, out var readable)) {
                        readable = new Texture2D(2, 2);
                        Assert.IsTrue(readable.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath, "Resources/ItemIcons", name + ".png"))));
                        textures.Add(name, readable);
                    }
                    var r = sprite.rect;
                    int visible = 0, clear = 0, samples = 0;
                    for (int y = (int)r.y; y < r.yMax; y += 4)
                        for (int x = (int)r.x; x < r.xMax; x += 4) {
                            float alpha = readable.GetPixel(x, y).a; samples++;
                            if (alpha > .5f) visible++;
                            if (alpha < .1f) clear++;
                        }
                    Assert.Greater(visible, samples / 40, "Empty cell: " + item.Id);
                    Assert.Greater(clear, samples / 10, "Opaque background: " + item.Id);
                }
            } finally { foreach (var texture in textures.Values) UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
