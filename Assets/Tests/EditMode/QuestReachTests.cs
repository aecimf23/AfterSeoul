using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Quest;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 못 깨는 의뢰가 걸리지 않는가.
    ///
    /// <para>티어 숫자만 보고 거르면 "레벨 1 에게 구로에서만 나오는 탄약 30발"이 걸린다.
    /// 실제로 그 상태였다. 이 파일은 그 경로 검사가 살아 있는지 본다.</para>
    /// </summary>
    [TestFixture]
    public class QuestReachTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave()
        {
            return new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = 0, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
        }

        private static QuestDef Quest(params QuestRequirement[] reqs) => new QuestDef
        {
            Id = "Q_TEST", Tier = 1, Requires = reqs,
            Touches = new[] { "expedition", "warehouse" },
        };

        private static QuestRequirement Item(string id, int count) =>
            new QuestRequirement { ItemId = id, Count = count };

        private static QuestRequirement Tag(string tag, int count) =>
            new QuestRequirement { Tag = tag, Count = count };

        // ── 지역 해금 ────────────────────────────────────────────

        [Test]
        public void ItemFromAnOpenMap_IsAchievable()
        {
            // MYEONGDONG 은 조건 없이 열려 있고 LT_GURO 를 쓴다 (FakeRegistry).
            Assert.IsTrue(QuestReach.IsAchievable(NewSave(), _data, Quest(Item("JUNK03", 5))));
        }

        /// <summary>원래 버그를 그대로 재현한다 — 잠긴 지역에서만 나오는 물건을 요구하는 의뢰.</summary>
        [Test]
        public void ItemOnlyInALockedMap_IsNotAchievable()
        {
            var save = NewSave();
            // FOOD01 은 UIJEONGBU(레벨 9 잠김) 표에만 있다.
            Assert.IsFalse(QuestReach.IsAchievable(save, _data, Quest(Item("FOOD01", 1))),
                "레벨 1 에게 의정부 전용 물건을 요구하면 못 깨는 의뢰다");

            save.Player.Level = 9;
            Assert.IsTrue(QuestReach.IsAchievable(save, _data, Quest(Item("FOOD01", 1))),
                "지역이 열리면 구할 수 있게 된다");
        }

        [Test]
        public void ItemAlreadyInTheWarehouse_IsAchievable()
        {
            var save = NewSave();
            Warehouse.TryAdd(save.Warehouse, _data, "FOOD01", 2);

            Assert.IsTrue(QuestReach.IsAchievable(save, _data, Quest(Item("FOOD01", 1))),
                "어제 주워 둔 것으로 오늘 의뢰를 채울 수 있어야 한다");
        }

        [Test]
        public void BuyableItem_IsAchievable()
        {
            var save = NewSave();
            // AMR10 은 신뢰도 0 상인(동대문 최씨)이 판다.
            Assert.IsTrue(QuestReach.IsAchievable(save, _data, Quest(Item("AMR10", 1))));

            // WPN22 는 신뢰도 5 상인(용산 김씨)이 판다.
            Assert.IsFalse(QuestReach.IsAchievable(save, _data, Quest(Item("WPN22", 1))),
                "아직 안 열린 상인의 물건은 못 구한다");

            save.NpcTrust["HWANG"] = 5;
            Assert.IsTrue(QuestReach.IsAchievable(save, _data, Quest(Item("WPN22", 1))),
                "신뢰도가 오르면 구할 수 있게 된다");
        }

        [Test]
        public void CraftableItem_IsAchievable()
        {
            var save = NewSave();
            // RCP_BOLT: JUNK16 → JUNK03. JUNK16 은 열린 지역에서 나온다.
            Assert.IsTrue(QuestReach.IsAchievable(save, _data, Quest(Item("JUNK03", 5))));
        }

        [Test]
        public void TagRequirement_LooksAtReachableItems()
        {
            var save = NewSave();
            Assert.IsTrue(QuestReach.IsAchievable(save, _data, Quest(Tag("금속", 4))),
                "금속 태그를 가진 물건이 열린 지역에서 나온다");
            Assert.IsFalse(QuestReach.IsAchievable(save, _data, Quest(Tag("없는태그", 1))));
        }

        [Test]
        public void AllRequirementsMustBeReachable()
        {
            var save = NewSave();
            Assert.IsFalse(
                QuestReach.IsAchievable(save, _data, Quest(Item("JUNK03", 1), Item("FOOD01", 1))),
                "하나라도 못 구하면 못 깨는 의뢰다");
        }

        // ── 실제 데이터 ──────────────────────────────────────────

        /// <summary>
        /// <b>이 테스트가 원래 있었어야 했다.</b> 새 세이브(레벨 1 · 신뢰도 0)에서
        /// 티어 1 의뢰가 전부 달성 가능한가.
        /// </summary>
        [Test]
        public void FreshSave_CanAchieveEveryTierOneQuest()
        {
            var save = NewSave();
            var pool = _data.GetQuestPool("DQP_HWANG");
            var stuck = new List<string>();

            foreach (var q in pool)
            {
                if (q.Tier != 1) continue;
                if (!QuestReach.IsAchievable(save, _data, q)) stuck.Add(q.Id);
            }

            Assert.IsEmpty(stuck,
                "레벨 1 플레이어에게 못 깨는 티어 1 의뢰가 걸린다: " + string.Join(", ", stuck.ToArray()));
        }
    }
}
