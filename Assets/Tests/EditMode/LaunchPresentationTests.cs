using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;

namespace AfterSeoul.Tests
{
    public class LaunchPresentationTests
    {
        [Test]
        public void FilmSkipStopsAtTitleAndRequiresAnotherTap()
        {
            var flow = new LaunchFlow();
            flow.Tick(2); flow.Tap();
            Assert.AreEqual(LaunchPhase.Title, flow.Phase);
            flow.Tick(3600);
            Assert.AreEqual(LaunchPhase.Title, flow.Phase);
            flow.Tap(); Assert.AreEqual(LaunchPhase.Entered, flow.Phase);
        }

        [Test]
        public void BottomNavigationIsAnchoredAboveTheSafeAreaBottom()
        {
            var parent = new GameObject("Safe",typeof(RectTransform));
            try
            {
                var bar=Ui.Rect("TabBar",parent.transform);
                Ui.Bottom(bar,Theme.TabBarHeight);
                Assert.AreEqual(Vector2.zero,bar.anchorMin);
                Assert.AreEqual(new Vector2(1,0),bar.anchorMax);
                Assert.AreEqual(Theme.TabBarHeight,bar.rect.height);
                Assert.AreEqual(0,bar.offsetMin.y);
            }
            finally { Object.DestroyImmediate(parent); }
        }
    }
}
