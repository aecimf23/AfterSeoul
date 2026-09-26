using System.Collections.Generic;
using System.IO;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class RegionalStoryQuestCatalogTests
    {
        JsonDataRegistry _data;

        [OneTimeSetUp]
        public void LoadData()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
        }

        [TestCase("YONGSAN_MARKET", "YONGSAN_KIM", "부품 창고", "JUNK25")]
        [TestCase("GURO_FACTORY", "DONGDAEMUN_CHOI", "정비 작업실", "JUNK28")]
        [TestCase("HAN_RIVER", "DOKKAEBI", "교각 아래", "JUNK_LIGHTER")]
        [TestCase("NAMSAN_WOODS", "WILDMAN", "숲속 야영지", "FOOD01")]
        [TestCase("GANGNAM_STREETS", "HWANG", "아파트 관리실", "JUNK20")]
        [TestCase("YONGSAN_BASE", "US_LIAISON", "막사 의무실", "MED16")]
        [TestCase("MYEONGDONG", "BROKER", "의류 매장", "JUNK34")]
        [TestCase("UIJEONGBU", "WILDMAN", "대피소 창고", "FOOD02")]
        public void EveryStoryUsesARealSafeSiteAndOrdinaryItem(string map, string npc, string siteName, string itemId)
        {
            var story = RegionalStoryQuestCatalog.Find(map);
            Assert.IsNotNull(story, map);
            Assert.AreEqual(map, story.Map);
            Assert.AreEqual(npc, story.Npc);
            Assert.AreEqual(siteName, story.Site);
            Assert.AreEqual(itemId, story.ItemId);
            var site = RaidRegions.Find(map, siteName);
            Assert.IsNotNull(site);
            Assert.IsFalse(site.Dangerous);
            var item = _data.GetItem(itemId);
            Assert.IsNotNull(item);
            Assert.Greater(item.SpawnWeight, 0);
            Assert.LessOrEqual(item.BasePrice, 15000);
            Assert.IsTrue(LootContainers.AvailableIn(item, site.Container), map + " item cannot appear in " + site.Container);
        }

        [Test]
        public void CatalogHasExactlyOneStoryForEachNormalRouteRegion()
        {
            var expected = new HashSet<string> {
                "YONGSAN_MARKET", "GURO_FACTORY", "HAN_RIVER", "NAMSAN_WOODS",
                "GANGNAM_STREETS", "YONGSAN_BASE", "MYEONGDONG", "UIJEONGBU"
            };
            Assert.AreEqual(expected.Count, RegionalStoryQuestCatalog.All.Length);
            foreach (var story in RegionalStoryQuestCatalog.All)
                Assert.IsTrue(expected.Remove(story.Map), story.Map);
            Assert.IsEmpty(expected);
            Assert.IsNull(RegionalStoryQuestCatalog.Find("SUSPICIOUS_TUNNEL"));
        }
    }
}
