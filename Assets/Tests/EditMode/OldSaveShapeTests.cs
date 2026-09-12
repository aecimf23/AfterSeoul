using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// <b>옛 빌드가 쓴 세이브를 진짜 그 모양 그대로 열어 본다.</b>
    ///
    /// <para>이미 있는 마이그레이션 테스트는 <b>지금 모양의 객체</b>를 직렬화해 놓고
    /// <c>SchemaVersion</c> 만 1 로 바꾼다. 그러면 JSON 안에 나중에 추가된 필드가 전부 들어 있어서,
    /// 정작 <b>키 자체가 없는</b> 옛 세이브는 한 번도 검증되지 않는다. 마이그레이션 로직은 보지만
    /// <b>역직렬화</b>는 안 보는 셈이다.</para>
    ///
    /// <para>그 사이 세이브에는 필드가 계속 붙었다 — 치료(<c>RecoversAt</c>, <c>TreatedAt</c>),
    /// 고용 시장 리롤(<c>RerollCount</c>), 창고 보너스(<c>BonusCapacity</c>), 지원계약(<c>Support</c>),
    /// 발송함 수령 표시. 하나라도 없을 때 터지면 <b>기존 플레이어의 세이브가 열리지 않는다</b> —
    /// 되돌릴 수 없는 부류의 사고다.</para>
    ///
    /// <para>그래서 JSON 을 손으로 적는다. 객체를 만들어 직렬화하면 "지금 모양"이 따라붙어서
    /// 검사하려는 것이 사라진다.</para>
    /// </summary>
    [TestFixture]
    public class OldSaveShapeTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private IDataRegistry _data;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
        }

        /// <summary>
        /// 초창기 세이브. 있는 것만 적혀 있고 나머지 키는 <b>존재하지 않는다</b> —
        /// 그게 옛 빌드가 실제로 쓰던 파일이다.
        /// </summary>
        private const string AncientSave = @"{
  ""SchemaVersion"": 1,
  ""SavedAt"": ""2026-09-10T01:00:00+00:00"",
  ""RngCounter"": 7,
  ""Player"": {
    ""CreatedAt"": ""2026-09-01T01:00:00+00:00"",
    ""Money"": 480000,
    ""Exp"": 1200,
    ""Level"": 2,
    ""EmployerNpcId"": ""HWANG""
  },
  ""Warehouse"": {
    ""Capacity"": 60,
    ""Stacks"": [ { ""ItemId"": ""JUNK03"", ""Count"": 4 } ]
  },
  ""Scavs"": [
    {
      ""Uid"": ""sc_old"",
      ""Name"": ""정대만"",
      ""Level"": 1,
      ""Tier"": 1,
      ""WagePerHour"": 40000,
      ""Search"": 4,
      ""Combat"": 5,
      ""Survival"": 4,
      ""Status"": ""Idle"",
      ""Equipment"": { ""Weapon"": ""MEL01"" },
      ""HiredAt"": ""2026-09-02T01:00:00+00:00""
    }
  ],
  ""NpcTrust"": { ""HWANG"": 3 }
}";

        private GameSession OpenAncient(out TestClock clock)
        {
            clock = new TestClock(T0);

            var files = new MemoryFileStore();
            files.WriteAllText(SaveService.FileName, AncientSave);

            var session = new GameSession(
                new SaveService(files, new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();
            return session;
        }

        [Test]
        public void AnAncientSave_OpensWithoutBlowingUp()
        {
            TestClock clock;
            var session = OpenAncient(out clock);

            Assert.AreEqual(480000, session.Save.Player.Money, "읽은 값이 엉뚱하다");
            Assert.AreEqual(1, session.Save.Scavs.Count);
            Assert.AreEqual(SaveService.CurrentSchemaVersion, session.Save.SchemaVersion);
        }

        /// <summary>
        /// 나중에 붙은 하위 객체는 키가 없어도 <b>null 이 아니어야 한다.</b>
        /// null 이면 그걸 만지는 첫 코드에서 터지고, 증상은 "세이브가 안 열린다" 하나뿐이다.
        /// </summary>
        [Test]
        public void FieldsAddedLater_AreNotNull()
        {
            TestClock clock;
            var save = OpenAncient(out clock).Save;

            Assert.IsNotNull(save.Support, "지원계약 상태가 null 이다");
            Assert.IsNotNull(save.Mail, "발송함이 null 이다");
            Assert.IsNotNull(save.Mail.Outbox);
            Assert.IsNotNull(save.Market, "고용 시장이 null 이다");
            Assert.IsNotNull(save.Market.Offers);
            Assert.IsNotNull(save.Factory, "공장이 null 이다");
            Assert.IsNotNull(save.Factory.Queue);
            Assert.IsNotNull(save.Quests);
            Assert.IsNotNull(save.Expeditions);
            Assert.IsNotNull(save.Warehouse.Stacks);
        }

        /// <summary>
        /// 나중에 붙은 값은 "없었던 상태"로 읽혀야 한다.
        /// 기본값이 엉뚱하면 옛 플레이어가 공짜 혜택을 받거나 반대로 손해를 본다.
        /// </summary>
        [Test]
        public void FieldsAddedLater_DefaultToNothingHavingHappened()
        {
            TestClock clock;
            var session = OpenAncient(out clock);
            var save = session.Save;

            Assert.IsFalse(Support.IsActive(save, clock.UtcNow), "계약을 산 적이 없는데 켜져 있다");
            Assert.IsFalse(save.Mail.Linked, "연동한 적이 없는데 연결돼 있다");
            Assert.AreEqual(0, save.Market.RerollCount);
            Assert.AreEqual(0, save.Warehouse.BonusCapacity);
            Assert.AreEqual(save.Warehouse.Capacity, save.Warehouse.TotalCapacity);

            var scav = save.Scavs[0];
            Assert.AreEqual(ScavStatus.Idle, scav.Status);
            Assert.AreEqual(default(DateTimeOffset), scav.RecoversAt, "치료한 적이 없는데 회복 시각이 있다");
            Assert.AreEqual(default(DateTimeOffset), scav.TreatedAt);
        }

        /// <summary>
        /// <b>열리기만 하면 되는 게 아니라 계속 굴러가야 한다.</b>
        ///
        /// <para>나중에 붙은 시스템들(고용 시장·보조 인력·구조·치료)이 옛 세이브를 처음 보는
        /// 순간이 여기다. 하나라도 없는 필드를 전제하면 정산에서 터진다.</para>
        /// </summary>
        [Test]
        public void AnAncientSave_KeepsResolving()
        {
            TestClock clock;
            var session = OpenAncient(out clock);

            Assert.DoesNotThrow(() =>
            {
                clock.Advance(TimeSpan.FromDays(3));
                session.Tick();
            }, "옛 세이브를 정산하다 터졌다");

            // 사흘이 지났으니 고용 시장이 후보를 걸었어야 한다 —
            // 이게 0 이면 정산이 돌긴 했는데 아무 일도 안 한 것이다.
            Assert.Greater(session.Save.Market.Offers.Count, 0,
                "옛 세이브에서 고용 시장이 열리지 않는다");
        }

        /// <summary>
        /// 열고 나서 저장하면 지금 모양이 된다. 그래야 다음 실행부터는 옛 모양을 다시 안 겪는다.
        /// </summary>
        [Test]
        public void OnceOpened_ItIsWrittenBackInTheCurrentShape()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            files.WriteAllText(SaveService.FileName, AncientSave);

            var codec = new NewtonsoftJsonCodec();
            var session = new GameSession(new SaveService(files, codec, clock), _data, clock);
            session.Boot();
            session.Suspend();

            string written = files.ReadAllText(SaveService.FileName);
            var reopened = codec.Deserialize<GameSave>(written);

            Assert.AreEqual(SaveService.CurrentSchemaVersion, reopened.SchemaVersion);
            Assert.IsNotNull(reopened.Support);
            Assert.AreEqual(480000, reopened.Player.Money);

            // 파생값은 저장하지 않는다 (계약이 끝나도 남아버리므로).
            StringAssert.DoesNotContain("BonusCapacity", written,
                "창고 보너스는 파생값이라 세이브에 담기면 안 된다 — 만료 뒤에도 남는다");
        }
    }
}
