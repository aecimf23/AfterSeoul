using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 아이템군 — 목록에 붙는 아이콘의 분류 (GDD §13).
    ///
    /// <para>여기서 보는 건 그림이 예쁜가가 아니라 <b>분류가 제 일을 하는가</b>다.
    /// 태그는 겹쳐 있어서(볼트는 <c>부품·볼트·금속</c> 을 다 갖고 있다) 판정 순서가 곧 규칙이고,
    /// 순서를 한 번 잘못 두면 한 무리가 통째로 엉뚱한 아이콘을 달게 된다.</para>
    /// </summary>
    [TestFixture]
    public class ItemGroupTests
    {
        private JsonDataRegistry _data;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
        }

        private ItemGroup GroupOf(string id) => ItemGroups.Of(_data.GetItem(id));

        // ── 판정 순서 ────────────────────────────────────────────

        /// <summary>
        /// 겹치는 태그에서 좁은 쪽이 이겨야 한다. 이 네 줄이 순서 규칙의 전부다 —
        /// 하나라도 뒤집히면 같은 실수가 수십 종에 번진다.
        /// </summary>
        [Test]
        public void OverlappingTags_ResolveToTheNarrowestGroup()
        {
            // 볼트: 부품 · 볼트 · 금속 → 금속이 아니라 볼트
            Assert.AreEqual(ItemGroup.Bolt, GroupOf("JUNK03"));

            // 기판: 부품 · 전자 → 부품이 아니라 전자부품
            Assert.AreEqual(ItemGroup.Electronics, GroupOf("JUNK23"));

            // NVG 배터리: 전자 · 부품 인데 배터리다 (본편에 '배터리' 태그가 없다)
            Assert.AreEqual(ItemGroup.Battery, GroupOf("BAT01"));

            // 전기 모터: 부품 · 전자 · 금속 → 전자부품
            Assert.AreEqual(ItemGroup.Electronics, GroupOf("JUNK08"));
        }

        [Test]
        public void ObviousThings_LandWhereYouExpect()
        {
            Assert.AreEqual(ItemGroup.Ammo, GroupOf("AMO01"));
            Assert.AreEqual(ItemGroup.Medical, GroupOf("MED16"));
            Assert.AreEqual(ItemGroup.Metal, GroupOf("JUNK16"));
            Assert.AreEqual(ItemGroup.Key, GroupOf("KEY_CITYHALL"));
        }

        /// <summary>장착 칸이 있으면 태그보다 그게 확실하다 — 무기는 무기, 나머지 착용품은 장비.</summary>
        [Test]
        public void EquippableItems_FollowTheirSlot()
        {
            foreach (var item in _data.Items)
            {
                if (!item.Equippable || string.IsNullOrEmpty(item.EquipSlot)) continue;

                var group = ItemGroups.Of(item);
                var expected = item.EquipSlot == "Weapon" ? ItemGroup.Weapon : ItemGroup.Armor;

                Assert.AreEqual(expected, group, $"{item.Id}({item.EquipSlot})");
            }
        }

        // ── 분류가 제 일을 하는가 ────────────────────────────────

        /// <summary>
        /// <b>플레이어가 실제로 보게 되는 것</b>들이 미분류에 몰려 있으면 안 된다.
        ///
        /// <para>미분류가 제일 많은 분류는 분류가 아니다. 본편 잡동사니 전체를 보면 '부품'이
        /// 많은 게 당연하지만, 전리품 표와 레시피에 실제로 등장하는 것들 중에서는 적어야 한다.</para>
        /// </summary>
        [Test]
        public void ThingsPlayersActuallySee_AreMostlyClassified()
        {
            var seen = new HashSet<string>();
            foreach (var table in _data.LootTables)
                foreach (var entry in table.Entries) seen.Add(entry.ItemId);
            foreach (var recipe in _data.Recipes)
            {
                seen.Add(recipe.OutputItemId);
                foreach (var input in recipe.Inputs) seen.Add(input.ItemId);
            }

            var unclassified = new List<string>();
            foreach (var id in seen)
                if (ItemGroups.Of(_data.GetItem(id)) == ItemGroup.Parts) unclassified.Add(id);

            Assert.Less(unclassified.Count, seen.Count / 3,
                $"눈에 보이는 {seen.Count}종 중 {unclassified.Count}종이 미분류다: "
                + string.Join(", ", unclassified.ToArray()));
        }

        [Test]
        public void EveryGroup_HasALabel()
        {
            var labels = new HashSet<string>();
            foreach (var group in ItemGroups.All)
            {
                string label = ItemGroups.LabelOf(group);
                Assert.IsNotEmpty(label, group.ToString());
                Assert.IsTrue(labels.Add(label), $"이름이 겹친다: {label}");
            }
        }

        [Test]
        public void NullItem_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ItemGroups.Of(null));
        }
    }
}
