using System.IO;
using NUnit.Framework;
using AfterSeoul.Unity.UI;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class ToonPresentationTests
    {
        [Test] public void CharacterSceneAndContainerSheetsAreBundled()
        {
            foreach (var sheet in new[] { "npcs", "world", "actors", "containers" })
                Assert.IsNotNull(Resources.Load<Texture2D>("ToonArt/" + sheet), sheet);
            for (int i = 0; i < 16; i++) Assert.IsNotNull(GameArt.World(i));
            Assert.AreNotSame(GameArt.World(GameArt.MapIndex("YONGSAN_MARKET")), GameArt.World(GameArt.MapIndex("GURO_FACTORY")));
        }

        [Test] public void VisiblePresentationHasNoAsciiArtCodePaths()
        {
            foreach (string file in new[] { "EmployerSceneView.cs", "EmployerPortrait.cs", "LaunchPresentation.cs", "ExplorationView.cs", "AppShell.cs" }) {
                string source = File.ReadAllText(Path.Combine(Application.dataPath, "Unity/UI", file));
                StringAssert.DoesNotContain("Theme.ArtFont", source, file);
                StringAssert.DoesNotContain("EmployerPortrait.Art", source, file);
                StringAssert.DoesNotContain("Ascii", source, file);
            }
            StringAssert.Contains("toon-catalog-", Resources.Load<TextAsset>("ItemIcons/catalog").text);
        }
    }
}
