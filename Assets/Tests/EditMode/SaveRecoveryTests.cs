using System;
using System.IO;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public sealed class SaveRecoveryTests
    {
        private string directory;
        private SaveService service;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "after-seoul-save-recovery-" + Guid.NewGuid().ToString("N"));
            service = new SaveService(new FileStore(directory), new NewtonsoftJsonCodec(),
                new TestClock(new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero)));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void ValidPrimaryWinsOverOlderBackup()
        {
            WriteTwoVersions();

            var loaded = service.LoadOrCreate();

            Assert.AreEqual(29, loaded.Player.Money);
            Assert.AreEqual(17, new NewtonsoftJsonCodec().Deserialize<GameSave>(Read("save.json.bak")).Player.Money);
        }

        [Test]
        public void CorruptPrimaryRestoresValidBackupAndPreservesCorruptBytes()
        {
            WriteTwoVersions();
            const string corrupt = "{ broken primary";
            File.WriteAllText(PathInTestDirectory("save.json"), corrupt);

            var loaded = service.LoadOrCreate();

            Assert.AreEqual(17, loaded.Player.Money);
            Assert.AreEqual(17, new NewtonsoftJsonCodec().Deserialize<GameSave>(Read("save.json")).Player.Money);
            Assert.AreEqual(17, new NewtonsoftJsonCodec().Deserialize<GameSave>(Read("save.json.bak")).Player.Money);
            Assert.AreEqual(corrupt, Read("save.json.corrupt"));
            Assert.IsNotEmpty(service.LastLoadError, "복구 사실을 사용자에게 알릴 수 있어야 한다");
        }

        [Test]
        public void MissingPrimaryRestoresValidBackup()
        {
            WriteTwoVersions();
            File.Delete(PathInTestDirectory("save.json"));

            var loaded = service.LoadOrCreate();

            Assert.AreEqual(17, loaded.Player.Money);
            Assert.AreEqual(17, new NewtonsoftJsonCodec().Deserialize<GameSave>(Read("save.json")).Player.Money);
        }

        [Test]
        public void EmptyObjectIsNotAcceptedAsNewProgress()
        {
            WriteTwoVersions();
            File.WriteAllText(PathInTestDirectory("save.json"), "{}");

            var loaded = service.LoadOrCreate();

            Assert.AreEqual(17, loaded.Player.Money);
            Assert.AreEqual("{}", Read("save.json.corrupt"));
        }

        [Test]
        public void TwoUnusableCopiesKeepTheirOriginalBytesAndBlockNewGame()
        {
            WriteTwoVersions();
            const string primary = "{ bad primary";
            const string backup = "{ bad backup";
            File.WriteAllText(PathInTestDirectory("save.json"), primary);
            File.WriteAllText(PathInTestDirectory("save.json.bak"), backup);

            Assert.Throws<InvalidDataException>(() => service.LoadOrCreate());

            Assert.AreEqual(primary, Read("save.json"));
            Assert.AreEqual(backup, Read("save.json.bak"));
        }

        [Test]
        public void ExplicitStartNewArchivesBothUnusableCopiesBeforeSaving()
        {
            WriteTwoVersions();
            const string primary = "{ bad primary";
            const string backup = "{ bad backup";
            File.WriteAllText(PathInTestDirectory("save.json"), primary);
            File.WriteAllText(PathInTestDirectory("save.json.bak"), backup);
            Assert.Throws<InvalidDataException>(() => service.LoadOrCreate());

            var fresh = service.StartNewAfterRecoveryFailure();

            Assert.AreEqual(0, fresh.Player.Money);
            Assert.AreEqual(primary, Read("save.json.corrupt"));
            Assert.AreEqual(backup, Read("save.json.bak.corrupt"));
            Assert.AreEqual(0, new NewtonsoftJsonCodec().Deserialize<GameSave>(Read("save.json")).Player.Money);
        }

        [Test]
        public void RepeatedRecoveryDoesNotOverwritePreviousCorruptArchive()
        {
            WriteTwoVersions();
            File.WriteAllText(PathInTestDirectory("save.json"), "first broken copy");
            service.LoadOrCreate();
            File.WriteAllText(PathInTestDirectory("save.json"), "second broken copy");

            service.LoadOrCreate();

            Assert.AreEqual("first broken copy", Read("save.json.corrupt"));
            var archives = Array.FindAll(Directory.GetFiles(directory),
                path => Path.GetFileName(path).StartsWith("save.json.corrupt.", StringComparison.Ordinal));
            Assert.AreEqual(1, archives.Length);
            Assert.AreEqual("second broken copy", File.ReadAllText(archives[0]));
        }

        [Test]
        public void FutureSchemaPrimaryIsNotReplacedWithOlderBackup()
        {
            WriteTwoVersions();
            const string futureBytes = "{\"SchemaVersion\":999,\"Player\":\"new layout\"}";
            File.WriteAllText(PathInTestDirectory("save.json"), futureBytes);

            Assert.Throws<InvalidDataException>(() => service.LoadOrCreate());

            Assert.AreEqual(futureBytes, Read("save.json"));
            Assert.AreEqual(17, new NewtonsoftJsonCodec().Deserialize<GameSave>(Read("save.json.bak")).Player.Money);
        }

        [Test]
        public void LowerCaseFutureSchemaPrimaryIsNotReplacedWithOlderBackup()
        {
            WriteTwoVersions();
            const string futureBytes = "{\"schemaVersion\":999,\"Player\":\"new layout\"}";
            File.WriteAllText(PathInTestDirectory("save.json"), futureBytes);

            Assert.Throws<InvalidDataException>(() => service.LoadOrCreate());

            Assert.AreEqual(futureBytes, Read("save.json"));
        }

        [Test]
        public void NullScavInOldPrimaryFallsBackToValidBackup()
        {
            WriteTwoVersions();
            const string malformed = "{\"SchemaVersion\":1,\"SavedAt\":\"2026-09-18T00:00:00+00:00\",\"Player\":{},\"Scavs\":[null]}";
            File.WriteAllText(PathInTestDirectory("save.json"), malformed);

            var loaded = service.LoadOrCreate();

            Assert.AreEqual(17, loaded.Player.Money);
            Assert.AreEqual(malformed, Read("save.json.corrupt"));
        }

        [Test]
        public void NullExpeditionInPrimaryFallsBackToValidBackup()
        {
            WriteTwoVersions();
            const string malformed = "{\"SchemaVersion\":2,\"SavedAt\":\"2026-09-18T00:00:00+00:00\",\"Player\":{},\"Expeditions\":[null]}";
            File.WriteAllText(PathInTestDirectory("save.json"), malformed);

            var loaded = service.LoadOrCreate();

            Assert.AreEqual(17, loaded.Player.Money);
            Assert.AreEqual(malformed, Read("save.json.corrupt"));
        }

        [Test]
        public void StorageAccessFailureIsNotTreatedAsCorruptSave()
        {
            var denied = new SaveService(new DeniedReadStore(), new NewtonsoftJsonCodec(),
                new TestClock(new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero)));

            Assert.Throws<UnauthorizedAccessException>(() => denied.LoadOrCreate());
        }

        [Test]
        public void StartNewButtonRequiresExplicitSecondTap()
        {
            var owner = new GameObject("RecoveryTestOwner");
            try
            {
                int starts = 0;
                SaveRecoveryDialog.ShowBlocked(owner.transform, false, false, () => { }, () => starts++);
                var button = Array.Find(owner.GetComponentsInChildren<Button>(true),
                    item => item.name == "StartNewAfterSaveFailure");
                Assert.IsNotNull(button);

                button.onClick.Invoke();
                Assert.AreEqual(0, starts);
                button.onClick.Invoke();
                Assert.AreEqual(1, starts);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void StorageFailureOnlyOffersRetry()
        {
            var owner = new GameObject("RecoveryTestOwner");
            try
            {
                SaveRecoveryDialog.ShowBlocked(owner.transform, false, true, () => { }, null);
                var buttons = owner.GetComponentsInChildren<Button>(true);
                Assert.IsNotNull(Array.Find(buttons, item => item.name == "RetrySaveRecovery"));
                Assert.IsNull(Array.Find(buttons, item => item.name == "StartNewAfterSaveFailure"));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void BlockedRecoveryConsumesBackWithoutClosing()
        {
            var owner = new GameObject("RecoveryTestOwner");
            try
            {
                SaveRecoveryDialog.ShowBlocked(owner.transform, false, false, () => { }, () => { });
                var recovery = owner.transform.Find("[SaveRecovery]");

                Assert.IsTrue(TryHandleRecoveryBack());
                Assert.IsTrue(recovery.gameObject.activeSelf);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void RecoveredNoticeClosesOnBack()
        {
            var owner = new GameObject("RecoveryTestOwner");
            try
            {
                SaveRecoveryDialog.ShowRecovered(owner.transform);

                Assert.IsTrue(TryHandleRecoveryBack());
                Assert.IsFalse(TryHandleRecoveryBack());
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        private static bool TryHandleRecoveryBack()
        {
            var method = typeof(SaveRecoveryDialog).GetMethod("TryHandleBack");
            Assert.IsNotNull(method, "복구 화면이 앱 뒤로가기를 우선 처리해야 한다");
            return (bool)method.Invoke(null, null);
        }

        private void WriteTwoVersions()
        {
            var save = service.CreateNew();
            save.Player.Money = 17;
            service.Save(save);
            save.Player.Money = 29;
            service.Save(save);
        }

        private string Read(string name) => File.ReadAllText(PathInTestDirectory(name));
        private string PathInTestDirectory(string name) => Path.Combine(directory, name);

        private sealed class DeniedReadStore : IFileStore
        {
            public bool Exists(string path) => path == SaveService.FileName;
            public string ReadAllText(string path) => throw new UnauthorizedAccessException("save locked");
            public void WriteAllText(string path, string content) => throw new AssertionException("Must not write");
            public void Replace(string source, string dest) => throw new AssertionException("Must not replace");
            public bool TryMove(string source, string dest) => throw new AssertionException("Must not move");
        }
    }
}
