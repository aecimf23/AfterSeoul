using System;
using System.Collections.Generic;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 세이브 루트. <b>게임의 변경 가능한 상태는 전부 여기 안에만 있다.</b>
    ///
    /// 시스템 클래스(FactorySystem 등)는 자기 상태를 들고 있지 않는다. 전부 stateless 이고
    /// 이 객체를 받아서 읽고 쓴다. 1인 개발에서 싱글턴이 각자 상태를 들고 있기 시작하면
    /// "세이브에는 있는데 메모리에는 없는" 불일치가 반드시 생기고, 그 버그는 재현이 안 된다.
    ///
    /// 스키마는 DATA_SCHEMA.md §2 와 같이 간다.
    /// </summary>
    [Serializable]
    public sealed class GameSave
    {
        public int SchemaVersion = 1;

        /// <summary>마지막으로 정산이 끝난 시각. 다음 정산의 시작점.</summary>
        public DateTimeOffset SavedAt;

        /// <summary>기기 시계가 뒤로 간 횟수. 처벌하지 않고 기록만 한다.</summary>
        public int ClockAnomalyCount;

        /// <summary>파견/시장 등에 쓸 시드를 뽑는 카운터. 뽑을 때마다 증가.</summary>
        public uint RngCounter = 1;

        public PlayerState Player = new PlayerState();
        public WarehouseState Warehouse = new WarehouseState();
        public FactoryState Factory = new FactoryState();
        public List<ScavState> Scavs = new List<ScavState>();
        public List<ExpeditionState> Expeditions = new List<ExpeditionState>();
        public QuestState Quests = new QuestState();
        public Dictionary<string, int> NpcTrust = new Dictionary<string, int>();
        public MailState Mail = new MailState();

        /// <summary>새 시드를 하나 발급한다. 반드시 이 경로로만 뽑는다.</summary>
        public uint TakeSeed()
        {
            unchecked
            {
                // 카운터를 그대로 시드로 쓰면 연속된 파견의 결과가 서로 닮는다.
                // 곱셈 해시 한 번 태워서 흩어준다.
                uint seed = RngCounter * 2654435761u;
                RngCounter++;
                return seed == 0 ? 1u : seed;
            }
        }
    }

    [Serializable]
    public sealed class PlayerState
    {
        public int Level = 1;
        public long Exp;
        public long Money;
        public string EmployerNpcId;
        public DateTimeOffset CreatedAt;
    }

    [Serializable]
    public sealed class WarehouseState
    {
        public int Capacity = 60;
        public List<ItemStack> Stacks = new List<ItemStack>();
    }

    [Serializable]
    public struct ItemStack
    {
        public string ItemId;
        public int Count;

        public ItemStack(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }
    }

    [Serializable]
    public sealed class FactoryState
    {
        public int StationLevel = 1;
        public int AutoLevel;
        public List<CraftJob> Queue = new List<CraftJob>();

        /// <summary>자동 생산의 마지막 정산 시각. 오프라인 생산 계산의 기준점.</summary>
        public DateTimeOffset LastCollectedAt;
    }

    [Serializable]
    public sealed class CraftJob
    {
        public string RecipeId;
        public DateTimeOffset StartedAt;
        public DateTimeOffset CompletesAt;

        /// <summary>출발 시점에 고정. 품질 판정에 쓴다.</summary>
        public uint Seed;

        /// <summary>수령 완료 표시. 중복 지급 방지.</summary>
        public bool Collected;
    }

    [Serializable]
    public sealed class ScavState
    {
        public string Uid;
        public string Name;
        public int Level = 1;
        public int Search, Combat, Survival;
        public List<string> TraitIds = new List<string>();
        public ScavStatus Status = ScavStatus.Idle;
        public Dictionary<string, string> Equipment = new Dictionary<string, string>();
        public int ExpeditionCount;
        public long TotalLootValue;
        public DateTimeOffset HiredAt;
    }

    public enum ScavStatus { Idle, OnExpedition, Injured, Treating, Missing, Dead }

    [Serializable]
    public sealed class ExpeditionState
    {
        public string Uid;
        public string MapId;
        public List<string> ScavUids = new List<string>();
        public DateTimeOffset DepartedAt;
        public DateTimeOffset ReturnsAt;
        public long CostPaid;

        /// <summary>
        /// <b>출발 시점에 확정해 저장한다.</b> 결과는 이 시드의 함수이지 정산 시각의 함수가 아니다.
        /// 그래서 앱을 껐다 켜도, 시계를 앞당겨도, 복귀 직전에 강제 종료해도 결과가 같다.
        /// 리세마라가 성립하지 않는다.
        /// </summary>
        public uint Seed;

        /// <summary>정산 완료 표시. 중복 지급 방지의 마지막 방어선.</summary>
        public bool Resolved;
    }

    [Serializable]
    public sealed class QuestState
    {
        /// <summary>현재 의뢰가 속한 게임 날짜. 이게 오늘과 다르면 갱신한다.</summary>
        public string ActiveGameDate;

        public List<ActiveQuest> Active = new List<ActiveQuest>();
        public List<string> CompletedIds = new List<string>();
    }

    [Serializable]
    public sealed class ActiveQuest
    {
        public string QuestId;
        public bool Delivered;
    }

    [Serializable]
    public sealed class MailState
    {
        public List<string> OutboxTxIds = new List<string>();
        public string DailyQuotaGameDate;
        public long DailyQuotaUsedValue;
        public int DailyShipmentsUsed;
    }
}
