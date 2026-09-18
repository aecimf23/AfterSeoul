using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
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
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void Setup()
        {
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            var clock = new TestClock(new DateTimeOffset(2026, 9, 17, 1, 0, 0, TimeSpan.Zero));
            _session = new GameSession(new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), data, clock);
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

        [Test] public void KnifeDetailsShowCombatDamageAndEquipOnPlayer()
        {
            Warehouse.TryAdd(_session.Save.Warehouse, _session.Data, "MEL01", 1);
            _screen.Refresh();
            _host.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Item_MEL01").onClick.Invoke();
            var stats=_host.GetComponentsInChildren<Text>(true).SingleOrDefault(t=>t.name=="CombatStats");
            Assert.IsNotNull(stats,"Knife details must show actual combat stats");
            StringAssert.Contains("26",stats.text);
            _host.GetComponentsInChildren<Button>(true).Single(b=>b.name=="EquipPlayer").onClick.Invoke();
            Assert.AreEqual("MEL01",AfterSeoul.Exploration.PlayerEquipment.Equipped(_session.Save,"Melee"));
            Assert.AreEqual(0,Warehouse.CountOf(_session.Save.Warehouse,"MEL01"));
            Assert.IsTrue(_host.GetComponentsInChildren<Button>(true).Any(b=>b.name=="PlayerSlot_Melee"));
        }

        [Test] public void PlayerLoadoutShowsSevenSlotsAndUnequipsToWarehouse()
        {
            Warehouse.TryAdd(_session.Save.Warehouse,_session.Data,"MEL01",1);
            Assert.IsTrue(AfterSeoul.Exploration.PlayerEquipment.TryEquip(_session.Save,_session.Data,"MEL01"));
            _host.GetComponentsInChildren<Button>(true).Single(b=>b.name=="PlayerLoadout").onClick.Invoke();
            Assert.AreEqual(7,_host.GetComponentsInChildren<Button>(true).Count(b=>b.name.StartsWith("PlayerSlot_")));
            _host.GetComponentsInChildren<Button>(true).Single(b=>b.name=="PlayerSlot_Melee").onClick.Invoke();
            _host.GetComponentsInChildren<Button>(true).Single(b=>b.name=="UnequipPlayer").onClick.Invoke();
            Assert.IsNull(AfterSeoul.Exploration.PlayerEquipment.Equipped(_session.Save,"Melee"));
            Assert.AreEqual(1,Warehouse.CountOf(_session.Save.Warehouse,"MEL01"));
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
