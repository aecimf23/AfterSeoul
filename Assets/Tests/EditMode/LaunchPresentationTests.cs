using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;
using System;
using System.Reflection;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class LaunchPresentationTests
    {
        [Test]
        public void IntroUsesIllustratedArtAndSkipStopsAtTitle()
        {
            var host=new GameObject("LaunchTest",typeof(RectTransform));
            ((RectTransform)host.transform).sizeDelta=new Vector2(1080,1920);
            bool entered=false;
            try {
                var type=typeof(AppShell).Assembly.GetType("AfterSeoul.Unity.UI.LaunchPresentation");
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var intro=Activator.CreateInstance(type,flags,null,new object[]{host.transform,(Action)(()=>entered=true),null},null);
                var tick=type.GetMethod("Tick",flags);
                tick.Invoke(intro,new object[]{.8f});
                var mobile=host.transform.Find("LaunchPresentation/LaunchSafeArea/LaunchInk/CityArt").GetComponent<Image>();
                Assert.IsNotNull(mobile.sprite);
                Assert.IsNull(host.transform.Find("LaunchPresentation/LaunchSafeArea/LaunchInk/MapViewport/LegacyMap"));
                host.transform.Find("LaunchPresentation").GetComponent<Button>().onClick.Invoke();
                tick.Invoke(intro,new object[]{20f});
                Assert.IsNotNull(mobile.sprite);
                Assert.IsFalse(entered,"Skipping the film must still stop at the title");
                host.transform.Find("LaunchPresentation").GetComponent<Button>().onClick.Invoke();
                Assert.IsTrue(entered);
            } finally { UnityEngine.Object.DestroyImmediate(host); }
        }
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
            finally { UnityEngine.Object.DestroyImmediate(parent); }
        }
    }
}
