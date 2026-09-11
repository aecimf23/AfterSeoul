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
    }

    public sealed class RecipeDef
    {
        public string Id;
        public int StationLevel = 1;
        public ItemStack[] Inputs = System.Array.Empty<ItemStack>();
        public string OutputItemId;
        public int OutputCount = 1;
        public int WorkSeconds = 8;
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
    /// <c>balance.json</c>. 기본값은 파일과 같은 값이다 — 테스트 스텁이 파일 없이 쓸 수 있게.
    /// 코드가 실제로 읽는 값만 둔다. 안 쓰는 상수를 미리 넣어두면 "고쳤는데 왜 안 바뀌지"가 된다.
    /// </summary>
    public sealed class BalanceDef
    {
        public long StartingMoney;

        /// <summary>판매가 = basePrice × 이 값. 본편 가격을 그대로 쓴다 (DATA_SCHEMA §3-1).</summary>
        public double SellPriceRatio = 1.0;

        /// <summary>파견비 ÷ 기대 회수가치. 데이터 테스트가 이 비율로 파견비를 검증한다.</summary>
        public double ExpeditionCostRatio = 0.55;

        /// <summary>의뢰 보상금 배수. 인덱스 = tier - 1.</summary>
        public double[] QuestRewardMultiplierByTier = { 1.25, 1.35, 1.5 };

        /// <summary>직접 노동 1회 보수. 인덱스 = <see cref="CraftQuality"/>.</summary>
        public long[] LaborPayByQuality = { 0, 8000, 11000, 15000 };

        /// <summary>미니게임 점수(0~1) → 품질 경계 3개. 실패|보통|양호|우수.</summary>
        public double[] LaborGradeThresholds = { 0.3, 0.6, 0.85 };
    }
}
