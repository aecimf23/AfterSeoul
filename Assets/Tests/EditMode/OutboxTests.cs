using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Mail;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// PC 본편 발송함 (GDD §10).
    ///
    /// <para><b>가치 상한이 진짜 방어선이다.</b> 본편 탄약은 단가가 900원부터 268,000원까지
    /// 300배 차이가 난다 — "60발까지" 같은 수량 제한은 방어가 되지 못한다. 한 스택이
    /// 90만원어치일 수 있다. 그래서 어떤 경로로도 1회·일일 가치 상한을 넘지 않는지가
    /// 이 파일에서 제일 중요한 부분이다.</para>
    /// </summary>
    [TestFixture]
    public class OutboxTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private GameSave NewSave()
        {
            var save = new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
            save.Warehouse.Capacity = 200;

            // 연결된 상태를 기본으로 둔다 — 연결 자체를 보는 테스트는 아래에 따로 있다.
            save.Mail.Linked = true;
            save.Mail.LinkedProfileLabel = "테스트 프로필";
            return save;
        }

        private void Give(GameSave save, string itemId, int count)
            => Assert.AreEqual(0, Warehouse.TryAdd(save.Warehouse, _data, itemId, count), itemId);

        private static List<ItemStack> Ship(params ItemStack[] items) => new List<ItemStack>(items);

        // ── 연동 통로 (GDD §12) ──────────────────────────────────

        /// <summary>
        /// <b>통로가 없으면 연결되지 않는다.</b>
        ///
        /// <para>예전에는 <c>LinkToMainline</c> 이 확인 없이 <c>Linked = true</c> 로 만들었다.
        /// 그러면 창고에 발송 버튼이 생기고, 누르면 물건이 <b>차감</b>되고(§V7 — 복제를 막으려면
        /// 차감이 먼저다), 화면은 "본편 접속 시 전달됩니다"라고 알린다. 그런데 받을 쪽이 없어서
        /// 화물은 영영 안 간다. <b>안심시키는 문구와 함께 플레이어의 물건을 지우는 기능</b>이었다.</para>
        ///
        /// <para>미완성 기능이 조용히 실패하는 것과 데이터를 지우는 것은 다른 문제다.</para>
        /// </summary>
        [Test]
        public void WithoutALink_ConnectingIsRefused()
        {
            var clock = new TestClock(T0);
            var session = new GameSession(
                new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();

            bool ok = true;
            string reason = null;
            session.LinkToMainline(null, (success, message) => { ok = success; reason = message; });

            Assert.IsFalse(ok, "통로가 없는데 연결됐다 — 발송이 물건을 지우는 버튼이 된다");
            Assert.IsFalse(string.IsNullOrEmpty(reason), "왜 안 되는지 말해줘야 한다");
            Assert.IsFalse(session.Save.Mail.Linked);
        }

        /// <summary>
        /// 연결이 안 되면 발송 경로 전체가 저절로 닫혀야 한다.
        /// 화면마다 따로 막는 방식이면 언젠가 한 군데가 빠진다.
        /// </summary>
        [Test]
        public void WithoutALink_NothingCanBeSent()
        {
            var save = NewSave();
            save.Mail.Linked = false;
            Give(save, "MED16", 2);

            Assert.IsNotNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("MED16", 1)), T0));
            Assert.IsNull(Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 1)), T0));

            // 그리고 창고는 그대로여야 한다. 막혔는데 차감되면 그게 제일 나쁘다.
            Assert.AreEqual(2, Warehouse.CountOf(save.Warehouse, "MED16"),
                "발송이 막혔는데 물건이 줄었다");
        }

        // ── 모바일 전용 아이템 (LINK_CONTRACT §V2) ───────────────

        /// <summary>
        /// <b>AFTER SEOUL 전용 아이템은 본편으로 갈 수 없다.</b>
        ///
        /// <para>부록 A 의 위험 목록에서 <b>유일하게 되돌릴 수 없는</b> 사고가 "본편 경제 오염"이다.
        /// 아이템 복제는 되돌릴 수라도 있지만, 모바일이 지어낸 물건이 본편 창고에 들어가면
        /// 그건 본편 데이터베이스에 없는 id 라 어떻게 치울지부터 문제가 된다.</para>
        ///
        /// <para>이 규칙은 원래 <c>transferable_items.json</c> 의 <c>"AS": {allowed:false}</c>
        /// 한 줄에만 기대고 있었다. 그런데 그 파일은 <c>extract_mainline_data.py</c> 가 생성한다 —
        /// 추출기 상수에서 "AS" 가 빠지는 날 규칙이 소리 없이 사라진다.
        /// <b>생성되는 값에 기대는 불변식은 불변식이 아니다.</b></para>
        /// </summary>
        [Test]
        public void MobileOnlyItems_CanNeverBeSent()
        {
            var mobileOnly = new ItemDef
            {
                Id = Outbox.MobileOnlyPrefix + "SCRIP",
                Category = "AS",
                BasePrice = 100,          // 값·분류·단가 전부 통과하게 두고
                MaxStack = 10,
                Tags = new[] { "부품" },
            };

            Assert.IsNotNull(Outbox.ItemBlockReason(_data.Transfer, mobileOnly),
                "모바일 전용 아이템이 전송 가능으로 나온다");
            Assert.IsFalse(Outbox.CanSend(_data.Transfer, mobileOnly));
        }

        /// <summary>
        /// 정책 데이터를 통째로 비워도 막혀야 한다. 데이터가 아니라 규칙이라는 뜻이다.
        /// </summary>
        [Test]
        public void MobileOnlyItems_AreBlockedEvenWithAnEmptyPolicy()
        {
            var wideOpen = new TransferPolicyDef
            {
                UnitPriceCeiling = long.MaxValue,
                DenyItemIds = new string[0],
                Categories = new Dictionary<string, TransferCategoryDef>
                {
                    { "AS", new TransferCategoryDef { Allowed = true, MaxPerShipment = 99, MaxPerDay = 99 } },
                },
                Limits = new TransferLimitsDef(),
            };

            var mobileOnly = new ItemDef
            {
                Id = Outbox.MobileOnlyPrefix + "SCRIP", Category = "AS",
                BasePrice = 100, MaxStack = 10, Tags = new[] { "부품" },
            };

            Assert.IsNotNull(Outbox.ItemBlockReason(wideOpen, mobileOnly),
                "분류를 허용으로 열어 두면 통과한다 — 그럼 그건 규칙이 아니라 설정이다");
        }

        // ── 미수령 적체 (LINK_CONTRACT §V6) ──────────────────────

        /// <summary>
        /// <b>본편이 안 가져가면 언젠가 막혀야 한다.</b>
        ///
        /// <para>규약(§V6)에는 "shipments 가 20건 미만인가 → 차단 + 안내"라고 적혀 있는데
        /// 그 검사가 코드에 없었다. 발송함을 비워 주는 쪽은 P5(본편)라 지금은 아무도 안 비운다 —
        /// 하루 2건씩 쌓이면 한 달에 60건이고, 클라우드 문서는 규약 크기를 넘는다.
        /// 그 사고는 연동을 붙이는 날에야 드러난다.</para>
        /// </summary>
        [Test]
        public void Sending_IsBlockedWhenTooManyShipmentsAreUnclaimed()
        {
            var save = NewSave();
            int cap = _data.Transfer.Limits.MaxPendingShipments;

            // 상한만큼 미수령 화물을 쌓아 둔다.
            for (int i = 0; i < cap; i++)
                save.Mail.Outbox.Add(new MailShipment { TxId = "tx" + i, QueuedAt = T0, Claimed = false });

            Give(save, "MED16", 2);
            var blocked = Outbox.BlockReason(save, _data, Ship(new ItemStack("MED16", 2)), T0);

            Assert.IsNotNull(blocked, "미수령이 상한을 넘었는데 발송이 통과한다");
            StringAssert.Contains("수령", blocked, "왜 막혔는지 말해줘야 한다");
            Assert.IsNull(Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 2)), T0));
        }

        /// <summary>수령된 화물은 자리를 차지하면 안 된다. 아니면 상한이 영구 차단이 된다.</summary>
        [Test]
        public void ClaimedShipments_DoNotCountTowardTheCap()
        {
            var save = NewSave();
            int cap = _data.Transfer.Limits.MaxPendingShipments;

            for (int i = 0; i < cap; i++)
                save.Mail.Outbox.Add(new MailShipment { TxId = "tx" + i, QueuedAt = T0, Claimed = true });

            Assert.AreEqual(0, Outbox.PendingCount(save));

            Give(save, "MED16", 2);
            Assert.IsNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("MED16", 2)), T0),
                "전부 수령됐는데도 막혀 있다 — 상한이 영구 차단이 된다");
        }

        /// <summary>상한이 하루 발송 횟수보다 작으면 첫날부터 막힌다.</summary>
        [Test]
        public void PendingCap_LeavesRoomForAtLeastOneDay()
        {
            var limits = _data.Transfer.Limits;
            Assert.GreaterOrEqual(limits.MaxPendingShipments, limits.MaxShipmentsPerDay);
        }

        // ── 정상 경로 ────────────────────────────────────────────

        [Test]
        public void Queue_MovesItemsOutOfTheWarehouse()
        {
            var save = NewSave();
            Give(save, "MED16", 2);   // 단가 4,500

            var shipment = Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 2)), T0);

            Assert.IsNotNull(shipment);
            Assert.AreEqual(9000, shipment.TotalValue);
            Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, "MED16"), "보냈으면 창고에서 빠진다");
            Assert.AreEqual(1, save.Mail.Outbox.Count);
            Assert.AreEqual(1, save.Mail.DailyShipmentsUsed);
            Assert.AreEqual(9000, save.Mail.DailyQuotaUsedValue);
        }

        /// <summary>거래 id 는 유일해야 한다. 본편이 중복 수령을 이걸로 막는다.</summary>
        [Test]
        public void TxIds_AreUnique()
        {
            var save = NewSave();
            Give(save, "MED16", 2);

            var a = Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 1)), T0);
            var b = Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 1)), T0);

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.AreNotEqual(a.TxId, b.TxId);
        }

        // ── 가치 상한 (핵심) ─────────────────────────────────────

        /// <summary>
        /// 수량은 정책상 허용인데 가치가 상한을 넘는 경우. 여기가 뚫리면 본편 경제가 열린다.
        /// </summary>
        [Test]
        public void ShipmentValueCeiling_HoldsEvenWhenCountIsAllowed()
        {
            var save = NewSave();
            // AMO01 단가 1,500 × 60발 = 90,000원. 카테고리 수량 상한(60)은 통과한다.
            Give(save, "AMO01", 60);

            var items = Ship(new ItemStack("AMO01", 60));
            long value = Outbox.ValueOf(_data, items);

            Assert.Greater(value, _data.Transfer.Limits.MaxShipmentValue, "테스트 전제: 한도를 넘는 가치");
            Assert.IsNotNull(Outbox.BlockReason(save, _data, items, T0));
            Assert.IsNull(Outbox.TryQueue(save, _data, items, T0));
            Assert.AreEqual(60, Warehouse.CountOf(save.Warehouse, "AMO01"), "막혔으면 창고가 그대로여야 한다");
            Assert.AreEqual(0, save.Mail.Outbox.Count);
        }

        [Test]
        public void DailyValueCeiling_AccumulatesAcrossShipments()
        {
            var save = NewSave();
            Give(save, "JUNK03", 4);   // 단가 15,000

            // 1회 한도 25,000 → 한 번에 1개씩만 보낼 수 있다.
            Assert.IsNotNull(Outbox.TryQueue(save, _data, Ship(new ItemStack("JUNK03", 1)), T0));
            Assert.IsNotNull(Outbox.TryQueue(save, _data, Ship(new ItemStack("JUNK03", 1)), T0));

            // 발송 횟수 한도(2회) 에서 막힌다.
            var reason = Outbox.BlockReason(save, _data, Ship(new ItemStack("JUNK03", 1)), T0);
            Assert.IsNotNull(reason);
            Assert.AreEqual(30000, save.Mail.DailyQuotaUsedValue);
        }

        [Test]
        public void UnitPriceCeiling_BlocksExpensiveItems()
        {
            var def = _data.GetItem("JUNK03");
            Assert.IsNotNull(def);
            Assert.LessOrEqual(def.BasePrice, _data.Transfer.UnitPriceCeiling,
                "볼트는 단가 상한 이하라 보낼 수 있어야 한다");

            // 단가 상한을 넘는 물건을 하나 만들어 확인한다.
            var pricey = new ItemDef
            {
                Id = "JUNK99", Category = "Junk", BasePrice = _data.Transfer.UnitPriceCeiling + 1,
                MaxStack = 1, Tags = new[] { "부품" },
            };
            Assert.IsNotNull(Outbox.ItemBlockReason(_data.Transfer, pricey));
            Assert.IsFalse(Outbox.CanSend(_data.Transfer, pricey));
        }

        // ── 분류와 차단 목록 ─────────────────────────────────────

        [Test]
        public void Equipment_CannotBeSentToTheMainline()
        {
            var save = NewSave();
            Give(save, "WPN01", 1);

            Assert.IsNotNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("WPN01", 1)), T0),
                "무기를 본편으로 보낼 수 있으면 안 된다");
            Assert.IsFalse(Outbox.CanSend(_data.Transfer, _data.GetItem("AMR04")));
            Assert.IsFalse(Outbox.CanSend(_data.Transfer, _data.GetItem("MEL01")));
        }

        [Test]
        public void DenyList_BeatsAnAllowedCategory()
        {
            var save = NewSave();
            Give(save, "JUNK16", 1);   // JUNK 는 허용이지만 개별 차단 목록에 있다

            Assert.IsFalse(Outbox.CanSend(_data.Transfer, _data.GetItem("JUNK16")));
            Assert.IsNotNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("JUNK16", 1)), T0));
        }

        [Test]
        public void CategoryOf_ReadsThePrefix()
        {
            Assert.AreEqual("AMO", Outbox.CategoryOf("AMO01"));
            Assert.AreEqual("JUNK", Outbox.CategoryOf("JUNK03"));
            Assert.AreEqual("JUNK_CIG", Outbox.CategoryOf("JUNK_CIG"));
            Assert.AreEqual("", Outbox.CategoryOf(null));
        }

        [Test]
        public void CategoryCount_IsCapped()
        {
            var save = NewSave();
            Give(save, "MED16", 5);   // MED 는 1회 3개까지

            Assert.IsNotNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("MED16", 5)), T0));
            Assert.IsNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("MED16", 3)), T0));
        }

        // ── 방어적인 것들 ────────────────────────────────────────

        [Test]
        public void CannotSendWhatYouDoNotHave()
        {
            var save = NewSave();
            Give(save, "MED16", 1);

            Assert.IsNotNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("MED16", 2)), T0));
            Assert.IsNotNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("AMO01", 1)), T0));
        }

        [Test]
        public void RejectsEmptyDuplicateAndZeroCount()
        {
            var save = NewSave();
            Give(save, "MED16", 4);

            Assert.IsNotNull(Outbox.BlockReason(save, _data, Ship(), T0), "빈 화물");
            Assert.IsNotNull(Outbox.BlockReason(save, _data, null, T0));
            Assert.IsNotNull(Outbox.BlockReason(save, _data,
                Ship(new ItemStack("MED16", 1), new ItemStack("MED16", 1)), T0), "같은 물건 두 번");
            Assert.IsNotNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("MED16", 0)), T0));
        }

        [Test]
        public void StackCount_IsCapped()
        {
            var save = NewSave();
            foreach (var id in new[] { "MED16", "AMO01", "JUNK03", "FOOD01" })
                Give(save, id, 1);

            // MaxItemStacksPerShipment = 4 이므로 5종은 거절.
            var five = Ship(
                new ItemStack("MED16", 1), new ItemStack("AMO01", 1), new ItemStack("JUNK03", 1),
                new ItemStack("FOOD01", 1), new ItemStack("MED16", 1));
            Assert.IsNotNull(Outbox.BlockReason(save, _data, five, T0));
        }

        // ── 일일 한도 초기화 ─────────────────────────────────────

        /// <summary>
        /// 날짜가 바뀌면 한도가 풀린다. 게임 날짜 경계는 KST 05:00 이지 자정이 아니다.
        /// </summary>
        [Test]
        public void DailyQuota_ResetsOnTheGameDayBoundary()
        {
            var save = NewSave();
            Give(save, "JUNK03", 4);

            Outbox.TryQueue(save, _data, Ship(new ItemStack("JUNK03", 1)), T0);
            Outbox.TryQueue(save, _data, Ship(new ItemStack("JUNK03", 1)), T0);
            Assert.AreEqual(0, Outbox.RemainingShipments(save, _data, T0), "오늘 횟수를 다 썼다");

            // 같은 날 안에서는 안 풀린다 (KST 10:00 → 23:00).
            var sameDay = T0.AddHours(13);
            Assert.AreEqual(0, Outbox.RemainingShipments(save, _data, sameDay));

            // 다음 게임 날짜. T0 는 KST 2026-09-11 10:00 이므로 다음 경계는
            // KST 2026-09-12 05:00 = UTC 2026-09-11 20:00 이다 (자정이 아니다).
            var nextDay = GameTime.StartOfGameDate(new GameDate(2026, 9, 12)).AddMinutes(1);
            Assert.AreEqual(_data.Transfer.Limits.MaxShipmentsPerDay,
                Outbox.RemainingShipments(save, _data, nextDay), "날짜가 바뀌면 풀려야 한다");

            Assert.IsNotNull(Outbox.TryQueue(save, _data, Ship(new ItemStack("JUNK03", 1)), nextDay));
            Assert.AreEqual(15000, save.Mail.DailyQuotaUsedValue, "새 날의 사용량만 남아야 한다");
        }

        // ── 세션 경유 ────────────────────────────────────────────

        [Test]
        public void Session_QueueShipmentPersists()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var codec = new NewtonsoftJsonCodec();

            var session = new GameSession(new SaveService(files, codec, clock), _data, clock);
            session.Boot();
            // 통로가 없으면 연결 자체가 거절된다 (IMailLink). 테스트에서는 가짜를 꽂는다.
            session.MailLink = new DebugMailLink();
            session.LinkToMainline(null, (ok, _) => Assert.IsTrue(ok, "연결 실패"));
            Give(session.Save, "MED16", 1);

            var shipment = session.QueueShipment(Ship(new ItemStack("MED16", 1)));
            Assert.IsNotNull(shipment);
            session.Suspend();

            var reopened = new GameSession(new SaveService(files, codec, clock), _data, clock);
            reopened.Boot();

            Assert.AreEqual(1, reopened.Save.Mail.Outbox.Count);
            Assert.AreEqual(shipment.TxId, reopened.Save.Mail.Outbox[0].TxId);
            Assert.AreEqual(1, reopened.Save.Mail.Outbox[0].Items.Count);
            Assert.AreEqual("MED16", reopened.Save.Mail.Outbox[0].Items[0].ItemId);
        }

        // ── 연결 ─────────────────────────────────────────────────

        /// <summary>
        /// 연결 전에는 보낼 수 없다. <b>연동은 선택이다</b> (GDD §12) — 연결하지 않은 사람이
        /// 발송 버튼을 눌러 물건이 사라지는 일이 있어서는 안 된다.
        /// </summary>
        [Test]
        public void Unlinked_CannotSendAnything()
        {
            var save = NewSave();
            save.Mail.Linked = false;
            Give(save, "MED16", 2);

            Assert.IsNotNull(Outbox.BlockReason(save, _data, Ship(new ItemStack("MED16", 2)), T0));
            Assert.IsNull(Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 2)), T0));
            Assert.AreEqual(2, Warehouse.CountOf(save.Warehouse, "MED16"), "막혔으면 창고가 그대로여야 한다");
            Assert.AreEqual(0, save.Mail.Outbox.Count);
        }

        // ── 복제 방지 (LINK_CONTRACT §9) ─────────────────────────

        /// <summary>
        /// <b>발송 직후 앱이 죽어도 창고에서 이미 빠져 있어야 한다.</b>
        ///
        /// <para>이게 뒤집히면 물건이 발송함과 창고 양쪽에 존재하게 되고, 그게 본편 경제를
        /// 부수는 복제다. 되돌릴 수 없는 종류의 사고라 P5 의 출시 조건에 들어 있다.</para>
        /// </summary>
        [Test]
        public void KilledRightAfterSending_TheItemsAreAlreadyGone()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var codec = new NewtonsoftJsonCodec();

            var session = new GameSession(new SaveService(files, codec, clock), _data, clock);
            session.Boot();
            // 통로가 없으면 연결 자체가 거절된다 (IMailLink). 테스트에서는 가짜를 꽂는다.
            session.MailLink = new DebugMailLink();
            session.LinkToMainline(null, (ok, _) => Assert.IsTrue(ok, "연결 실패"));
            Give(session.Save, "MED16", 3);

            Assert.IsNotNull(session.QueueShipment(Ship(new ItemStack("MED16", 2))));
            // Suspend 를 부르지 않는다 — 앱이 그냥 죽은 상황이다.

            var reopened = new GameSession(new SaveService(files, codec, clock), _data, clock);
            reopened.Boot();

            Assert.AreEqual(1, Warehouse.CountOf(reopened.Save.Warehouse, "MED16"),
                "보낸 2개가 창고에 남아 있으면 물건이 두 곳에 존재한다");
            Assert.AreEqual(1, reopened.Save.Mail.Outbox.Count);
        }

        /// <summary>막힌 발송은 창고를 건드리지 않는다 — 네트워크가 없거나 한도가 찼을 때의 경로.</summary>
        [Test]
        public void ABlockedSend_LeavesTheWarehouseUntouched()
        {
            var save = NewSave();
            Give(save, "MED16", 2);

            // 한도를 넘기는 화물. 어떤 이유로 막히든 결과는 같아야 한다.
            var tooMuch = Ship(new ItemStack("MED16", 2), new ItemStack("JUNK03", 5));

            Assert.IsNull(Outbox.TryQueue(save, _data, tooMuch, T0));
            Assert.AreEqual(2, Warehouse.CountOf(save.Warehouse, "MED16"));
            Assert.AreEqual(0, save.Mail.DailyShipmentsUsed, "막힌 발송이 한도를 깎으면 안 된다");
        }

        /// <summary>txId 는 재사용 금지. 같은 게 두 번 나오면 본편이 둘째를 중복으로 버린다.</summary>
        [Test]
        public void EveryShipment_GetsItsOwnTxId()
        {
            var save = NewSave();
            Give(save, "MED16", 4);

            var first = Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 1)), T0);
            var second = Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 1)), T0);

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreNotEqual(first.TxId, second.TxId);
        }

        // ── 체크섬 ───────────────────────────────────────────────

        [Test]
        public void Shipments_CarryAMatchingChecksum()
        {
            var save = NewSave();
            Give(save, "MED16", 2);

            var shipment = Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 2)), T0);

            Assert.IsNotNull(shipment.Checksum);
            StringAssert.StartsWith(ShipmentChecksum.Prefix, shipment.Checksum);
            Assert.IsTrue(ShipmentChecksum.Matches(shipment, save.SchemaVersion));
        }

        /// <summary>내용이 바뀌면 체크섬이 어긋난다 — 본편의 P4 가 걸러낼 수 있어야 한다.</summary>
        [Test]
        public void ChangingTheContents_BreaksTheChecksum()
        {
            var save = NewSave();
            Give(save, "MED16", 2);

            var shipment = Outbox.TryQueue(save, _data, Ship(new ItemStack("MED16", 2)), T0);
            shipment.Items[0] = new ItemStack("MED16", 999);

            Assert.IsFalse(ShipmentChecksum.Matches(shipment, save.SchemaVersion));
        }

        /// <summary>
        /// 고른 순서가 달라도 같은 화물이면 같은 체크섬이어야 한다.
        /// 목록 순서는 플레이어가 누른 순서라 같은 내용도 실행마다 다르다.
        /// </summary>
        [Test]
        public void ItemOrder_DoesNotChangeTheChecksum()
        {
            var a = new List<ItemStack> { new ItemStack("MED16", 2), new ItemStack("JUNK03", 1) };
            var b = new List<ItemStack> { new ItemStack("JUNK03", 1), new ItemStack("MED16", 2) };

            Assert.AreEqual(
                ShipmentChecksum.Compute("m2p_20260911_a7f3e9c1", 2, a),
                ShipmentChecksum.Compute("m2p_20260911_a7f3e9c1", 2, b));
        }

        /// <summary>
        /// 직렬화 형식을 글자 그대로 고정한다.
        ///
        /// <para><b>본편과 한 글자라도 다르면 모든 배송이 조용히 폐기된다.</b> 구분자든 정렬이든
        /// 대소문자든, 증상은 "보냈는데 안 온다" 하나뿐이라 어디가 틀렸는지 알 길이 없다.
        /// 그래서 형식 자체를 테스트로 못 박는다 — 이 값이 바뀌면 본편도 같이 고쳐야 한다.</para>
        /// </summary>
        [Test]
        public void ChecksumPayload_HasAFixedShape()
        {
            var items = new List<ItemStack> { new ItemStack("MED16", 2), new ItemStack("AMO01", 30) };

            Assert.AreEqual(
                "m2p_20260911_a7f3e9c1|2|AMO01x30;MED16x2",
                ShipmentChecksum.Payload("m2p_20260911_a7f3e9c1", 2, items));
        }
    }
}
