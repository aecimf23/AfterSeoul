using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Mail;
using AfterSeoul.Quest;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// <b>Vertical Slice 관문</b> (GDD §17).
    ///
    /// <para>여기 있는 것은 기능 테스트가 아니라 <b>한 판</b>이다. 새 세이브에서 시작해
    /// 의뢰 수령 → 공장에서 벌기 → 고용 → 파견 → 회수 → 판매 → 납품 → 보상 → 본편 발송까지
    /// 실제로 걸어본다. 기능별 테스트는 전부 녹색인데 게임은 막혀 있던 일이 세 번 있었다
    /// (무기 필수, 레벨업 없음, 못 깨는 의뢰). 그 셋 다 이 테스트가 있었으면 잡혔다.</para>
    ///
    /// <para>세션 하나로 끝까지 간다 — 중간에 상태를 손으로 만들지 않는다. 손으로 만드는 순간
    /// "그 상태에 도달할 수 있는가"를 검사하지 못하게 되고, 막다른 길은 바로 거기서 생긴다.</para>
    /// </summary>
    [TestFixture]
    public class VerticalSliceTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        [Test]
        public void FullCycle_FromNewSaveToMainlineOutbox()
        {
            var clock = new TestClock(T0);
            var session = new GameSession(
                new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), _data, clock);

            // ── 1. 새 게임 — 황 상사 배정과 오늘의 의뢰 ──────────────
            session.Boot();

            Assert.AreEqual("HWANG", session.Save.Player.EmployerNpcId, "고용주가 배정돼야 한다");
            Assert.Greater(session.Save.Quests.Active.Count, 0, "첫 부팅에 의뢰가 나와야 한다");
            Assert.AreEqual(0, session.Save.Player.Money, "빈손으로 시작한다");

            foreach (var active in session.Save.Quests.Active)
            {
                var def = FindQuest(active.QuestId);
                Assert.IsNotNull(def, active.QuestId + " 정의가 없다");
                Assert.IsTrue(QuestReach.IsAchievable(session.Save, _data, def),
                    $"첫날 의뢰 {active.QuestId} 를 깰 방법이 없다");
            }

            // ── 2. 공장에서 돈 벌기 ────────────────────────────────
            // 첫 고용비가 모일 때까지 두드린다. 무한 루프가 되면 그것도 알아야 하므로 상한을 둔다.
            long hireCost = FirstOfferCost(session);
            int taps = WorkUntil(session, () => session.Save.Player.Money >= hireCost, 200,
                "직접 노동만으로 첫 고용비가 모이지 않는다");

            Assert.Greater(session.Save.Player.Exp, 0, "노동도 경험치를 줘야 한다");

            // ── 3. 첫 스캐브 고용 ──────────────────────────────────
            var offer = FirstOpenOffer(session);
            Assert.IsNotNull(offer, "고용 시장에 후보가 있어야 한다");

            var scav = session.Hire(offer.OfferId);
            Assert.IsNotNull(scav, "첫 고용이 돼야 한다");
            Assert.IsTrue(Equipment.EffectsOf(scav, _data).HasWeapon,
                "고용한 사람이 맨손이면 파견을 못 간다");

            // ── 4. 파견 ────────────────────────────────────────────
            // 고용하고 나면 자금이 거의 남지 않는다 (계약금 250,000 / 명동 파견비 27,000).
            // 실제 플레이도 여기서 몇 번 더 두드리므로 테스트도 그렇게 걷는다.
            // 다만 그 횟수가 갑자기 늘면 첫 30분이 무너지므로 상한을 건다.
            taps += WorkUntil(session, () => FirstAffordableMap(session, scav.Uid) != null, 40,
                $"고용 후 아무리 일해도 갈 수 있는 지역이 없다 (소지금 {session.Save.Player.Money:N0}원)");

            Assert.Less(taps, 60, $"의뢰 한 바퀴까지 미니게임 {taps}회 — 첫 30분에 안 들어온다 (GDD §16)");

            string mapId = FirstAffordableMap(session, scav.Uid);
            var expedition = session.Depart(mapId, new[] { scav.Uid });
            Assert.IsNotNull(expedition);
            Assert.AreEqual(ScavStatus.OnExpedition, scav.Status);

            // ── 5. 회수 (앱을 닫았다 켠 셈) ─────────────────────────
            clock.Advance(TimeSpan.FromHours(3));
            var report = session.Resume();

            Assert.AreEqual(1, report.Expeditions.Count, "복귀가 정산돼야 한다");
            Assert.Greater(Warehouse.Snapshot(session.Save.Warehouse).Count, 0, "빈손으로 돌아오면 안 된다");

            // ── 6. 판매 ────────────────────────────────────────────
            var sellable = FirstStack(session);
            long before = session.Save.Player.Money;
            Assert.IsTrue(session.Sell(sellable.ItemId, 1), "회수품을 팔 수 있어야 한다");
            Assert.Greater(session.Save.Player.Money, before, "팔았으면 돈이 늘어야 한다");

            // ── 7. 납품 ────────────────────────────────────────────
            // 의뢰 요구품을 창고에 넣어 준다. "구할 수 있는가"는 QuestReach 가 따로 보고,
            // 여기서는 납품 → 보상 → 신뢰도 경로가 이어지는지만 본다.
            var quest = FindQuest(session.Save.Quests.Active[0].QuestId);
            GiveRequirements(session.Save, quest);

            long moneyBefore = session.Save.Player.Money;
            long expBefore = session.Save.Player.Exp;
            int trustBefore = Trust(session.Save);

            Assert.IsTrue(session.Deliver(quest.Id), "요구품을 다 갖췄으면 납품돼야 한다");

            // ── 8. 보상 ────────────────────────────────────────────
            Assert.AreEqual(moneyBefore + quest.RewardMoney, session.Save.Player.Money);
            Assert.AreEqual(expBefore + quest.RewardExp, session.Save.Player.Exp);
            Assert.AreEqual(trustBefore + quest.RewardTrust, Trust(session.Save),
                "신뢰도가 올라야 상점과 상위 의뢰가 열린다");
            CollectionAssert.Contains(session.Save.Quests.CompletedIds, quest.Id);

            // ── 9. 본편 발송함에 등록 ───────────────────────────────
            // 연동은 선택이므로(GDD §12) 먼저 연결한다. 연결 전에는 발송 자체가 막힌다.
            // 연동 통로가 안 붙은 빌드에서는 연결이 거절된다 — 그게 기본값이다 (IMailLink).
            // 여기서는 루프의 마지막 칸을 보는 것이 목적이라 가짜 통로를 꽂는다.
            session.MailLink = new AfterSeoul.Mail.DebugMailLink();
            session.LinkToMainline(null, (ok, message) => Assert.IsTrue(ok, message));

            var sendable = FirstSendableStack(session);
            Assert.IsNotNull(sendable,
                "창고에 본편으로 보낼 수 있는 물건이 하나도 없다 — 루프의 마지막 칸이 비어 있다");

            var shipment = session.QueueShipment(
                new List<ItemStack> { new ItemStack(sendable.Value.ItemId, 1) });

            Assert.IsNotNull(shipment, "발송함 등록이 돼야 한다");
            Assert.IsFalse(string.IsNullOrEmpty(shipment.TxId));
            Assert.AreEqual(1, session.Save.Mail.Outbox.Count);
            Assert.Greater(shipment.TotalValue, 0);
            Assert.IsTrue(AfterSeoul.Mail.ShipmentChecksum.Matches(shipment, session.Save.SchemaVersion),
                "본편이 P4 에서 체크섬으로 스키마 불일치를 거른다 — 여기서 안 찍히면 전부 폐기된다");

            // ── 10. 앱을 껐다 켜도 전부 남아 있는가 ──────────────────
            session.Suspend();
            Assert.AreEqual(1, session.Save.Mail.Outbox.Count);
        }

        /// <summary>
        /// 한 판을 끝내는 데 걸리는 조작 수. GDD §16 의 "첫 30분" 이 성립하는지 본다.
        ///
        /// <para>숫자를 정확히 맞히려는 게 아니라, 어느 날 갑자기 세 배가 되면 알아채려는 것이다.</para>
        /// </summary>
        [Test]
        public void FirstHire_DoesNotTakeTooManyTaps()
        {
            var clock = new TestClock(T0);
            var session = new GameSession(
                new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Boot();

            long hireCost = FirstOfferCost(session);
            int taps = WorkUntil(session, () => session.Save.Player.Money >= hireCost, 500,
                "첫 고용비가 모이지 않는다");

            Assert.Less(taps, 40,
                $"첫 고용까지 미니게임 {taps}회 — 첫 30분에 고용까지 가야 한다 (GDD §16)");
            Assert.Greater(taps, 3, "너무 쉬우면 공장을 만든 의미가 없다");
        }

        // ── 도우미 ───────────────────────────────────────────────

        /// <summary>
        /// 조건이 참이 될 때까지 만들고 판다. 소요 미니게임 횟수를 돌려준다.
        ///
        /// <para>작업대는 돈이 아니라 물건을 만든다. 그래서 "벌기"는 만들어서 파는 두 동작이고,
        /// 이 테스트도 그대로 걷는다 — 실제 플레이가 그렇기 때문이다.</para>
        /// </summary>
        private int WorkUntil(GameSession session, Func<bool> done, int cap, string failMessage)
        {
            int taps = 0;
            var recipe = _data.GetRecipe(StarterRecipeId);

            while (!done())
            {
                if (session.Save.Factory.Workbench.IsIdle)
                    Assert.IsTrue(session.StartWork(StarterRecipeId), "기초 제작을 시작할 수 없다");

                var result = session.AdvanceWork(1.0);
                taps++;
                Assert.Less(taps, cap, failMessage);

                if (!result.Completed) continue;

                // 만든 것을 판다. 돈은 여기서 나온다.
                int stored = result.Output.Count - result.Overflow;
                if (stored > 0) session.Sell(result.Output.ItemId, stored);
            }
            return taps;
        }

        /// <summary>재료 없이 만들 수 있는 기초 제작. 빈손 플레이어의 유일한 입구다.</summary>
        private string StarterRecipeId
        {
            get
            {
                foreach (var r in _data.AllRecipes)
                    if (r.ManualSteps > 0 && r.Inputs.Length == 0) return r.Id;

                Assert.Fail("재료 없이 만들 수 있는 레시피가 없다 — 빈손으로 시작하면 아무것도 못 한다");
                return null;
            }
        }

        private QuestDef FindQuest(string id)
        {
            foreach (var q in _data.GetQuestPool("DQP_HWANG"))
                if (q.Id == id) return q;
            return null;
        }

        private static int Trust(GameSave save)
        {
            int v;
            return save.NpcTrust.TryGetValue(save.Player.EmployerNpcId, out v) ? v : 0;
        }

        private static ScavOffer FirstOpenOffer(GameSession session)
        {
            foreach (var o in session.Save.Market.Offers)
                if (!o.Hired) return o;
            return null;
        }

        private static long FirstOfferCost(GameSession session)
        {
            var offer = FirstOpenOffer(session);
            return offer != null ? offer.HireCost : long.MaxValue;
        }

        /// <summary>지금 자금으로 갈 수 있는 지역 중 제일 싼 곳.</summary>
        private string FirstAffordableMap(GameSession session, string scavUid)
        {
            string best = null;
            long bestCost = long.MaxValue;

            foreach (var map in _data.AllMaps)
            {
                var team = new[] { scavUid };
                if (ExpeditionSystem.DepartBlockReason(session.Save, _data, map.Id, team) != null) continue;

                long cost = ExpeditionSystem.CostFor(session.Save, _data, map, team);
                if (cost >= bestCost) continue;

                bestCost = cost;
                best = map.Id;
            }
            return best;
        }

        private static ItemStack FirstStack(GameSession session)
            => Warehouse.Snapshot(session.Save.Warehouse)[0];

        private ItemStack? FirstSendableStack(GameSession session)
        {
            foreach (var stack in Warehouse.Snapshot(session.Save.Warehouse))
            {
                var def = _data.GetItem(stack.ItemId);
                if (Outbox.CanSend(_data.Transfer, def)) return stack;
            }
            return null;
        }

        /// <summary>의뢰 요구품을 창고에 넣어 준다.</summary>
        private void GiveRequirements(GameSave save, QuestDef quest)
        {
            save.Warehouse.Capacity = 200;   // 넣다가 넘치지 않게

            foreach (var req in quest.Requires)
            {
                string itemId = req.ItemId;

                if (string.IsNullOrEmpty(itemId))
                {
                    // 태그 조건이면 그 태그를 가진 아이템을 아무거나 하나 고른다.
                    foreach (var def in _data.Items)
                    {
                        if (def.Tags == null) continue;
                        bool match = false;
                        foreach (var t in def.Tags) if (t == req.Tag) { match = true; break; }
                        if (match) { itemId = def.Id; break; }
                    }
                }

                Assert.IsFalse(string.IsNullOrEmpty(itemId), "의뢰 요구 조건이 비어 있다");
                Warehouse.TryAdd(save.Warehouse, _data, itemId, req.Count);
            }
        }
    }
}
