using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public sealed class CharacterLevelRewardTests
    {
        private IDataRegistry _data;
        private SwitchableFiles _files;
        private TestClock _clock;

        [SetUp]
        public void SetUp()
        {
            _data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            _files = new SwitchableFiles();
            _clock = new TestClock(DateTimeOffset.Parse("2026-09-18T00:00:00Z"));
        }

        private GameSession Session()
        {
            var session = new GameSession(new SaveService(_files, new NewtonsoftJsonCodec(), _clock), _data, _clock);
            session.Boot();
            return session;
        }

        private static void ClearProgress(GameSave save)
        {
            save.Player.Money = 0;
            save.Player.CharacterLevel = 1;
            save.Player.CharacterExp = 0;
            save.Warehouse.Capacity = 60;
            save.Warehouse.Stacks.Clear();
            save.ExplorationOverflow.Clear();
        }

        private static int ItemCount(GameSave save, string id) =>
            Warehouse.CountOf(save.Warehouse, id) + save.ExplorationOverflow.Where(x => x.ItemId == id).Sum(x => x.Count);

        private static Type RewardApi()
        {
            var api = typeof(GameSession).Assembly.GetType("AfterSeoul.Core.CharacterLevelRewards");
            Assert.IsNotNull(api, "Character level rewards need a shared catalog and grant policy.");
            return api;
        }

        private static T Member<T>(object owner, string name)
        {
            var type = owner.GetType();
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) return (T)field.GetValue(owner);
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(property, name + " should be readable on the reward snapshot.");
            return (T)property.GetValue(owner);
        }

        [Test]
        public void LevelTwoCommitGrantsItsMoneyAndRealSuppliesOnce()
        {
            var session = Session();
            ClearProgress(session.Save);
            session.Save.Player.CharacterExp = CharacterProgression.ExpForLevel(2, _data.Balance);

            session.Commit();

            Assert.AreEqual(2, session.Save.Player.CharacterLevel);
            Assert.AreEqual(10000, session.Save.Player.Money);
            Assert.AreEqual(2, ItemCount(session.Save, "MED05"));
            Assert.AreEqual(2, ItemCount(session.Save, "FOOD01"));
            Assert.AreEqual(2, ItemCount(session.Save, "FOOD02"));
            session.Commit();
            Assert.AreEqual(10000, session.Save.Player.Money, "Refreshing the same level must not pay twice.");
        }

        [Test]
        public void MultipleLevelsGrantEveryTierAndMilestoneCapacity()
        {
            var session = Session();
            ClearProgress(session.Save);
            session.Save.Player.CharacterExp = CharacterProgression.ExpForLevel(6, _data.Balance);

            session.Commit();

            Assert.AreEqual(100000, session.Save.Player.Money, "Levels 2..6 award 10k+15k+20k+25k+30k.");
            Assert.AreEqual(65, session.Save.Warehouse.Capacity, "Level 5 permanently adds five slots.");
            Assert.AreEqual(10, ItemCount(session.Save, "MED05"));
            Assert.AreEqual(10, ItemCount(session.Save, "FOOD01"));
            Assert.AreEqual(10, ItemCount(session.Save, "FOOD02"));

            var restored = Session();
            Assert.AreEqual(100000, restored.Save.Player.Money);
            Assert.AreEqual(65, restored.Save.Warehouse.Capacity);
            Assert.AreEqual(10, ItemCount(restored.Save, "MED05"));
        }

        [Test]
        public void LegacyHighLevelReceivesMissingRewardsOnBootOnlyOnce()
        {
            var service = new SaveService(_files, new NewtonsoftJsonCodec(), _clock);
            var old = service.CreateNew();
            old.Player.EmployerNpcId = "HWANG";
            old.Player.CharacterLevel = 7;
            old.Player.CharacterExp = 0;
            service.Save(old);

            var loaded = Session();
            Assert.AreEqual(135000, loaded.Save.Player.Money, "Existing level 7 earns levels 2..7 once.");
            Assert.AreEqual(65, loaded.Save.Warehouse.Capacity);
            Assert.AreEqual(12, ItemCount(loaded.Save, "MED05"));

            var again = Session();
            Assert.AreEqual(135000, again.Save.Player.Money);
            Assert.AreEqual(12, ItemCount(again.Save, "MED05"));
        }

        [Test]
        public void LegacyLevelRewardsSurviveDelayedEmployerChoice()
        {
            var service = new SaveService(_files, new NewtonsoftJsonCodec(), _clock);
            var old = service.CreateNew();
            old.Player.CharacterLevel = 7;
            service.Save(old);

            var loaded = Session();
            Assert.AreEqual(135000, loaded.Save.Player.Money);
            Assert.IsTrue(loaded.NeedsEmployerChoice);
            Assert.IsTrue(loaded.ChooseEmployer("HWANG"));
            Assert.AreEqual(135000, loaded.Save.Player.Money);
            Assert.AreEqual(135000, Session().Save.Player.Money);
        }

        [Test]
        public void FullWarehousePreservesEverySupplyInOverflow()
        {
            var session = Session();
            ClearProgress(session.Save);
            session.Save.Warehouse.Capacity = 0;
            session.Save.Player.CharacterExp = CharacterProgression.ExpForLevel(2, _data.Balance);

            session.Commit();

            Assert.AreEqual(0, session.Save.Warehouse.Stacks.Count);
            Assert.AreEqual(2, session.Save.ExplorationOverflow.Where(x => x.ItemId == "MED05").Sum(x => x.Count));
            Assert.AreEqual(2, session.Save.ExplorationOverflow.Where(x => x.ItemId == "FOOD01").Sum(x => x.Count));
            Assert.AreEqual(2, session.Save.ExplorationOverflow.Where(x => x.ItemId == "FOOD02").Sum(x => x.Count));
            Assert.AreEqual(10000, session.Save.Player.Money);
        }

        [Test]
        public void FailedActionSaveRollsBackRewardAndRetryPaysOnce()
        {
            var session = Session();
            ClearProgress(session.Save);
            _files.FailReplace = true;

            Assert.Throws<IOException>(() => session.ExecuteSavedAction(save =>
                CharacterProgression.Award(save, CharacterProgression.ExpForLevel(2, _data.Balance), _data.Balance) > 0));
            Assert.AreEqual(0, session.Save.Player.Money);
            Assert.AreEqual(0, session.Save.Player.CharacterExp);
            Assert.AreEqual(0, ItemCount(session.Save, "MED05"));

            _files.FailReplace = false;
            Assert.IsTrue(session.ExecuteSavedAction(save =>
                CharacterProgression.Award(save, CharacterProgression.ExpForLevel(2, _data.Balance), _data.Balance) > 0));
            Assert.AreEqual(10000, session.Save.Player.Money);
            Assert.AreEqual(2, ItemCount(session.Save, "MED05"));
            Assert.AreEqual(10000, Session().Save.Player.Money);
        }

        [Test]
        public void RewardCatalogAndAcknowledgementUseGrantedSnapshot()
        {
            var session = Session();
            ClearProgress(session.Save);
            var api = RewardApi();
            var forLevel = api.GetMethod("ForLevel", BindingFlags.Public | BindingFlags.Static);
            var pending = api.GetMethod("Pending", BindingFlags.Public | BindingFlags.Static);
            var next = api.GetMethod("Next", BindingFlags.Public | BindingFlags.Static);
            var acknowledge = api.GetMethod("Acknowledge", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(forLevel); Assert.IsNotNull(pending); Assert.IsNotNull(next); Assert.IsNotNull(acknowledge);

            var milestone = forLevel.Invoke(null, new object[] { 5 });
            Assert.AreEqual(25000, Member<long>(milestone, "Money"));
            Assert.AreEqual(5, Member<int>(milestone, "WarehouseSlots"));
            Assert.AreEqual(3, Member<ItemStack[]>(milestone, "Items").Length);
            session.Save.Player.CharacterExp = CharacterProgression.ExpForLevel(4, _data.Balance);
            session.Commit();
            Assert.AreEqual(3, ((IEnumerable)pending.Invoke(null, new object[] { session.Save })).Cast<object>().Count());
            Assert.AreEqual(5, Member<int>(next.Invoke(null, new object[] { session.Save, _data.Balance }), "Level"));
            Assert.IsFalse((bool)acknowledge.Invoke(null, new object[] { session.Save, 5 }), "A later, ungranted level cannot be acknowledged.");
            Assert.IsTrue((bool)acknowledge.Invoke(null, new object[] { session.Save, 2 }));
            Assert.AreEqual(2, ((IEnumerable)pending.Invoke(null, new object[] { session.Save })).Cast<object>().Count());
            Assert.AreEqual(5, Member<int>(next.Invoke(null, new object[] { session.Save, _data.Balance }), "Level"));
            session.Commit();
            Assert.AreEqual(2, ((IEnumerable)pending.Invoke(null, new object[] { Session().Save })).Cast<object>().Count(),
                "The acknowledged receipt must stay acknowledged after restarting.");
        }

        [Test]
        public void CatalogStopsAtLevelTwentyAndNeverWrapsMoneyOrCapacity()
        {
            var session = Session();
            ClearProgress(session.Save);
            session.Save.Player.CharacterLevel = 25;
            session.Commit();
            Assert.AreEqual(1045000, session.Save.Player.Money);
            Assert.AreEqual(80, session.Save.Warehouse.Capacity);
            Assert.AreEqual(38, ItemCount(session.Save, "MED05"));

            _files = new SwitchableFiles();
            var almostFull = Session();
            ClearProgress(almostFull.Save);
            almostFull.Save.Player.Money = long.MaxValue;
            almostFull.Save.Player.CharacterExp = CharacterProgression.ExpForLevel(2, _data.Balance);
            Assert.Throws<OverflowException>(() => almostFull.ExecuteSavedAction(_ => true));
            Assert.AreEqual(long.MaxValue, almostFull.Save.Player.Money);
            Assert.AreEqual(0, ItemCount(almostFull.Save, "MED05"));

            _files = new SwitchableFiles();
            var almostFullWarehouse = Session();
            ClearProgress(almostFullWarehouse.Save);
            almostFullWarehouse.Save.Warehouse.Capacity = int.MaxValue - 4;
            almostFullWarehouse.Save.Player.CharacterExp = CharacterProgression.ExpForLevel(5, _data.Balance);
            Assert.Throws<OverflowException>(() => almostFullWarehouse.ExecuteSavedAction(_ => true));
            Assert.AreEqual(int.MaxValue - 4, almostFullWarehouse.Save.Warehouse.Capacity);
            Assert.AreEqual(0, almostFullWarehouse.Save.Player.Money);
            Assert.AreEqual(0, ItemCount(almostFullWarehouse.Save, "MED05"));
        }

        private sealed class SwitchableFiles : IFileStore
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
            public bool FailReplace;
            public bool Exists(string path) => _files.ContainsKey(path);
            public string ReadAllText(string path) => _files[path];
            public void WriteAllText(string path, string content) => _files[path] = content;
            public void Replace(string source, string destination)
            {
                if (FailReplace) throw new IOException("The test store rejected the save.");
                _files[destination] = _files[source];
                _files.Remove(source);
            }
            public bool TryMove(string source, string destination)
            {
                if (!_files.ContainsKey(source)) return false;
                _files[destination] = _files[source];
                _files.Remove(source);
                return true;
            }
        }
    }
}
