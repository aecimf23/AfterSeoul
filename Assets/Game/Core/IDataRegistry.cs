using System.Collections.Generic;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 정적 데이터(items/maps/recipes/loot_tables/quests/balance) 조회.
    ///
    /// 인터페이스로 두는 이유는 추상화 취향이 아니라 테스트 때문이다.
    /// 테스트가 458종짜리 실제 JSON 을 읽을 필요는 없고, 아이템 세 개짜리
    /// 가짜 레지스트리면 충분하다. 그래야 테스트가 빠르고 실패 원인이 명확하다.
    ///
    /// 구현체는 둘뿐이다: JSON 로더(<c>JsonDataRegistry</c>)와 테스트용 스텁.
    /// 그 이상 늘리지 않는다.
    /// </summary>
    public interface IDataRegistry
    {
        ItemDef GetItem(string id);
        MapDef GetMap(string id);
        RecipeDef GetRecipe(string id);
        LootTableDef GetLootTable(string id);
        IReadOnlyList<QuestDef> GetQuestPool(string poolId);

        /// <summary>밸런스 상수. 코드에 박지 않고 <c>balance.json</c> 에서 온다 (DATA_SCHEMA §4).</summary>
        BalanceDef Balance { get; }

        /// <summary>고용 시장 템플릿. <c>scav_pool.json</c>.</summary>
        ScavPoolDef ScavPool { get; }

        /// <summary>장비 상점. <c>shop.json</c>.</summary>
        ShopDef Shop { get; }

        /// <summary>본편 전송 정책. <c>transferable_items.json</c>.</summary>
        TransferPolicyDef Transfer { get; }

        /// <summary>본편 상인. 상점 재고가 여기서 온다. 모르는 id 면 null.</summary>
        NpcDef GetNpc(string id);

        /// <summary>
        /// 모든 지역. 의뢰가 "지금 갈 수 있는 곳에서 구할 수 있는가"를 따지는 데 쓴다
        /// (<c>QuestReach</c>). id 조회만으로는 "어디서 나오는가"를 되물을 수 없다.
        /// </summary>
        IEnumerable<MapDef> AllMaps { get; }

        /// <summary>모든 제작법. 의뢰 물건을 만들어서 댈 수 있는지 보는 데 쓴다.</summary>
        IEnumerable<RecipeDef> AllRecipes { get; }

        /// <summary>
        /// 모든 아이템. "이 태그를 가진 아무거나"처럼 id 를 모르는 채로 찾아야 할 때 쓴다
        /// (태그 조건 의뢰의 아이콘 등).
        /// </summary>
        IEnumerable<ItemDef> AllItems { get; }

        /// <summary>탐색 중에 일어날 수 있는 일들 (매복·생존자·잠긴 창고).</summary>
        IEnumerable<ExpeditionEventDef> AllExpeditionEvents { get; }

        /// <summary>고를 수 있는 고용주들 (GDD §4).</summary>
        IEnumerable<EmployerDef> AllEmployers { get; }
    }

    /// <summary>
    /// 탐색 이벤트 한 종류 (<c>expedition_events.json</c>).
    ///
    /// <para>요구 능력치는 <b>팀 합계</b>와 견준다. 0 이면 그 능력은 보지 않는다.
    /// 성공/실패로 값이 갈리는 이유는, 같은 사건이 보낸 사람에 따라 다른 이야기가 돼야
    /// "누구를 보낼까"가 선택이 되기 때문이다.</para>
    /// </summary>
    public sealed class ExpeditionEventDef
    {
        public string Id;

        /// <summary>추첨 가중치.</summary>
        public int Weight = 10;

        /// <summary>이 티어 이상에서만 나온다. <see cref="MaxTier"/> 가 0 이면 상한 없음.</summary>
        public int MinTier = 1;
        public int MaxTier;

        public int RequiresSearch;
        public int RequiresCombat;
        public int RequiresSurvival;

        /// <summary>회수 횟수 증감.</summary>
        public int PassLootRolls;
        public int FailLootRolls;

        public long PassMoney;
        public long FailMoney;

        public int PassTrust;
        public int FailTrust;

        /// <summary>실패가 곧 사고인가 (막지 못한 매복 등).</summary>
        public bool FailCausesAccident;
    }

    /// <summary>
    /// 본편 상인 (<c>npcs.json</c>). 재고는 본편에서 추출한 실제 목록이다 —
    /// 모바일에서 품목을 지어내면 본편과 다른 물건을 파는 같은 이름의 상인이 생긴다.
    /// </summary>
    public sealed class NpcDef
    {
        public string Id;
        public string Currency;
        public string[] InventoryItemIds = System.Array.Empty<string>();
    }

    /// <summary>
    /// 장비 상점 (<c>shop.json</c>).
    ///
    /// <para><b>품목을 따로 적지 않는다.</b> 상인 id 와 해금 신뢰도만 두고, 파는 물건은
    /// <c>npcs.json</c> 의 본편 재고에서 가져온다. 목록을 두 곳에 두면 반드시 어긋난다.</para>
    /// </summary>
    public sealed class ShopDef
    {
        /// <summary>구매가 = <c>basePrice</c> × 이 값. 판매가(1.0)와의 차이가 상인의 몫이다.</summary>
        public double PriceMultiplier = 1.35;

        public ShopTraderDef[] Traders = System.Array.Empty<ShopTraderDef>();
    }

    public sealed class ShopTraderDef
    {
        public string NpcId;

        /// <summary>고용주 신뢰도가 이 값 이상이어야 이 상인의 물건을 댄다.</summary>
        public int RequiresTrust;
    }

    /// <summary>
    /// AFTER SEOUL → ESCAPE FROM SEOUL 전송 정책 (<c>transferable_items.json</c>, GDD §10).
    ///
    /// <para><b>수량 상한만으로는 못 막는다.</b> 본편 탄약은 단가가 900원부터 268,000원까지
    /// 300배 차이가 난다 — 60발 한 스택이 900,000원어치일 수 있다. 그래서 진짜 방어선은
    /// <see cref="MaxShipmentValue"/>·<see cref="MaxDailyValue"/> 같은 <b>가치 상한</b>이고,
    /// 이건 어떤 이유로도 건너뛰면 안 된다.</para>
    /// </summary>
    public sealed class TransferPolicyDef
    {
        /// <summary>이 단가를 넘는 물건은 아예 못 보낸다.</summary>
        public long UnitPriceCeiling = 15000;

        /// <summary>카테고리(아이템 id 접두사)별 규칙.</summary>
        public Dictionary<string, TransferCategoryDef> Categories =
            new Dictionary<string, TransferCategoryDef>();

        /// <summary>개별 차단 목록. 카테고리가 허용해도 여기 있으면 못 보낸다.</summary>
        public string[] DenyItemIds = System.Array.Empty<string>();

        public TransferLimitsDef Limits = new TransferLimitsDef();
    }

    public sealed class TransferCategoryDef
    {
        public bool Allowed;
        public int MaxPerShipment = int.MaxValue;
        public int MaxPerDay = int.MaxValue;
    }

    public sealed class TransferLimitsDef
    {
        public int MaxShipmentsPerDay = 2;
        public int MaxItemStacksPerShipment = 4;
        public long MaxShipmentValue = 25000;
        public long MaxDailyValue = 50000;

        /// <summary>
        /// 아직 본편이 수령하지 않은 화물의 최대 건수 (LINK_CONTRACT §V6 — 20건).
        ///
        /// <para><b>이 검사가 없었다.</b> 규약에는 "shipments 가 20건 미만인가 → 차단 + 안내"라고
        /// 적혀 있는데 코드에는 없어서, 발송함이 무한히 쌓였다. 하루 2건씩 한 달이면 60건이고,
        /// 홈 화면의 연동 카드는 그걸 전부 그린다.</para>
        ///
        /// <para>수령 표시(<c>MailShipment.Claimed</c>)를 쓰는 쪽이 P5(본편)라서 지금은 아무도
        /// 비워주지 않는다. 그래서 상한이 더더욱 필요하다 — 안 그러면 클라우드 문서가 규약 크기를
        /// 넘기고, 그건 연동을 붙이는 날 터진다.</para>
        /// </summary>
        public int MaxPendingShipments = 20;
    }

    public sealed class ItemDef
    {
        public string Id;
        public string Category;
        public string Slot;
        public long BasePrice;
        public int MaxStack = 1;
        public string[] Tags = System.Array.Empty<string>();
        public bool Transferable;

        // ── 장비 (본편에서 추출) ──────────────────────────────
        // 슬롯마다 의미 있는 값 하나씩만 쓴다. 해당 없는 슬롯에서는 0 이다.
        // 새로 지어낸 수치가 아니라 본편 ItemDatabase 의 값 그대로다 —
        // 본편에서 좋은 방탄복은 모바일에서도 좋아야 한다.

        /// <summary>장비로 지급할 수 있는가. 의정부 모드 전용 물건은 false 다.</summary>
        public bool Equippable;

        /// <summary>
        /// AFTER SEOUL 의 장비 칸 (<see cref="Scav.EquipSlot"/>). <see cref="Slot"/> 은 본편 슬롯 이름
        /// 그대로이고, 이쪽이 모바일의 6칸이다. 본편의 주무기·보조무기·근접무기가 여기서 "Weapon"
        /// 하나로 합쳐진다.
        /// </summary>
        public string EquipSlot;

        /// <summary>방탄복·헬멧 방어등급 1~6.</summary>
        public int ArmorClass;

        /// <summary>리그·가방 적재 칸 수 (본편 격자 가로×세로). 6~64.</summary>
        public int GridSlots;

        /// <summary>헤드셋 청취 반경 12/15/18. 본편 미착용 기준은 7.</summary>
        public int HearingRange;

        /// <summary>무기 등급 1~5. 본편에 등급 스탯이 없어 가격으로 매긴다 (추출 스크립트 주석 참조).</summary>
        public int WeaponGrade;
    }

    public sealed class MapDef
    {
        public string Id;
        public int Tier;
        public int DurationMinutes;
        public long BaseCostWage;
        public long BaseCostSupply;
        public int RiskLevel;
        public double CombatChance;
        public string LootTableId;

        /// <summary>해금 조건. 없으면 처음부터 열려 있다.</summary>
        public UnlockDef Unlock = UnlockDef.Default;
    }

    /// <summary>
    /// 지역 해금 조건 (<c>expeditions.json</c> 의 <c>unlockCondition</c>).
    ///
    /// <para>타입을 문자열로 두고 판정을 <c>MapUnlock</c> 한 곳에 모은다. enum 으로 두면
    /// 데이터에 새 조건을 넣을 때마다 Core 를 고쳐야 하고, 모르는 값이 오면 파싱이 죽는다.
    /// 모르는 타입은 "잠김"으로 취급한다 — 조용히 열리는 쪽이 더 나쁘다.</para>
    /// </summary>
    public sealed class UnlockDef
    {
        public static readonly UnlockDef Default = new UnlockDef();

        /// <summary>"default" | "playerLevel" | "npcTrust".</summary>
        public string Type = "default";

        public string NpcId;
        public int Value;
    }

    public sealed class RecipeDef
    {
        public string Id;
        public int StationLevel = 1;
        public ItemStack[] Inputs = System.Array.Empty<ItemStack>();
        public string OutputItemId;

        /// <summary>양호 품질 기준 산출 개수. 품질이 이 수를 늘리고 줄인다.</summary>
        public int OutputCount = 1;

        public int WorkSeconds = 8;

        /// <summary>
        /// 손으로 만들 때 필요한 공정 수. 미니게임 한 번이 한 단계다.
        ///
        /// <para>0 이면 작업대에서 못 만든다 — 큐로만 돌리는 레시피라는 뜻이다.</para>
        /// </summary>
        public int ManualSteps = 3;

        /// <summary>공정 단계 이름. 비어 있으면 기본 이름을 쓴다.</summary>
        public string[] StepNames = System.Array.Empty<string>();

        /// <summary>
        /// 단계별 미니게임 종류 ("timing" / "hold" / "inspect").
        ///
        /// <para>비어 있거나 모자라면 <c>Minigames.KindFor</c> 가 돌려가며 배정한다.
        /// 네 단계가 전부 같은 동작이면 그건 놀이가 아니라 절차다.</para>
        /// </summary>
        public string[] StepGames = System.Array.Empty<string>();
    }

    /// <summary>작업대 성장 계수 (<c>balance.station</c>).</summary>
    public sealed class StationTuning
    {
        public int MaxLevel = 5;
        public long UpgradeBaseCost = 300_000;
        public double UpgradeCostExponent = 2.0;

        /// <summary>레벨당 제작 시간 배수. 0.85 면 한 단계마다 15% 빨라진다.</summary>
        public double SpeedPerLevel = 0.85;

        /// <summary>
        /// 오프라인 생산 상한(시간). 무제한이면 "일주일 뒤에 한 번 접속"이 최적 전략이 되어
        /// 매일 접속할 이유가 사라지고, 그러면 일일 의뢰(GDD §5)가 무의미해진다.
        /// </summary>
        public int OfflineCapHours = 12;
    }

    /// <summary>보조 인력 계수 (<c>balance.assistant</c>).</summary>
    public sealed class AssistantTuning
    {
        public int UnlockStationLevel = 3;
        public int MaxCount = 3;
        public long HireBaseCost = 900_000;
        public double HireCostExponent = 1.8;

        /// <summary>
        /// 보조 인력이 만든 물건의 품질 분포 (실패/보통/양호/우수 가중치).
        ///
        /// <para><b>사람 손보다 한 수 아래로 둔다.</b> 자동이 직접 하는 것보다 잘하면
        /// 미니게임이 장식이 되고, 그러면 이 게임에서 유일하게 손으로 하는 일이 사라진다.</para>
        /// </summary>
        public int[] QualityWeights = { 15, 55, 25, 5 };
    }

    public sealed class LootTableDef
    {
        public string Id;
        public int RollsMin = 1;
        public int RollsMax = 3;
        public LootEntry[] Entries = System.Array.Empty<LootEntry>();
    }

    public sealed class LootEntry
    {
        public string ItemId;
        public int Weight = 1;
        public int CountMin = 1;
        public int CountMax = 1;
    }

    public sealed class QuestDef
    {
        public string Id;
        public int Tier = 1;
        public QuestRequirement[] Requires = System.Array.Empty<QuestRequirement>();
        public long RewardMoney;
        public int RewardTrust;
        public long RewardExp;
        public string ChainNextId;

        /// <summary>
        /// 이 의뢰가 경유하는 시스템. GDD §5 의 "최소 2개 시스템 경유" 규칙을
        /// 데이터로 검증하기 위한 필드다. 빌드 테스트가 길이 2 미만이면 실패시킨다.
        /// </summary>
        public string[] Touches = System.Array.Empty<string>();
    }

    public sealed class QuestRequirement
    {
        /// <summary>둘 중 하나만 채운다. Tag 는 "볼트 계열 아무거나" 같은 유연한 조건.</summary>
        public string ItemId;
        public string Tag;
        public int Count = 1;
    }

    /// <summary>
    /// <c>scav_pool.json</c> — 고용 후보를 만들 재료.
    /// 여기 있는 건 템플릿이고, 실제로 뽑힌 후보는 세이브의 <c>MarketState</c> 에 들어간다.
    /// </summary>
    public sealed class ScavPoolDef
    {
        public string[] Names = System.Array.Empty<string>();
        public ScavTraitDef[] Traits = System.Array.Empty<ScavTraitDef>();
        public ScavTierDef[] Tiers = System.Array.Empty<ScavTierDef>();
    }

    /// <summary>
    /// 스캐브 특성 (GDD §14).
    ///
    /// <para><b>한동안 아무 일도 하지 않았다.</b> 특성은 고용 시장에서 뽑혀 세이브에 저장되고
    /// 인원 화면에 "특성 겁쟁이"라고 표시까지 됐는데, <c>EffectType</c> 을 읽는 코드가
    /// 한 군데도 없었다. 원래 주석에 "효과는 P2 후반에서 읽는다"고 적혀 있었지만 그 P2 후반이
    /// 오지 않았고, 그 사이 화면은 계속 있지도 않은 능력을 광고하고 있었다.</para>
    ///
    /// <para><c>penalty</c> 는 JSON 에 있는데 파서가 아예 안 옮기고 있었다 — 그래서
    /// 겁쟁이는 <b>장점만 있고 단점이 없는</b> 특성이었다. manualSteps 와 같은 부류다.</para>
    /// </summary>
    public sealed class ScavTraitDef
    {
        public string Id;

        /// <summary>"mapBonus" | "fleeChance" | "injurySurvival". <see cref="Scav.Traits"/> 가 읽는다.</summary>
        public string EffectType;

        /// <summary><c>mapBonus</c> 일 때만 뜻이 있다.</summary>
        public string EffectMapId;

        public double EffectValue;

        /// <summary>
        /// 대가. 지금은 <c>lootMultiplier</c> 하나뿐이다.
        ///
        /// <para>특성에 장점만 있으면 그건 특성이 아니라 등급이다 — 뽑기 결과를 보고
        /// "좋은가 나쁜가"만 판정하게 되고, 누구를 어디에 보낼지의 판단이 사라진다.</para>
        /// </summary>
        public string PenaltyType;
        public double PenaltyValue;
    }

    public sealed class ScavTierDef
    {
        public int Tier = 1;
        public int StatTotalMin = 9;
        public int StatTotalMax = 14;
        public long HireCost;
        public long WagePerHour;
        public int TraitCount = 1;

        /// <summary>
        /// 고용하면 따라오는 무기. 스캐브는 자기 무기를 들고 온다.
        ///
        /// <para>없으면 첫 고용 직후 파견할 방법이 없다 — 무기가 필수인데 입문 지역
        /// 전리품 표에 장비가 없고 상점은 신뢰도가 필요하다.</para>
        /// </summary>
        public string StarterWeapon;
    }

    /// <summary>
    /// <c>balance.json</c>. 기본값은 파일과 같은 값이다 — 테스트 스텁이 파일 없이 쓸 수 있게.
    /// 코드가 실제로 읽는 값만 둔다. 안 쓰는 상수를 미리 넣어두면 "고쳤는데 왜 안 바뀌지"가 된다.
    /// </summary>
    public sealed class BalanceDef
    {
        public AfterSeoul.Factory.ProductionTuning Production = new AfterSeoul.Factory.ProductionTuning();
        public long StartingMoney;

        /// <summary>판매가 = basePrice × 이 값. 본편 가격을 그대로 쓴다 (DATA_SCHEMA §3-1).</summary>
        public double SellPriceRatio = 1.0;

        /// <summary>파견비 ÷ 기대 회수가치. 데이터 테스트가 이 비율로 파견비를 검증한다.</summary>
        public double ExpeditionCostRatio = 0.55;

        /// <summary>
        /// 인건비 계산의 기준 시급. 티어 1 스캐브의 시급과 같게 둔다.
        ///
        /// <para>이 값이 있어야 <c>expeditions.json</c> 의 <c>baseCostWage</c> 가
        /// "티어 1 한 명을 보낼 때의 인건비"라는 뜻을 갖는다. 그 위에 실제 팀의 시급 비율을
        /// 곱해서 인건비를 낸다 (<c>ExpeditionSystem.CostFor</c>).</para>
        /// </summary>
        public long BaseWagePerHour = 40000;

        /// <summary>의뢰 보상금 배수. 인덱스 = tier - 1.</summary>
        public double[] QuestRewardMultiplierByTier = { 1.25, 1.35, 1.5 };

        /// <summary>
        /// 직접 노동 1회 보수. 인덱스 = <see cref="CraftQuality"/>.
        ///
        /// <para><b>더 이상 쓰지 않는다.</b> 작업대가 돈이 아니라 물건을 만들게 바뀌었고
        /// (<see cref="Factory.Workbench"/>), 돈은 그 물건을 팔아서 번다. 값은 예전 세이브와
        /// 밸런스 비교용으로만 남겨 둔다.</para>
        /// </summary>
        public long[] LaborPayByQuality = { 0, 8000, 11000, 15000 };

        /// <summary>
        /// 작업대 산출 개수 배수. 인덱스 = <see cref="CraftQuality"/>, 기준은 레시피의 <c>output.count</c>.
        ///
        /// <para>품질을 보수 배수가 아니라 개수로 두는 이유: 배수는 숫자 하나가 달라질 뿐이지만
        /// 개수는 "잘해서 하나 더 나왔다"가 눈에 보인다.</para>
        /// </summary>
        public double[] ManualOutputByQuality = { 0.4, 0.7, 1.0, 1.4 };

        /// <summary>미니게임 점수(0~1) → 품질 경계 3개. 실패|보통|양호|우수.</summary>
        public double[] LaborGradeThresholds = { 0.3, 0.6, 0.85 };

        /// <summary>장비 효과 계수 (GDD §7). 기본값은 <c>balance.json</c> 과 같게 유지한다.</summary>
        public EquipmentTuning Equipment = new EquipmentTuning();

        /// <summary>레벨 곡선.</summary>
        public LevelCurveDef LevelCurve = new LevelCurveDef();

        /// <summary>회수 가치 이만큼당 경험치 1. 작을수록 파견이 빨리 성장시킨다.</summary>
        public long ExpPerLootValue = 5000;

        /// <summary>직접 노동 1회 경험치. 인덱스 = <see cref="CraftQuality"/>.</summary>
        public long[] ExpByLaborQuality = { 0, 2, 3, 5 };

        /// <summary>작업대 성장.</summary>
        public StationTuning Station = new StationTuning();

        /// <summary>보조 인력.</summary>
        public AssistantTuning Assistant = new AssistantTuning();

        /// <summary>부상 치료.</summary>
        public TreatmentTuning Treatment = new TreatmentTuning();
    }

    /// <summary>
    /// 부상 치료 (GDD §15).
    ///
    /// <para><b>이게 없던 동안 부상은 사실상 사망이었다.</b> <c>ScavStatus.Injured</c> 를 쓰는
    /// 코드는 파견·구조·화면에 다 있었는데 그 상태에서 <b>빠져나오는 길이 한 군데도 없었다</b> —
    /// <c>Treating</c> 은 값만 선언돼 있고 아무도 쓰지 않았다. 그래서 한 번 다친 사람은 영영
    /// 명단에 앉아 있고 명단은 시간이 갈수록 줄기만 했다. 값을 읽는 코드가 있다고 그 값이
    /// 움직이는 건 아니다 — 이 프로젝트에서 네 번째로 만난 같은 부류다.</para>
    /// </summary>
    public sealed class TreatmentTuning
    {
        /// <summary>치료비 = 이 값 × 티어. 부상이 사건이려면 공짜여선 안 된다.</summary>
        public long CostPerTier = 120_000;

        /// <summary>치료 시간(시간) = 이 값 × 티어. 파견 한 번을 못 나가는 정도의 무게.</summary>
        public double HoursPerTier = 6.0;

        /// <summary>
        /// 의료품을 쓰면 시간이 이 배로 줄어든다.
        ///
        /// <para>의료 아이템(<c>ItemGroup.Medical</c>)은 그동안 팔아치우는 것 말고는 쓸 데가
        /// 없었다. 여기에 쓰이면 "팔까 남길까"가 생기고, 그게 창고를 보는 이유가 된다.</para>
        /// </summary>
        public double SuppliesSpeedup = 0.5;
    }

    /// <summary>
    /// 레벨에 필요한 누적 경험치 = <c>baseExp × (레벨-1)^exponent</c>.
    ///
    /// <para>표가 아니라 식인 이유는 레벨 상한을 올릴 때 숫자를 스무 개 더 적지 않아도
    /// 되기 때문이다. 대신 경계값이 눈에 안 보이므로 테스트가 주요 지점을 박아 둔다.</para>
    /// </summary>
    public sealed class LevelCurveDef
    {
        public long BaseExp = 200;
        public double Exponent = 1.7;
        public int MaxLevel = 20;
    }

    /// <summary>
    /// 장비가 파견에 얼마나 영향을 주는지. 슬롯별로 딱 한 줄씩이다.
    ///
    /// <para>여기 없는 건 <c>Scav.Equipment</c> 의 계산식이 정한다. 계수를 늘리기 전에
    /// "이 숫자를 실제로 만질 것인가"를 먼저 묻는다 — 안 만질 상수는 코드에 두는 게 낫다.</para>
    /// </summary>
    public sealed class EquipmentTuning
    {
        /// <summary>무기 등급(1~5) 1당 운. 등급 5 = 0.30.</summary>
        public double WeaponLuckPerGrade = 0.06;

        /// <summary>헤드셋 단계(1~3) 1당 운. 최고 = 0.18.</summary>
        public double HeadsetLuckPerStep = 0.06;

        /// <summary>운의 상한. 1.0 이면 싼 물건을 절대 안 집어오게 되어 전리품 표가 무의미해진다.</summary>
        public double LuckCap = 0.55;

        /// <summary>헬멧 방어등급 1당 피해 강등 확률. 등급 6 = 0.24.</summary>
        public double HelmetMitigationPerClass = 0.04;

        /// <summary>강등 확률의 상한. 1.0 이면 아무도 안 죽어서 상실이 사건이 되지 못한다.</summary>
        public double MitigationCap = 0.80;

        /// <summary>이 칸 수 이상인 리그는 회수 +2 (미만은 +1).</summary>
        public int RigLargeSlots = 12;

        /// <summary>가방 회수 +2 / +3 경계 (칸 수).</summary>
        public int BackpackMediumSlots = 20;
        public int BackpackLargeSlots = 48;
    }
}
