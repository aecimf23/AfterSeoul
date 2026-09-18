using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Exploration;
using AfterSeoul.Unity.UI;
using AfterSeoul.Unity.UI.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class WarehouseBrowserTests
    {
        private GameObject _host;
        private WarehouseScreen _screen;
        private GameSession _session;
        private FailingFiles _files;
        private TestClock _clock;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void Setup()
        {
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            var clock = new TestClock(new DateTimeOffset(2026, 9, 17, 1, 0, 0, TimeSpan.Zero));
            _clock = clock; _files = new FailingFiles();
            _session = new GameSession(new SaveService(_files, new NewtonsoftJsonCodec(), clock), data, clock);
            _session.Boot();
            _session.ChooseEmployer("HWANG");
            Warehouse.TryAdd(_session.Save.Warehouse, data, "MED01", 1);
            _host = new GameObject("WarehouseTest", typeof(RectTransform));
            _host.SetActive(false);
            var shell = _host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady", Private).Invoke(shell, new object[] { _session });
            shell.Select(4);
            var screens = (System.Collections.IList)typeof(AppShell).GetField("_screens", Private).GetValue(shell);
            _screen = (WarehouseScreen)screens[4];
        }

        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(_host); Tween.Clear(); }

        private Button Find(string name) => _host.GetComponentsInChildren<Button>(true).Last(b => b.name == name);
        private Text Label(string name) => _host.GetComponentsInChildren<Text>(true).Last(t => t.name == name);

        [Test] public void WarehouseShowsEveryPlayerSlotIncludingEmptySlots()
        {
            foreach (var slot in PlayerEquipment.Slots) Assert.IsNotNull(Find("Slot_" + slot));
            Assert.IsTrue(_host.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "CharacterSilhouette"));
            Find("Slot_Melee").onClick.Invoke();
            Assert.IsNotEmpty(Label("EquipmentEmpty").text);
        }

        [Test] public void KnifeDetailExplainsSlotAndEquipsWithoutReplacingGun()
        {
            ExplorationSystem.PrepareStarter(_session.Save, _session.Data);
            _screen.Refresh();
            Find("Item_MEL01").onClick.Invoke();
            StringAssert.Contains("근접", Label("EquipmentSlot").text);
            Assert.IsNotEmpty(Label("CurrentEquipment").text);
            Find("WearItem").onClick.Invoke();
            Assert.AreEqual("MEL01", PlayerEquipment.Equipped(_session.Save, "Melee"));
            Assert.AreEqual("WPN04", PlayerEquipment.Equipped(_session.Save, "Weapon"));
            Assert.AreEqual(0, Warehouse.CountOf(_session.Save.Warehouse, "MEL01"));
            Assert.AreNotEqual(Loc.Text("미착용 · 선택"), Label("Equipped_Melee").text);
            Assert.IsTrue(Find("Unequip").interactable);
            Find("Unequip").onClick.Invoke();
            Assert.IsNull(PlayerEquipment.Equipped(_session.Save, "Melee"));
            Assert.AreEqual(1, Warehouse.CountOf(_session.Save.Warehouse, "MEL01"));
        }

        [Test] public void FullWarehouseExplainsWhyEquippedItemCannotBeRemoved()
        {
            _session.Save.Player.Equipment["Melee"] = "MEL01";
            _session.Save.Warehouse.Capacity = 0;
            _session.Save.Warehouse.BonusCapacity = 0;
            _screen.Refresh();
            Find("Slot_Melee").onClick.Invoke();
            Assert.IsFalse(Find("Unequip").interactable);
            StringAssert.Contains("창고", Label("UnequipReason").text);
            Assert.AreEqual("MEL01", PlayerEquipment.Equipped(_session.Save, "Melee"));
        }

        [Test] public void ActiveRaidExplainsEquipmentLockInsteadOfFailingSilently()
        {
            ExplorationSystem.PrepareStarter(_session.Save, _session.Data);
            Warehouse.TryAdd(_session.Save.Warehouse, _session.Data, "MEL01", 1);
            Assert.IsTrue(ExplorationSystem.Start(_session.Save, _session.Data, "YONGSAN_MARKET"));
            _screen.Refresh(); Find("Item_MEL01").onClick.Invoke();
            Assert.IsFalse(Find("WearItem").interactable);
            StringAssert.Contains("탐색", Label("EquipmentReason").text);
        }

        [Test] public void FailedEquipmentSaveKeepsInventoryAndAllowsRetryThenReload()
        {
            Warehouse.TryAdd(_session.Save.Warehouse, _session.Data, "MEL01", 1);
            _session.Commit(); _screen.Refresh();
            Find("Item_MEL01").onClick.Invoke();
            _files.Fail = true; Find("WearItem").onClick.Invoke();
            Assert.IsNull(PlayerEquipment.Equipped(_session.Save, "Melee"));
            Assert.AreEqual(1, Warehouse.CountOf(_session.Save.Warehouse, "MEL01"));
            _files.Fail = false; Find("Equip_MEL01").onClick.Invoke();
            var reloaded = new GameSession(new SaveService(_files, new NewtonsoftJsonCodec(), _clock), _session.Data, _clock);
            reloaded.Boot();
            Assert.AreEqual("MEL01", PlayerEquipment.Equipped(reloaded.Save, "Melee"));
            Assert.AreEqual(0, Warehouse.CountOf(reloaded.Save.Warehouse, "MEL01"));
        }

        private sealed class FailingFiles : IFileStore
        {
            private readonly MemoryFileStore _inner = new MemoryFileStore();
            public bool Fail;
            public bool Exists(string path) => _inner.Exists(path);
            public string ReadAllText(string path) => _inner.ReadAllText(path);
            public void WriteAllText(string path, string content) { if (Fail) throw new IOException("test storage failure"); _inner.WriteAllText(path, content); }
            public void Replace(string source, string destination) => _inner.Replace(source, destination);
            public bool TryMove(string source, string destination) => _inner.TryMove(source, destination);
        }

        [Test] public void SelectingItemShowsDescriptionWithoutConsumingAndClosingPreservesRow()
        {
            var item = _session.Save.Warehouse.Stacks.First();
            typeof(WarehouseScreen).GetField("_filter", Private).SetValue(_screen, ItemGroups.Of(_session.Data.GetItem(item.ItemId)));
            _screen.Refresh();
            var list = (RectTransform)typeof(WarehouseScreen).GetField("_list", Private).GetValue(_screen);
            var position = list.anchoredPosition = new Vector2(0f, 37f);
            int before = Warehouse.CountOf(_session.Save.Warehouse, item.ItemId);
            var row = _host.GetComponentsInChildren<Button>(true).Single(b => b.name == "Item_" + item.ItemId);
            row.onClick.Invoke();
            Assert.AreEqual(before, Warehouse.CountOf(_session.Save.Warehouse, item.ItemId));
            Assert.IsTrue(_host.GetComponentsInChildren<Text>(true).Any(t => t.name == "Description"));
            _host.GetComponentsInChildren<Button>(true).Last(b => b.name == "Close").onClick.Invoke();
            _screen.Refresh();
            Assert.AreEqual(position, list.anchoredPosition);
            Assert.AreEqual(ItemGroups.Of(_session.Data.GetItem(item.ItemId)), typeof(WarehouseScreen).GetField("_filter", Private).GetValue(_screen));
            Assert.AreSame(row, _host.GetComponentsInChildren<Button>(true).Single(b => b.name == "Item_" + item.ItemId));
            Assert.IsFalse(_host.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "ItemDetail"));
        }

        [Test] public void RefreshReusesUnchangedRows()
        {
            var rows = _host.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Item_")).ToArray();
            Assert.IsNotEmpty(rows);
            _screen.Refresh();
            CollectionAssert.AreEquivalent(rows, _host.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Item_")).ToArray());
        }
    }
}
