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
        public void IntroCrossfadesOriginalMapIntoMobileArt_AndSkipShowsFinishedArt()
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
                var legacy=host.transform.Find("LaunchPresentation/LaunchSafeArea/LaunchInk/MapViewport/LegacyMap").GetComponent<Text>();
                var mobile=host.transform.Find("LaunchPresentation/LaunchSafeArea/LaunchInk/MapViewport/CityArt").GetComponent<Image>();
                Assert.AreSame(legacy.transform.parent,mobile.transform.parent);
                var rows=legacy.text.Split('\n');
                foreach(var row in rows) Assert.AreEqual(rows[0].Length,row.Length);
                Assert.AreEqual(TextAnchor.UpperLeft,legacy.alignment);
                var viewport=(RectTransform)mobile.transform.parent;
                Assert.AreEqual(2,viewport.rect.width/viewport.rect.height,.001f);
                Assert.AreEqual(viewport.rect.width,legacy.rectTransform.rect.width*legacy.rectTransform.localScale.x,.01f);
                Assert.AreEqual(viewport.rect.height,legacy.rectTransform.rect.height*legacy.rectTransform.localScale.y,.01f);
                StringAssert.Contains("^^^^",legacy.text);
                StringAssert.Contains("launch_map_mobile",mobile.sprite.name);
                Assert.AreEqual(1,legacy.color.a,.001f); Assert.AreEqual(0,mobile.color.a,.001f);
                tick.Invoke(intro,new object[]{1.8f});
                Assert.Greater(legacy.color.a,0); Assert.Less(legacy.color.a,1);
                Assert.Greater(mobile.color.a,0); Assert.AreEqual(1,legacy.color.a+mobile.color.a,.001f);
                host.transform.Find("LaunchPresentation").GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual(0,legacy.color.a,.001f); Assert.AreEqual(1,mobile.color.a,.001f);
                tick.Invoke(intro,new object[]{20f});
                Assert.AreEqual(1,mobile.color.a,.001f);
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
