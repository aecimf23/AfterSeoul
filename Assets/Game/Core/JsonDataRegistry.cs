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
            "recipes.json", "daily_quests.json", "scav_pool.json", "balance.json",
            "npcs.json", "shop.json", "transferable_items.json", "expedition_events.json",
            "employers.json",
        };

        private readonly Dictionary<string, ItemDef> _items = new Dictionary<string, ItemDef>();
        private readonly Dictionary<string, MapDef> _maps = new Dictionary<string, MapDef>();
        private readonly HashSet<string> _mainlineMapIds = new HashSet<string>();
        private readonly Dictionary<string, RecipeDef> _recipes = new Dictionary<string, RecipeDef>();
        private readonly List<ExpeditionEventDef> _events = new List<ExpeditionEventDef>();
        private readonly List<EmployerDef> _employers = new List<EmployerDef>();
        private readonly Dictionary<string, LootTableDef> _loot = new Dictionary<string, LootTableDef>();
        private readonly Dictionary<string, List<QuestDef>> _pools = new Dictionary<string, List<QuestDef>>();

        public BalanceDef Balance { get; private set; } = new BalanceDef();
        public ScavPoolDef ScavPool { get; private set; } = new ScavPoolDef();
        public ShopDef Shop { get; private set; } = new ShopDef();
        public TransferPolicyDef Transfer { get; private set; } = new TransferPolicyDef();

        private readonly Dictionary<string, NpcDef> _npcs = new Dictionary<string, NpcDef>();
        public NpcDef GetNpc(string id) { NpcDef v; return _npcs.TryGetValue(id, out v) ? v : null; }

        // 데이터 검증 테스트와 UI 목록용. 게임 로직은 id 조회만 쓴다.
        public IEnumerable<ItemDef> Items => _items.Values;
        public IEnumerable<MapDef> Maps => _maps.Values;
        public IEnumerable<RecipeDef> Recipes => _recipes.Values;

        // 인터페이스 쪽 이름. 위의 Maps/Recipes 는 데이터 검증 테스트가 쓰던 이름이라 남겨 둔다.
        IEnumerable<MapDef> IDataRegistry.AllMaps => _maps.Values;
        IEnumerable<RecipeDef> IDataRegistry.AllRecipes => _recipes.Values;
        IEnumerable<ItemDef> IDataRegistry.AllItems => _items.Values;
        IEnumerable<ExpeditionEventDef> IDataRegistry.AllExpeditionEvents => _events;
        IEnumerable<EmployerDef> IDataRegistry.AllEmployers => _employers;

        /// <summary>데이터 테스트가 직접 훑는다.</summary>
        public IReadOnlyList<EmployerDef> Employers => _employers;

        /// <summary>데이터 테스트가 직접 훑는다.</summary>
        public IReadOnlyList<ExpeditionEventDef> ExpeditionEvents => _events;
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
                // ItemDef fields deserialize directly; JSON shortName maps to ShortName.
                if (item.ShortName == null) item.ShortName = "";
                if (item.Tags == null) item.Tags = Array.Empty<string>();
                if (item.MaxStack < 1) item.MaxStack = 1;
                AddUnique(r._items, item.Id, item, "items.json");
            }

            var mapPositions = new Dictionary<string, MapIdDto>();
            foreach (var m in OrEmpty(Parse<MapsFile>(readFile, "maps.json").Maps)) {
                r._mainlineMapIds.Add(m.Id);
                mapPositions[m.Id] = m;
            }

            foreach (var n in OrEmpty(Parse<NpcsFile>(readFile, "npcs.json").Npcs))
                AddUnique(r._npcs, n.Id, new NpcDef
                {
                    Id = n.Id,
                    Currency = n.Currency,
                    // 본편 상인의 실제 재고. 상점이 파는 물건이 여기서 온다 —
                    // 모바일에서 품목을 따로 지어내면 본편과 다른 물건을 파는 상인이 된다.
                    InventoryItemIds = (n.MainlineInventoryItemIds ?? new List<string>()).ToArray(),
                }, "npcs.json");

            foreach (var e in OrEmpty(Parse<ExpeditionsFile>(readFile, "expeditions.json").Expeditions))
            {
                // 모바일이 지역 id 를 지어내지 않는다 (GDD §20-12).
                //
                // <b>이 규칙에는 강제가 없었다.</b> IsMainlineMap 은 그 검사를 하라고 만들어 뒀는데
                // 부르는 코드가 한 곳도 없었다 — 규칙이 주석으로만 있었던 셈이다.
                // 지어낸 지역은 본편에 존재하지 않으므로 나중에 연동을 붙일 때 대응할 곳이 없고,
                // 그때는 이미 그 지역으로 플레이한 세이브가 쌓여 있다.
                if (!r._mainlineMapIds.Contains(e.MapId))
                    throw new InvalidOperationException(
                        $"expeditions.json 의 {e.MapId} 가 본편 maps.json 에 없다 — " +
                        "모바일이 지역을 지어내면 안 된다 (GDD §20-12)");

                AddUnique(r._maps, e.MapId, new MapDef
                {
                    Id = e.MapId,
                    MapX = mapPositions[e.MapId].MapX, MapY = mapPositions[e.MapId].MapY,
                    Tier = e.Tier,
                    DurationMinutes = e.DurationMinutes,
                    BaseCostWage = e.BaseCostWage,
                    BaseCostSupply = e.BaseCostSupply,
                    RiskLevel = e.RiskLevel,
                    CombatChance = e.CombatChance,
                    LootTableId = e.LootTable,
                    Unlock = ToUnlock(e.UnlockCondition),
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
                // 단계 수·이름·미니게임은 오래 읽히지 않고 있었다 (JSON 에는 있는데 여기서 안 옮겼다).
                // 그래서 4단계 레시피가 3단계로 돌고 단계 이름이 전부 기본값이었다.
                var stepGames = OrEmpty(rc.StepGames).ToArray();
                foreach (var game in stepGames)
                {
                    MinigameKind ignored;
                    if (!Minigames.TryParse(game, out ignored))
                        throw new InvalidOperationException(
                            $"recipes.json: {rc.Id} 의 stepGames 에 모르는 값 '{game}' 이 있다 " +
                            "(timing / hold / inspect)");
                }

                AddUnique(r._recipes, rc.Id, new RecipeDef
                {
                    Id = rc.Id,
                    StationLevel = rc.StationLevel,
                    Inputs = OrEmpty(rc.Inputs).ToArray(),
                    OutputItemId = rc.Output.ItemId,
                    OutputCount = rc.Output.Count,
                    WorkSeconds = rc.WorkSeconds,
                    ManualSteps = rc.ManualSteps,
                    StepNames = OrEmpty(rc.StepNames).ToArray(),
                    StepGames = stepGames,
                }, "recipes.json");
            }

            var seenEvents = new HashSet<string>();
            foreach (var ev in OrEmpty(Parse<EventsFile>(readFile, "expedition_events.json").Events))
            {
                if (string.IsNullOrEmpty(ev.Id))
                    throw new InvalidOperationException("expedition_events.json 에 id 가 빈 항목이 있다");
                if (!seenEvents.Add(ev.Id))
                    throw new InvalidOperationException($"expedition_events.json 에 id 중복: {ev.Id}");
                // 가중치가 0 이면 영원히 안 뽑힌다 — 지워야 할 것을 남겨 둔 상태다.
                if (ev.Weight <= 0)
                    throw new InvalidOperationException($"expedition_events.json: {ev.Id} 의 weight 가 0 이하다");

                r._events.Add(ev);
            }

            var seenEmployers = new HashSet<string>();
            foreach (var emp in OrEmpty(Parse<EmployersFile>(readFile, "employers.json").Employers))
            {
                if (string.IsNullOrEmpty(emp.NpcId))
                    throw new InvalidOperationException("employers.json 에 npcId 가 빈 항목이 있다");
                if (!seenEmployers.Add(emp.NpcId))
                    throw new InvalidOperationException($"employers.json 에 npcId 중복: {emp.NpcId}");

                r._employers.Add(emp);
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

            r.ScavPool = LoadScavPool(readFile);

            r.Balance = Parse<BalanceDef>(readFile, "balance.json");
            ValidateBalance(r.Balance);

            r.Shop = Parse<ShopDef>(readFile, "shop.json");
            if (r.Shop.Traders == null) r.Shop.Traders = System.Array.Empty<ShopTraderDef>();
            if (r.Shop.PriceMultiplier <= 0)
                throw new InvalidOperationException("shop.json priceMultiplier 는 0 보다 커야 한다");
            r.Transfer = Parse<TransferPolicyDef>(readFile, "transferable_items.json");
            ValidateTransfer(r.Transfer);

            foreach (var t in r.Shop.Traders)
                if (r.GetNpc(t.NpcId) == null)
                    throw new InvalidOperationException(
                        $"shop.json 이 npcs.json 에 없는 상인을 가리킨다: {t.NpcId}");
            return r;
        }

        public ItemDef GetItem(string id) => id != null && _items.TryGetValue(id, out var v) ? v : null;
        public MapDef GetMap(string id) => id != null && _maps.TryGetValue(id, out var v) ? v : null;
        public RecipeDef GetRecipe(string id) => id != null && _recipes.TryGetValue(id, out var v) ? v : null;
        public LootTableDef GetLootTable(string id) => id != null && _loot.TryGetValue(id, out var v) ? v : null;
        public IReadOnlyList<QuestDef> GetQuestPool(string id) => id != null && _pools.TryGetValue(id, out var v) ? v : null;

        private static ScavPoolDef LoadScavPool(Func<string, string> readFile)
        {
            var file = Parse<ScavPoolFile>(readFile, "scav_pool.json");
            var pool = new ScavPoolDef();

            if (file.Names != null) pool.Names = file.Names.ToArray();

            if (file.Traits != null)
            {
                var traits = new List<ScavTraitDef>();
                foreach (var t in file.Traits)
                {
                    if (string.IsNullOrEmpty(t.Id)) continue;
                    traits.Add(new ScavTraitDef
                    {
                        Id = t.Id,
                        EffectType = t.Effect != null ? t.Effect.Type : null,
                        EffectMapId = t.Effect != null ? t.Effect.MapId : null,
                        EffectValue = t.Effect != null ? t.Effect.Value : 0.0,

                        // penalty 를 안 옮기고 있었다 — 그래서 겁쟁이는 장점만 있는 특성이었다.
                        PenaltyType = t.Penalty != null ? t.Penalty.Type : null,
                        PenaltyValue = t.Penalty != null ? t.Penalty.Value : 0.0,
                    });
                }
                pool.Traits = traits.ToArray();
            }

            if (file.Tiers != null)
            {
                var tiers = new List<ScavTierDef>();
                foreach (var td in file.Tiers)
                {
                    int min = td.StatTotal != null ? td.StatTotal.Min : 9;
                    int max = td.StatTotal != null ? td.StatTotal.Max : 14;
                    if (max < min) max = min;
                    // 능력치 셋에 최소 1씩은 줘야 한다 (ScavMarket.SplitStats 의 전제).
                    if (min < 3) min = 3;
                    tiers.Add(new ScavTierDef
                    {
                        Tier = td.Tier,
                        StatTotalMin = min,
                        StatTotalMax = max,
                        HireCost = td.HireCost,
                        WagePerHour = td.WagePerHour,
                        TraitCount = td.TraitCount,
                        StarterWeapon = td.StarterWeapon,
                    });
                }
                tiers.Sort((a, b) => a.Tier.CompareTo(b.Tier));
                pool.Tiers = tiers.ToArray();
            }

            return pool;
        }

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

        /// <summary>
        /// 전송 정책의 가치 상한이 살아 있는지 본다.
        ///
        /// <para>수량 상한만으로는 못 막는다 — 본편 탄약 단가가 900원~268,000원으로
        /// 300배 차이라 60발 한 스택이 90만원어치일 수 있다. 가치 상한이 0 이거나
        /// 빠지면 그게 곧 본편 경제를 여는 구멍이므로 부팅에서 막는다.</para>
        /// </summary>
        private static void ValidateTransfer(TransferPolicyDef t)
        {
            if (t == null) throw new InvalidOperationException("transferable_items.json 을 읽지 못했다");
            if (t.Categories == null) t.Categories = new Dictionary<string, TransferCategoryDef>();
            if (t.DenyItemIds == null) t.DenyItemIds = Array.Empty<string>();
            if (t.Limits == null) throw new InvalidOperationException("transferable_items.json limits 가 없다");

            if (t.UnitPriceCeiling <= 0)
                throw new InvalidOperationException("transferable_items.json unitPriceCeiling 이 0 이하다");
            if (t.Limits.MaxShipmentValue <= 0 || t.Limits.MaxDailyValue <= 0)
                throw new InvalidOperationException(
                    "transferable_items.json 의 가치 상한(maxShipmentValue/maxDailyValue)이 0 이하다 — 본편 경제가 열린다");
            if (t.Limits.MaxDailyValue < t.Limits.MaxShipmentValue)
                throw new InvalidOperationException("일일 가치 상한이 1회 상한보다 작다");
            if (t.Limits.MaxShipmentsPerDay <= 0 || t.Limits.MaxItemStacksPerShipment <= 0)
                throw new InvalidOperationException("transferable_items.json 의 횟수·스택 상한이 0 이하다");

            // 0 이면 발송이 아예 막히고, 너무 크면 클라우드 문서가 규약 크기를 넘는다
            // (LINK_CONTRACT §V6 — 20건).
            if (t.Limits.MaxPendingShipments <= 0)
                throw new InvalidOperationException(
                    "transferable_items.json maxPendingShipments 가 0 이하다 — 아무것도 못 보낸다");
            if (t.Limits.MaxPendingShipments < t.Limits.MaxShipmentsPerDay)
                throw new InvalidOperationException(
                    "maxPendingShipments 가 하루 발송 횟수보다 작다 — 첫날부터 막힌다");
        }

        private static List<T> OrEmpty<T>(List<T> list) => list ?? new List<T>();

        /// <summary>
        /// <c>unlockCondition</c> 을 옮긴다. 필드가 없으면 "처음부터 열림"이다 —
        /// 조건을 적지 않은 지역을 잠가버리면 데이터를 고칠 때마다 지역이 사라진다.
        /// 반대로 <b>모르는 타입</b>은 <c>MapUnlock</c> 이 잠긴 것으로 본다.
        /// </summary>
        private static UnlockDef ToUnlock(UnlockDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.Type)) return UnlockDef.Default;
            return new UnlockDef { Type = dto.Type, NpcId = dto.NpcId, Value = dto.Value };
        }

        private static void ValidateBalance(BalanceDef b)
        {
            // 인덱스로 읽는 배열이라 길이가 틀리면 플레이 중에 IndexOutOfRange 로 죽는다. 부팅에서 막는다.
            if (b.LaborPayByQuality == null || b.LaborPayByQuality.Length != 4)
                throw new InvalidOperationException("balance.json laborPayByQuality 는 4개(실패/보통/양호/우수)여야 한다");
            if (b.LaborGradeThresholds == null || b.LaborGradeThresholds.Length != 3)
                throw new InvalidOperationException("balance.json laborGradeThresholds 는 3개여야 한다");
            if (b.QuestRewardMultiplierByTier == null || b.QuestRewardMultiplierByTier.Length < 3)
                throw new InvalidOperationException("balance.json questRewardMultiplierByTier 는 tier 1~3 을 덮어야 한다");
            // 인건비의 분모다. 0 이면 파견비 계산에서 0 나누기가 난다.
            if (b.BaseWagePerHour <= 0)
                throw new InvalidOperationException("balance.json baseWagePerHour 는 0 보다 커야 한다");

            if (b.Equipment == null) b.Equipment = new EquipmentTuning();
            // 운이 1.0 이면 싼 물건을 절대 안 집어와서 전리품 표가 무의미해진다.
            if (b.Equipment.LuckCap <= 0 || b.Equipment.LuckCap >= 1.0)
                throw new InvalidOperationException("balance.json equipment.luckCap 은 0 과 1 사이여야 한다");
            // 1.0 이면 아무도 안 죽는다. GDD §15 의 상실이 사건이 되지 못한다.
            if (b.Equipment.MitigationCap <= 0 || b.Equipment.MitigationCap >= 1.0)
                throw new InvalidOperationException("balance.json equipment.mitigationCap 은 0 과 1 사이여야 한다");

            if (b.Station == null) b.Station = new StationTuning();
            // 1.0 이상이면 올릴수록 느려지고, 0 이하면 즉시 완성된다. 둘 다 조용히 이상해진다.
            if (b.Station.SpeedPerLevel <= 0 || b.Station.SpeedPerLevel >= 1.0)
                throw new InvalidOperationException("balance.json station.speedPerLevel 은 0 과 1 사이여야 한다");
            // 큐 슬롯 수가 곧 레벨이다. 0 이면 아무것도 못 넣는다.
            if (b.Station.MaxLevel < 1)
                throw new InvalidOperationException("balance.json station.maxLevel 은 1 이상이어야 한다");
            // 0 이면 오프라인 생산이 아예 없고, 너무 크면 매일 접속할 이유가 사라진다.
            if (b.Station.OfflineCapHours <= 0)
                throw new InvalidOperationException("balance.json station.offlineCapHours 는 0 보다 커야 한다");

            if (b.Assistant == null) b.Assistant = new AssistantTuning();
            if (b.Assistant.QualityWeights == null || b.Assistant.QualityWeights.Length != 4)
                throw new InvalidOperationException(
                    "balance.json assistant.qualityWeights 는 4개(실패/보통/양호/우수)여야 한다");

            int weightSum = 0;
            foreach (int w in b.Assistant.QualityWeights)
            {
                if (w < 0) throw new InvalidOperationException("balance.json assistant.qualityWeights 에 음수가 있다");
                weightSum += w;
            }
            if (weightSum <= 0)
                throw new InvalidOperationException("balance.json assistant.qualityWeights 합이 0 이다 — 뽑을 수가 없다");

            if (b.Treatment == null) b.Treatment = new TreatmentTuning();
            // 0 이면 사고에 아무 대가가 없어서 장비를 챙길 이유가 사라진다.
            if (b.Treatment.CostPerTier <= 0)
                throw new InvalidOperationException("balance.json treatment.costPerTier 는 0 보다 커야 한다");
            // 0 이면 치료가 버튼 한 번이 되고, 그러면 부상이 사건이 아니라 절차가 된다.
            if (b.Treatment.HoursPerTier <= 0)
                throw new InvalidOperationException("balance.json treatment.hoursPerTier 는 0 보다 커야 한다");
            // 1 이상이면 의료품을 쓸수록 느려지고, 0 이하면 즉시 회복이다. 둘 다 조용히 이상해진다.
            if (b.Treatment.SuppliesSpeedup <= 0 || b.Treatment.SuppliesSpeedup >= 1.0)
                throw new InvalidOperationException("balance.json treatment.suppliesSpeedup 은 0 과 1 사이여야 한다");
        }

        // ── JSON 모양 그대로의 DTO ─────────────────────────────
        // camelCase ↔ PascalCase 는 Newtonsoft 가 대소문자 무시로 맞춰준다.

        internal sealed class ItemsFile { public List<ItemDef> Items; }
        internal sealed class MapsFile { public List<MapIdDto> Maps; }
        internal sealed class MapIdDto { public string Id; public int MapX, MapY; }

        internal sealed class NpcsFile { public List<NpcDto> Npcs; }
        internal sealed class NpcDto
        {
            public string Id;
            public string Currency;
            public List<string> MainlineInventoryItemIds;
        }

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
            public UnlockDto UnlockCondition;
        }

        internal sealed class UnlockDto
        {
            public string Type;
            public string NpcId;
            public int Value;
        }

        internal sealed class LootFile { public List<LootTableDto> Tables; }
        internal sealed class LootTableDto { public string Id; public RangeDto Rolls; public List<LootEntryDto> Entries; }
        internal sealed class LootEntryDto { public string ItemId; public int Weight = 1; public RangeDto Count; }
        internal sealed class RangeDto { public int Min = 1; public int Max = 1; }

        internal sealed class ScavPoolFile
        {
            public List<string> Names;
            public List<ScavTraitDto> Traits;
            public List<ScavTierDto> Tiers;
        }
        internal sealed class ScavTraitDto
        {
            public string Id;
            public ScavEffectDto Effect;
            public ScavEffectDto Penalty;
        }
        internal sealed class ScavEffectDto { public string Type; public string MapId; public double Value; }
        internal sealed class ScavTierDto
        {
            public int Tier = 1;
            public RangeDto StatTotal;
            public long HireCost;
            public long WagePerHour;
            public int TraitCount = 1;
            public string StarterWeapon;
        }

        internal sealed class RecipesFile { public List<RecipeDto> Recipes; }
        internal sealed class RecipeDto
        {
            public string Id;
            public int StationLevel = 1;
            public List<ItemStack> Inputs;
            public ItemStack Output;
            public int WorkSeconds = 8;
            public int ManualSteps = 3;
            public List<string> StepNames;
            public List<string> StepGames;
        }

        internal sealed class EventsFile { public List<ExpeditionEventDef> Events; }

        internal sealed class EmployersFile { public List<EmployerDef> Employers; }

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
