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

        // -1 keeps existing saves opted out; only SaveService.CreateNew starts the briefing.
        public int WelcomePage = -1;
        public List<string> LearnedMinigames = new List<string>();
        public string TrackedQuestId;
        public OrientationState Orientation;
        public StarterSupportState Starter;
        public List<string> ExploredMapIds = new List<string>();

        /// <summary>마지막으로 정산이 끝난 시각. 다음 정산의 시작점.</summary>
        public DateTimeOffset SavedAt;

        /// <summary>기기 시계가 뒤로 간 횟수. 처벌하지 않고 기록만 한다.</summary>
        public int ClockAnomalyCount;

        /// <summary>파견/시장 등에 쓸 시드를 뽑는 카운터. 뽑을 때마다 증가.</summary>
        public uint RngCounter = 1;

        public AfterSeoul.Exploration.ExplorationState Exploration;
        public AfterSeoul.Exploration.RaidBaseProgress RaidBase = new AfterSeoul.Exploration.RaidBaseProgress();
        public List<ItemStack> ExplorationOverflow = new List<ItemStack>();
        public int ExplorationTutorialSeen;
        public bool ExplorationStarterClaimed;
        public bool ExplorationStarterPrepared;
        public List<string> SurvivedExplorationMapIds = new List<string>();
        public FirstExplorationQuestProgress FirstExplorationQuest = new FirstExplorationQuestProgress();
        public Dictionary<string, RegionalQuestProgress> RegionalExplorationQuests = new Dictionary<string, RegionalQuestProgress>();
        public PlayerState Player = new PlayerState();
        public WarehouseState Warehouse = new WarehouseState();
        public FactoryState Factory = new FactoryState();
        public List<ScavState> Scavs = new List<ScavState>();
        public List<ExpeditionState> Expeditions = new List<ExpeditionState>();
        public QuestState Quests = new QuestState();
        public MarketState Market = new MarketState();
        public Dictionary<string, int> NpcTrust = new Dictionary<string, int>();
        public MailState Mail = new MailState();

        /// <summary>월간 지원계약·보상 광고 (GDD §11). 없어도 게임 전 구간이 돈다.</summary>
        public SupportState Support = new SupportState();

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
        public string Name;
        public static bool ValidName(string value)
        {
            if(string.IsNullOrWhiteSpace(value)) return false;
            value=value.Trim();
            if(new System.Globalization.StringInfo(value).LengthInTextElements>16) return false;
            foreach(char c in value) if(char.IsControl(c) || c=='<' || c=='>') return false;
            return true;
        }
        public int Level = 1;
        public long Exp;
        public int CharacterLevel = 1;
        public long CharacterExp;
        public double Hp = 100, Hydration = 100, Energy = 100;
        public Dictionary<string, string> Equipment = new Dictionary<string, string>();
        public long Money;
        public string EmployerNpcId;
        public DateTimeOffset CreatedAt;
    }

    [Serializable]
    public sealed class WarehouseState
    {
        /// <summary>본래 칸 수. 성장으로 늘어나는 값이고, 세이브에 남는다.</summary>
        public int Capacity = 60;

        /// <summary>
        /// 지원계약으로 얹힌 칸 (GDD §11). <b>파생값이라 세이브에 담지 않는다.</b>
        ///
        /// <para>저장해 두면 계약이 만료된 뒤에도 칸이 남아서, 돈을 안 내는 사람에게 혜택이
        /// 계속 간다. 여기서는 정산이 돌 때마다(<c>GameSession.ResolveNow</c>) 계약 상태를 보고
        /// 다시 계산한다 — 시간이 흐르는 곳이 거기 하나뿐이라 어긋날 데가 없다.</para>
        ///
        /// <para><b>왜 필요했나:</b> <c>Support.WarehouseBonus</c> 는 있었지만 부르는 코드가
        /// 한 군데도 없었다. 창고 코드는 <see cref="Capacity"/> 를 날것으로 읽었고, 그래서
        /// 화면에 "창고 +40칸"이라고 적어 팔면서 실제로는 한 칸도 안 늘었다 —
        /// 제작 큐 +1칸이 화면에만 안 보이던 것과 같은 부류의, 그러나 더 나쁜 버그다.</para>
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public int BonusCapacity;

        /// <summary>실제로 쓸 수 있는 칸. <b>창고 코드는 항상 이걸 본다.</b></summary>
        [Newtonsoft.Json.JsonIgnore]
        public int TotalCapacity => Capacity + BonusCapacity;

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
        public AfterSeoul.Factory.ProductionState Production = new AfterSeoul.Factory.ProductionState();
        public int StationLevel = 1;

        /// <summary>고용한 보조 인력 수. 없는 동안 <see cref="AutoRecipeId"/> 를 사람 수만큼 만든다.</summary>
        public int AutoLevel;

        /// <summary>보조 인력이 만들 레시피. 비어 있으면 아무도 아무것도 만들지 않는다.</summary>
        public string AutoRecipeId;

        public List<CraftJob> Queue = new List<CraftJob>();

        /// <summary>지금 손으로 만들고 있는 것. 없으면 <c>RecipeId</c> 가 비어 있다.</summary>
        public WorkbenchState Workbench = new WorkbenchState();

        /// <summary>자동 생산의 마지막 정산 시각. 오프라인 생산 계산의 기준점.</summary>
        public DateTimeOffset LastCollectedAt;
    }

    /// <summary>
    /// 작업대에서 손으로 만드는 중인 물건.
    ///
    /// <para>직접 노동이 "돈 버는 탭"이 아니라 <b>물건 만드는 탭</b>인 이유: 두드려도
    /// 아무것도 쌓이지 않으면 그건 놀이가 아니라 노동이다. 한 번의 미니게임이 공정을
    /// 한 단계 밀고, 단계가 차면 물건이 나온다.</para>
    ///
    /// <para>세이브에 남는다. 두 단계 해놓고 앱을 닫았다가 다시 열면 이어서 한다.</para>
    /// </summary>
    [Serializable]
    public sealed class WorkbenchState
    {
        public string RecipeId;

        /// <summary>끝낸 공정 수.</summary>
        public int StepsDone;

        /// <summary>
        /// 각 단계의 점수(0~1). 평균이 완성품의 품질을 정하고, 품질이 산출 개수를 바꾼다.
        /// 합계가 아니라 목록으로 두는 이유는 화면이 단계별 성적을 그대로 보여주기 때문이다.
        /// </summary>
        public List<double> Scores = new List<double>();

        public bool IsIdle => string.IsNullOrEmpty(RecipeId);

        public void Clear()
        {
            RecipeId = null;
            StepsDone = 0;
            Scores.Clear();
        }
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

        /// <summary>고용 당시 티어. 표시용이자, 나중에 재계약·승급 규칙의 기준.</summary>
        public int Tier = 1;

        /// <summary>시급. 지금은 표시만 하고, 파견비는 지역 고정값을 쓴다 (P2 밸런스에서 합칠 것).</summary>
        public long WagePerHour;
        public int Search, Combat, Survival;
        public List<string> TraitIds = new List<string>();
        public ScavStatus Status = ScavStatus.Idle;
        public Dictionary<string, string> Equipment = new Dictionary<string, string>();
        public int ExpeditionCount;
        public long TotalLootValue;
        public DateTimeOffset HiredAt;

        // ── 실종 (GDD §15 — 구조 임무) ──
        //
        // 실종이 명단에서 이름이 회색이 되는 것으로 끝나면 그건 사건이 아니라 삭제다.
        // 다시 데려올 수 있어야 잃는 것이 무게를 갖는다.

        /// <summary>실종된 시각. 여기서 일정 시간 뒤에 무전이 잡힌다.</summary>
        public DateTimeOffset LostAt;

        /// <summary>어디서 잃었는가. 구조하러 갈 곳이자, 얼마나 어려운지의 기준.</summary>
        public string LostAtMapId;

        /// <summary>무전이 잡힌 시각. 비어 있으면 아직(또는 영영) 안 잡혔다.</summary>
        public DateTimeOffset SignalAt;

        // ── 치료 (GDD §15) ──

        /// <summary>
        /// 치료가 끝나는 시각. <see cref="ScavStatus.Treating"/> 일 때만 뜻이 있다.
        ///
        /// <para><b>이 필드가 부상에서 나오는 문이다.</b> 없던 동안 <c>Injured</c> 는
        /// 들어가기만 하고 나올 길이 없는 상태였고, 그래서 다친 사람은 영영 명단에 앉아 있었다.
        /// 한 번 다치면 끝이면 그건 부상이 아니라 사망이다.</para>
        /// </summary>
        public DateTimeOffset RecoversAt;

        /// <summary>
        /// 치료를 시작한 시각. 진행률을 그리려면 끝만으로는 안 되고 시작이 있어야 한다 —
        /// <c>CraftJob.StartedAt</c>, <c>ExpeditionState.DepartedAt</c> 과 같은 자리다.
        /// </summary>
        public DateTimeOffset TreatedAt;
    }

    public enum ScavStatus { Idle, OnExpedition, Injured, Treating, Missing, Dead, Working }

    [Serializable]
    public sealed class ExpeditionState
    {
        public bool IsOrientation;
        public string Uid;
        public string MapId;
        public List<string> ScavUids = new List<string>();
        public DateTimeOffset DepartedAt;
        public DateTimeOffset ReturnsAt;
        public long CostPaid;

        /// <summary>
        /// 구조하러 간 실종자의 uid. 비어 있으면 평범한 탐색이다.
        ///
        /// <para>구조를 별도 시스템으로 두지 않고 파견에 얹은 이유: 나가고, 기다리고, 돌아오는
        /// 것이 똑같기 때문이다. 따로 만들면 사고 판정·장비 효과·알림을 두 벌로 유지해야 한다.</para>
        /// </summary>
        public string RescueScavUid;

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

    /// <summary>
    /// 고용 시장. 후보는 날짜 경계마다 갈린다 (<c>ScavMarket</c>).
    ///
    /// 시드에서 매번 다시 뽑지 않고 저장하는 이유: 고용하면 한 명이 빠지므로 어차피
    /// 상태가 생긴다. 화면을 다시 그릴 때마다 후보가 바뀌는 것보다 저장하는 쪽이 단순하다.
    /// </summary>
    [Serializable]
    public sealed class MarketState
    {
        /// <summary>현재 후보가 속한 게임 날짜. 오늘과 다르면 갱신한다.</summary>
        public string ActiveGameDate;

        /// <summary>
        /// 오늘 다시 굴린 횟수 (보상 광고). 날이 바뀌면 0 으로 돌아간다.
        ///
        /// <para>시드에 섞인다. 날짜만으로 시드를 만들면 같은 날은 몇 번을 돌려도 같은 후보가
        /// 나오도록 돼 있어서 — 그게 리세마라 방지 장치다 — 이 값 없이는 다시 굴릴 방법이 없다.</para>
        /// </summary>
        public int RerollCount;

        public List<ScavOffer> Offers = new List<ScavOffer>();
    }

    [Serializable]
    public sealed class ScavOffer
    {
        public string OfferId;
        public string Name;
        public int Tier = 1;
        public int Search, Combat, Survival;
        public List<string> TraitIds = new List<string>();
        public long HireCost;
        public long WagePerHour;

        /// <summary>이 사람이 들고 오는 무기. 고용하면 무기 칸에 바로 들어간다.</summary>
        public string StarterWeapon;

        /// <summary>고용 완료 표시. 같은 후보를 두 번 사지 못하게 한다.</summary>
        public bool Hired;

        public int StatTotal => Search + Combat + Survival;
    }

    /// <summary>
    /// PC 본편 발송함 (GDD §10).
    ///
    /// <para>MVP 에서는 <b>여기까지만</b> 한다 — 실제 전송은 P5 다. 그래도 검증은 지금
    /// 전부 건다. 나중에 붙이면 "이미 큐에 쌓인 것"을 어떻게 할지부터 곤란해진다.</para>
    /// </summary>
    /// <summary>
    /// 월간 지원계약과 보상 광고 (GDD §11).
    ///
    /// <para>여기 있는 값은 <b>편의</b>만 정한다. 본편 배송 한도는 이 상태를 절대 읽지 않는다 —
    /// 그 사실을 <c>SupportTests</c> 가 검사한다.</para>
    /// </summary>
    [Serializable]
    public sealed class SupportState
    {
        /// <summary>계약 만료 시각. 기본값(과거)이면 계약이 없다.</summary>
        public DateTimeOffset ActiveUntil;

        /// <summary>광고 횟수가 속한 게임 날짜. 오늘과 다르면 초기화된다.</summary>
        public string AdGameDate;

        public int AdsWatchedToday;
    }

    [Serializable]
    public sealed class MailState
    {
        public string AccountId;
        /// <summary>전송 대기 중인 화물. P5 에서 이 목록을 본편으로 보낸다.</summary>
        public List<MailShipment> Outbox = new List<MailShipment>();

        /// <summary>일일 한도가 속한 게임 날짜. 오늘과 다르면 한도가 초기화된다.</summary>
        public string DailyQuotaGameDate;

        public long DailyQuotaUsedValue;
        public int DailyShipmentsUsed;

        /// <summary>
        /// 본편과 연결됐는가. 연동은 선택이다 (GDD §12) — 모바일만 해도 전 구간이 돌아간다.
        ///
        /// <para>연결 전에는 발송 버튼 자체를 띄우지 않는다 (LINK_CONTRACT §5-1).
        /// 누를 수는 있는데 아무 데도 안 가는 버튼은, 물건이 사라졌다는 신고로 돌아온다.</para>
        /// </summary>
        public bool Linked;

        /// <summary>연결된 본편 프로필 표시용 이름. 연결 화면과 발송함에 보여준다.</summary>
        public string LinkedProfileLabel;
    }

    [Serializable]
    public sealed class MailShipment
    {
        public string AccountId;
        public bool Uploaded;
        /// <summary>
        /// 거래 id. 본편이 중복 수령을 막는 열쇠다 — 같은 tx 를 두 번 받으면 무시해야 한다.
        /// 그래서 큐에 넣는 순간 확정하고 이후 바뀌지 않는다.
        /// </summary>
        public string TxId;

        public DateTimeOffset QueuedAt;
        public List<ItemStack> Items = new List<ItemStack>();

        /// <summary>넣을 때 계산한 총 가치. 한도 검증과 표시에 쓴다.</summary>
        public long TotalValue;

        /// <summary>
        /// <c>sha256(txId + 정렬된 items + schemaVersion)</c> (LINK_CONTRACT §3).
        /// 본편이 P4 에서 이걸로 스키마 불일치를 걸러낸다.
        /// </summary>
        public string Checksum;

        /// <summary>
        /// 본편이 수령했는가. 수령 확인이 오기 전까지는 <c>pending</c> 이다.
        ///
        /// <para>수령됐다고 목록에서 바로 지우지 않는다 — 보낸 사람에게 남는 기록이
        /// 하나도 없으면 "보냈는데 안 왔다"를 확인할 방법이 없다.</para>
        /// </summary>
        public bool Claimed;
    }
}
