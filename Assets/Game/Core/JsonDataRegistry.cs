using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AfterSeoul.Core
{
    /// <summary>
    /// <see cref="IDataRegistry"/> 의 JSON 구현. <c>StreamingAssets/Data/*.json</c> 을 읽는다.
    ///
    /// <para>파일을 직접 열지 않고 "파일명 → 텍스트" 함수를 받는다. Android 의 StreamingAssets 는
    /// apk 안에 있어서 <c>File</c> 로 못 읽고 <c>UnityWebRequest</c> 가 필요한데, 그건 Unity 레이어의
    /// 일이다 (R1). 테스트는 같은 함수로 디스크를 읽는다.</para>
    ///
    /// <para>JSON 모양과 C# 정의가 어긋나는 곳(<c>rolls: {min,max}</c> 등)만 DTO 를 거친다.
    /// 로딩 때 한 번 변환하고 이후엔 사전 조회뿐이다.</para>
    ///
    /// <para><b>데이터가 깨졌으면 부팅에서 바로 죽는다.</b> 빌드에 포함되는 파일이라 없거나 깨졌다면
    /// 버그이고, 조용히 넘기면 "파견 보냈는데 아무것도 안 가져옴" 같은 증상으로 늦게 나타난다.</para>
    /// </summary>
    public sealed class JsonDataRegistry : IDataRegistry
    {
        /// <summary>읽는 파일 전부. Unity 레이어가 이 목록대로 미리 읽어서 넘긴다.</summary>
        public static readonly string[] FileNames =
        {
            "items.json", "maps.json", "expeditions.json", "loot_tables.json",
            "recipes.json", "daily_quests.json", "balance.json",
        };

        private readonly Dictionary<string, ItemDef> _items = new Dictionary<string, ItemDef>();
        private readonly Dictionary<string, MapDef> _maps = new Dictionary<string, MapDef>();
        private readonly HashSet<string> _mainlineMapIds = new HashSet<string>();
        private readonly Dictionary<string, RecipeDef> _recipes = new Dictionary<string, RecipeDef>();
        private readonly Dictionary<string, LootTableDef> _loot = new Dictionary<string, LootTableDef>();
        private readonly Dictionary<string, List<QuestDef>> _pools = new Dictionary<string, List<QuestDef>>();

        public BalanceDef Balance { get; private set; } = new BalanceDef();

        // 데이터 검증 테스트와 UI 목록용. 게임 로직은 id 조회만 쓴다.
        public IEnumerable<ItemDef> Items => _items.Values;
        public IEnumerable<MapDef> Maps => _maps.Values;
        public IEnumerable<RecipeDef> Recipes => _recipes.Values;
        public IEnumerable<LootTableDef> LootTables => _loot.Values;
        public IEnumerable<string> QuestPoolIds => _pools.Keys;

        /// <summary>본편 <c>maps.json</c> 에 있는 지역인가. 모바일이 지역 id 를 지어내지 않는다 (GDD §20-12).</summary>
        public bool IsMainlineMap(string id) => _mainlineMapIds.Contains(id);

        private JsonDataRegistry() { }

        public static JsonDataRegistry Load(Func<string, string> readFile)
        {
            if (readFile == null) throw new ArgumentNullException(nameof(readFile));
            var r = new JsonDataRegistry();

            foreach (var item in OrEmpty(Parse<ItemsFile>(readFile, "items.json").Items))
            {
                if (item.Tags == null) item.Tags = Array.Empty<string>();
                if (item.MaxStack < 1) item.MaxStack = 1;
                AddUnique(r._items, item.Id, item, "items.json");
            }

            foreach (var m in OrEmpty(Parse<MapsFile>(readFile, "maps.json").Maps))
                r._mainlineMapIds.Add(m.Id);

            foreach (var e in OrEmpty(Parse<ExpeditionsFile>(readFile, "expeditions.json").Expeditions))
            {
                AddUnique(r._maps, e.MapId, new MapDef
                {
                    Id = e.MapId,
                    Tier = e.Tier,
                    DurationMinutes = e.DurationMinutes,
                    BaseCostWage = e.BaseCostWage,
                    BaseCostSupply = e.BaseCostSupply,
                    RiskLevel = e.RiskLevel,
                    CombatChance = e.CombatChance,
                    LootTableId = e.LootTable,
                }, "expeditions.json");
            }

            foreach (var t in OrEmpty(Parse<LootFile>(readFile, "loot_tables.json").Tables))
            {
                var entries = new List<LootEntry>();
                foreach (var e in OrEmpty(t.Entries))
                {
                    var count = e.Count ?? new RangeDto();
                    entries.Add(new LootEntry
                    {
                        ItemId = e.ItemId, Weight = e.Weight,
                        CountMin = count.Min, CountMax = count.Max,
                    });
                }
                var rolls = t.Rolls ?? new RangeDto();
                AddUnique(r._loot, t.Id, new LootTableDef
                {
                    Id = t.Id, RollsMin = rolls.Min, RollsMax = rolls.Max, Entries = entries.ToArray(),
                }, "loot_tables.json");
            }

            foreach (var rc in OrEmpty(Parse<RecipesFile>(readFile, "recipes.json").Recipes))
            {
                AddUnique(r._recipes, rc.Id, new RecipeDef
                {
                    Id = rc.Id,
                    StationLevel = rc.StationLevel,
                    Inputs = OrEmpty(rc.Inputs).ToArray(),
                    OutputItemId = rc.Output.ItemId,
                    OutputCount = rc.Output.Count,
                    WorkSeconds = rc.WorkSeconds,
                }, "recipes.json");
            }

            foreach (var pool in OrEmpty(Parse<QuestsFile>(readFile, "daily_quests.json").Pools))
            {
                var quests = new List<QuestDef>();
                foreach (var q in OrEmpty(pool.Quests))
                {
                    var reward = q.Reward ?? new RewardDto();
                    quests.Add(new QuestDef
                    {
                        Id = q.Id,
                        Tier = q.Tier,
                        Requires = q.Requires ?? Array.Empty<QuestRequirement>(),
                        RewardMoney = reward.Money,
                        RewardTrust = reward.Trust,
                        RewardExp = reward.Exp,
                        ChainNextId = q.ChainNextId,
                        Touches = q.Touches ?? Array.Empty<string>(),
                    });
                }
                AddUnique(r._pools, pool.Id, quests, "daily_quests.json");
            }

            r.Balance = Parse<BalanceDef>(readFile, "balance.json");
            ValidateBalance(r.Balance);
            return r;
        }

        public ItemDef GetItem(string id) => id != null && _items.TryGetValue(id, out var v) ? v : null;
        public MapDef GetMap(string id) => id != null && _maps.TryGetValue(id, out var v) ? v : null;
        public RecipeDef GetRecipe(string id) => id != null && _recipes.TryGetValue(id, out var v) ? v : null;
        public LootTableDef GetLootTable(string id) => id != null && _loot.TryGetValue(id, out var v) ? v : null;
        public IReadOnlyList<QuestDef> GetQuestPool(string id) => id != null && _pools.TryGetValue(id, out var v) ? v : null;

        // ── 로딩 헬퍼 ──────────────────────────────────────────

        private static T Parse<T>(Func<string, string> readFile, string fileName) where T : class
        {
            string text = readFile(fileName);
            if (string.IsNullOrEmpty(text))
                throw new InvalidOperationException($"데이터 파일이 없거나 비어 있다: {fileName}");
            try
            {
                return JsonConvert.DeserializeObject<T>(text)
                       ?? throw new InvalidOperationException($"{fileName} 이 null 로 파싱됐다");
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException($"{fileName} 파싱 실패: {e.Message}", e);
            }
        }

        private static void AddUnique<T>(Dictionary<string, T> dict, string id, T value, string fileName)
        {
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException($"{fileName} 에 id 가 빈 항목이 있다");
            if (dict.ContainsKey(id))
                throw new InvalidOperationException($"{fileName} 에 id 중복: {id}");
            dict.Add(id, value);
        }

        private static List<T> OrEmpty<T>(List<T> list) => list ?? new List<T>();

        private static void ValidateBalance(BalanceDef b)
        {
            // 인덱스로 읽는 배열이라 길이가 틀리면 플레이 중에 IndexOutOfRange 로 죽는다. 부팅에서 막는다.
            if (b.LaborPayByQuality == null || b.LaborPayByQuality.Length != 4)
                throw new InvalidOperationException("balance.json laborPayByQuality 는 4개(실패/보통/양호/우수)여야 한다");
            if (b.LaborGradeThresholds == null || b.LaborGradeThresholds.Length != 3)
                throw new InvalidOperationException("balance.json laborGradeThresholds 는 3개여야 한다");
            if (b.QuestRewardMultiplierByTier == null || b.QuestRewardMultiplierByTier.Length < 3)
                throw new InvalidOperationException("balance.json questRewardMultiplierByTier 는 tier 1~3 을 덮어야 한다");
        }

        // ── JSON 모양 그대로의 DTO ─────────────────────────────
        // camelCase ↔ PascalCase 는 Newtonsoft 가 대소문자 무시로 맞춰준다.

        internal sealed class ItemsFile { public List<ItemDef> Items; }
        internal sealed class MapsFile { public List<MapIdDto> Maps; }
        internal sealed class MapIdDto { public string Id; }

        internal sealed class ExpeditionsFile { public List<ExpeditionDto> Expeditions; }
        internal sealed class ExpeditionDto
        {
            public string MapId;
            public int Tier;
            public int DurationMinutes;
            public long BaseCostWage;
            public long BaseCostSupply;
            public int RiskLevel;
            public double CombatChance;
            public string LootTable;
        }

        internal sealed class LootFile { public List<LootTableDto> Tables; }
        internal sealed class LootTableDto { public string Id; public RangeDto Rolls; public List<LootEntryDto> Entries; }
        internal sealed class LootEntryDto { public string ItemId; public int Weight = 1; public RangeDto Count; }
        internal sealed class RangeDto { public int Min = 1; public int Max = 1; }

        internal sealed class RecipesFile { public List<RecipeDto> Recipes; }
        internal sealed class RecipeDto
        {
            public string Id;
            public int StationLevel = 1;
            public List<ItemStack> Inputs;
            public ItemStack Output;
            public int WorkSeconds = 8;
        }

        internal sealed class QuestsFile { public List<QuestPoolDto> Pools; }
        internal sealed class QuestPoolDto { public string Id; public List<QuestDto> Quests; }
        internal sealed class QuestDto
        {
            public string Id;
            public int Tier = 1;
            public QuestRequirement[] Requires;
            public RewardDto Reward;
            public string ChainNextId;
            public string[] Touches;
        }
        internal sealed class RewardDto { public long Money; public int Trust; public long Exp; }
    }
}
