using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Inventory;

namespace AfterSeoul.Exploration
{
    public static partial class ExplorationSystem
    {
        // Mainline Program.Progression.IsRaidMapAvailable reconnaissance chain.
        public static readonly IReadOnlyList<string> MainRoute = Array.AsReadOnly(new[] {
            "YONGSAN_MARKET", "GURO_FACTORY", "HAN_RIVER", "NAMSAN_WOODS", "GANGNAM_STREETS", "YONGSAN_BASE"
        });

        public static IEnumerable<MapDef> OrderedMaps(IDataRegistry data)
        {
            foreach (var id in MainRoute)
                if (data.GetMap(id) != null) yield return data.GetMap(id);
            foreach (var id in new[] { "MYEONGDONG", "UIJEONGBU" })
                if (data.GetMap(id) != null) yield return data.GetMap(id);
        }

        public static string RouteLockReason(GameSave save, string mapId)
        {
            bool known = mapId == "MYEONGDONG" || mapId == "UIJEONGBU";
            foreach (var id in MainRoute) if (id == mapId) known = true;
            if (!known) return Loc.Text("지역 정보를 찾을 수 없습니다");
            foreach (var id in MainRoute)
            {
                if (id == mapId) return null;
                // An existing successful result is retained when loading older saves.
                bool survived = save.SurvivedExplorationMapIds != null && save.SurvivedExplorationMapIds.Contains(id);
                var result = save.Exploration?.Result;
                survived |= result != null && result.Outcome == ExplorationOutcome.Success && result.MapId == id;
                if (!survived) return Loc.Text("{0}에서 생존 귀환하면 열립니다", Loc.MapName(id));
            }
            return null;
        }

        public static bool IsActive(GameSave s)
        {
            return s.Exploration != null && s.Exploration.Phase != ExplorationPhase.Result;
        }

        public static string StartBlockReason(GameSave s, IDataRegistry d, string mapId)
        {
            if (IsActive(s) || (s.Exploration?.Result != null && !s.Exploration.Result.Acknowledged))
                return "진행 중인 탐색 결과를 먼저 확인하세요";
            var map = d.GetMap(mapId);
            if (map == null)
                return "지역 정보를 찾을 수 없습니다";
            var locked = RouteLockReason(s, mapId);
            if (locked != null)
                return locked;
            bool recovery=s.RaidBase.RecoveryReady && RaidProgression.CanRequestRecovery(s);
            if(recovery && mapId!="YONGSAN_MARKET") return "대여 보급은 용산 전자상가에서 사용할 수 있습니다";
            foreach (var ex in s.Expeditions)
                if (!ex.Resolved && ex.MapId == mapId)
                    return "이 지역에 스캐브가 파견 중입니다";
            if (s.Player.Hp <= 0 || s.Player.Energy <= 0)
                return "기지에서 치료와 식사를 먼저 하세요";
            if (!recovery && CombatProfiles.For(PlayerEquipment.Equipped(s, "Weapon")) == null && PlayerEquipment.Equipped(s, "Melee") == null)
                return "총기 또는 근접 무기를 장착하세요";
            return null;
        }

        public static bool IsSupply(IDataRegistry data, string id)
        {
            return CombatProfiles.ConsumableFor(id) != null || data.GetItem(id)?.Category == "Ammo";
        }

        public static bool Start(GameSave s, IDataRegistry d, string mapId, IReadOnlyList<ItemStack> supplies = null)
        {
            if (StartBlockReason(s, d, mapId) != null)
                return false;
            var selected = new List<ItemStack>();
            bool recovery=s.RaidBase.RecoveryReady && RaidProgression.CanRequestRecovery(s);
            if(recovery) supplies=null;
            if (supplies != null)
                foreach (var x in supplies)
                {
                    if (x.Count <= 0 || !IsSupply(d, x.ItemId))
                        return false;
                    Add(selected, x.ItemId, x.Count);
                }

            foreach (var x in selected)
                if (Warehouse.CountOf(s.Warehouse, x.ItemId) < x.Count)
                    return false;
            foreach (var x in selected)
                Warehouse.TryRemove(s.Warehouse, x.ItemId, x.Count);
            var previousResult = s.Exploration?.Result;
            if (s.SurvivedExplorationMapIds == null) s.SurvivedExplorationMapIds = new List<string>();
            if (previousResult != null && previousResult.Outcome == ExplorationOutcome.Success &&
                !s.SurvivedExplorationMapIds.Contains(previousResult.MapId))
                s.SurvivedExplorationMapIds.Add(previousResult.MapId);
            var seed = s.TakeSeed();
            var e = new ExplorationState{Uid = "walk_" + seed.ToString("x8"), MapId = mapId, RngState = seed, Supplies = selected};
            e.RecoveryRun=recovery;
            if(recovery) e.LoanSupplies.AddRange(RaidProgression.LoanKit());
            s.RaidBase.RecoveryReady=false;
            e.LootCapacity = RaidEquipment.LootCapacity(s, d);
            s.Exploration = e;
            e.NodeCount = 6 + Roll(e, 5);
            e.IntermediateExitIndex = 2 + Roll(e, e.NodeCount - 3);
            e.Weather = (ExplorationWeather)Roll(e, 3);
            e.Ammo = AmmoRemaining(s);
            Enter(s, d, "거리 입구");
            // A new raid is safe until the player explicitly chooses an entrance.
            e.NodeIndex = -1;
            e.AwaitingEntryChoice = true;
            e.Phase = ExplorationPhase.Routes;
            e.Enemy = null; e.Detected = false;
            if (mapId == "YONGSAN_MARKET") {
                e.Routes = new[] { "1층 전자 매장", "지하 주차장" };
                e.RouteContainers = new[] { "Tool", "Pocket" };
                e.RouteDangerous = new[] { false, false }; e.RouteIndoors = new[] { true, true };
                if (FirstExplorationQuest.IsPending(s) && s.FirstExplorationQuest.Accepted && !s.FirstExplorationQuest.ReadyToReport)
                    e.RouteContainers = new[] { FirstExplorationQuest.RequiredContainer(s), FirstExplorationQuest.RequiredContainer(s) };
            }
            return true;
        }

        public static bool CanExtract(ExplorationState e)
        {
            return e != null && !e.AwaitingEntryChoice && e.Phase == ExplorationPhase.Routes && (e.NodeIndex == e.IntermediateExitIndex || e.NodeIndex == e.NodeCount - 1);
        }

        public static bool Move(GameSave s, IDataRegistry d, int routeIndex)
        {
            var e = s.Exploration;
            if (!IsActive(s) || e.Paused || e.Phase != ExplorationPhase.Routes || routeIndex < 0 || routeIndex > 1 || e.NodeIndex >= e.NodeCount - 1 || s.Player.Energy <= 0 || e.UseRemaining > 0)
                return false;
            s.Player.Hydration = Math.Max(0, s.Player.Hydration - (12-Math.Min(2,s.RaidBase.Supplies)));
            s.Player.Energy = Math.Max(0, s.Player.Energy - (14-Math.Min(2,s.RaidBase.Supplies)));
            if (s.Player.Energy == 0 && s.Player.Hydration == 0)
            {
                Finish(s, d, ExplorationOutcome.Exhausted, "수분과 에너지 고갈");
                return true;
            }

            string location = e.Routes[routeIndex];
            string container = e.RouteContainers != null && e.RouteContainers.Length > routeIndex ? e.RouteContainers[routeIndex] : null;
            e.NodeIndex++;
            e.AwaitingEntryChoice = false;
            Enter(s, d, location, container);
            return true;
        }

        static void Enter(GameSave s, IDataRegistry d, string location, string container = null)
        {
            var e = s.Exploration;
            e.Location = location;
            var site=RaidRegions.Find(e.MapId,location);
            e.Indoors=site?.Indoors ?? false; e.Dangerous=site?.Dangerous ?? false;
            e.ConversationOpen=false; e.EncounterNote=null; e.Initiative=false;
            e.DodgedThreat=false; e.DodgeCooldown=0;
            e.Enemy = null;
            e.EncounterRewarded = false;
            e.EncounterLoot.Clear();
            e.PlayerFeedback = e.EnemyFeedback = null;
            e.CoverRemaining = 0;
            e.AttackCooldown = 0;
            e.ContainerKind = container ?? LootContainers.Kinds[Roll(e, LootContainers.Kinds.Count)];
            bool firstQuestSearch = e.NodeIndex == 0 && e.MapId == "YONGSAN_MARKET" &&
                FirstExplorationQuest.IsPending(s) && s.FirstExplorationQuest.Accepted && !s.FirstExplorationQuest.ReadyToReport;
            if (firstQuestSearch) e.ContainerKind = FirstExplorationQuest.RequiredContainer(s);
            e.LootOptions = LootContainers.RollChoices(d, e.ContainerKind, count => Roll(e, count), e.MapId, e.Dangerous ? 3 : 2);
            GenerateRoutes(e);
            int kind = Roll(e, 100) < (e.Dangerous ? 70 : 35) ? Roll(e, 2) : 2 + Roll(e, 2);
            e.EncounterKind = new[]{"Scav", "PMC", "House", "Supplies"}[kind];
            e.Phase = ExplorationPhase.Encounter;
            double hearing = 7;
            var earId = PlayerEquipment.Equipped(s, "Earpiece");
            var ear = earId == null ? null : d.GetItem(earId);
            if (ear != null)
                hearing = Math.Max(7, ear.HearingRange);
            double playerHearing = Hearing(hearing, EffectiveWeather(e)), enemyHearing = Hearing(kind == 1 ? 12 : 7, EffectiveWeather(e));
            bool playerHeard = Chance(e, Math.Min(.95, playerHearing / 18));
            bool enemyHeard = Chance(e, Math.Min(.95, enemyHearing / 18));
            e.Detected = enemyHeard;
            if (!playerHeard && !enemyHeard && kind < 2)
            {
                e.EncounterKind = "House";
                kind = 2;
            }

            if (firstQuestSearch) { kind = 2; e.EncounterKind = "House"; e.Detected = false; }

            if (kind < 2)
            {
                string archetype = new[]{"Rifleman","Rusher","Sniper","Grenadier"}[Roll(e,4)];
                bool beginner=e.MapId=="YONGSAN_MARKET";
                double hp=kind==0 ? (beginner ? 55 : 70) : (beginner ? 80 : 105);
                e.Enemy = new ExplorationEnemy { Kind=e.EncounterKind, Name=(kind==0 ? "스캐브 " : "PMC ")+ArchetypeName(archetype), Archetype=archetype,
                    WeaponId=archetype=="Sniper" ? "WPN03" : kind==0 ? "WPN04" : "WPN01", Hp=hp, MaxHp=hp };
                if(!e.Indoors && e.Weather==ExplorationWeather.Rain) e.Detected=false;
                e.Initiative=!e.Detected;
                e.ScavAttitude=kind==0 ? (Roll(e,100)<20 ? "Hostile" : Roll(e,100)<60 ? "Friendly" : "Wary") : null;
            }
        }
        public static bool Choose(GameSave s, IDataRegistry d, EncounterChoice choice)
        {
            var e = s.Exploration;
            if (!IsActive(s) || e.Paused || e.Phase != ExplorationPhase.Encounter)
                return false;
            if (e.Enemy != null)
            {
                if (choice == EncounterChoice.Talk || choice == EncounterChoice.RequestAid || choice == EncounterChoice.Trade) return SocialChoice(s,d,choice);
                if (choice == EncounterChoice.Fight)
                {
                    BeginCombat(e);
                    return true;
                }

                if (choice == EncounterChoice.Avoid)
                {
                    if(e.Detected) {
                        if(s.Player.Energy<6) return false;
                        s.Player.Energy-=6;
                        if(!Chance(e,.65)) { e.EncounterNote="상대가 퇴로를 막았습니다. 엄폐를 준비하세요!"; BeginCombat(e); return true; }
                    }
                    e.ConversationOpen=false; e.EncounterNote="접촉을 피해 우회했습니다. 안전한 길에서 남은 물자를 찾습니다.";
                    e.Enemy = null;
                    PrepareLootChoice(e, d);
                    return true;
                }

                return false;
            }

            if (choice != EncounterChoice.Search && choice != EncounterChoice.Leave)
                return false;
            PrepareLootChoice(e, d);
            return choice == EncounterChoice.Search || ChooseLoot(s, 0);
        }

        static void PrepareLootChoice(ExplorationState e, IDataRegistry d)
        {
            // Existing saves may have entered before containers were introduced.
            if (string.IsNullOrEmpty(e.ContainerKind)) e.ContainerKind = "Pocket";
            if (e.LootOptions == null || e.LootOptions.Count == 0)
                e.LootOptions = LootContainers.RollChoices(d, e.ContainerKind, count => Roll(e, count), e.MapId, e.Dangerous ? 3 : 2);
            e.Phase = ExplorationPhase.LootChoice;
        }

        public static bool ChooseLoot(GameSave s, int index)
        {
            var e = s.Exploration;
            if (e == null || e.Paused || e.Phase != ExplorationPhase.LootChoice || e.EncounterRewarded ||
                e.LootOptions == null || index < 0 || index >= e.LootOptions.Count) return false;
            var item = e.LootOptions[index];
            GrantLoot(e, item.ItemId, item.Count);
            Add(e.EncounterLoot, item.ItemId, item.Count);
            e.EncounterRewarded = true;
            FirstExplorationQuest.OnContainerLooted(s, e.ContainerKind);
            e.Phase = ExplorationPhase.EncounterResult;
            return true;
        }

        public static bool ContinueEncounter(GameSave s)
        {
            var e = s.Exploration;
            if (e == null || e.Paused || e.Phase != ExplorationPhase.EncounterResult || e.PendingLoot.Count > 0)
                return false;
            e.Phase = ExplorationPhase.Routes;
            return true;
        }

        public static bool CanCarry(ExplorationState e, string id)
        {
            foreach (var item in e.Loot) if (item.ItemId == id) return true;
            return e.Loot.Count < Math.Max(8, e.LootCapacity);
        }

        static void GrantLoot(ExplorationState e, string id, int count)
        {
            Add(CanCarry(e, id) ? e.Loot : e.PendingLoot, id, count);
        }

        public static bool ResolvePendingLoot(GameSave s, bool take)
        {
            var e = s.Exploration;
            if (e == null || e.Paused || e.Phase != ExplorationPhase.EncounterResult || e.PendingLoot.Count == 0) return false;
            var item = e.PendingLoot[0];
            if (take && !CanCarry(e, item.ItemId)) return false;
            if (take) Add(e.Loot, item.ItemId, item.Count);
            else Remove(e.EncounterLoot, item.ItemId, item.Count);
            e.PendingLoot.RemoveAt(0);
            return true;
        }

        public static bool DiscardLoot(GameSave s, string id)
        {
            var e = s.Exploration;
            if (e == null || e.Paused || (e.Phase != ExplorationPhase.Routes && e.Phase != ExplorationPhase.EncounterResult && e.Phase != ExplorationPhase.LootChoice)) return false;
            int removed = e.Loot.RemoveAll(x => x.ItemId == id);
            if (removed > 0) e.EncounterLoot.RemoveAll(x => x.ItemId == id);
            if (removed > 0) e.Ammo = AmmoRemaining(s);
            return removed > 0;
        }

        public static double Accuracy(double value, ExplorationWeather w)
        {
            return value * (w == ExplorationWeather.Fog ? .7 : 1);
        }

        public static double Hearing(double value, ExplorationWeather w)
        {
            return value * (w == ExplorationWeather.Rain ? .65 : 1);
        }

        static bool Ready(GameSave s)
        {
            return IsActive(s) && !s.Exploration.Paused && s.Exploration.Phase == ExplorationPhase.Combat && s.Exploration.UseRemaining <= 0 && s.Exploration.AttackCooldown <= 0;
        }

        public static bool Attack(GameSave s, IDataRegistry d, FireMode mode)
        {
            if (!Ready(s))
                return false;
            var e = s.Exploration;
            var p = CombatProfiles.For(PlayerEquipment.Equipped(s, "Weapon"));
            if (p == null || Array.IndexOf(p.Modes, mode) < 0)
                return false;
            int rounds = mode == FireMode.Single ? 1 : mode == FireMode.Burst ? 3 : 5;
            if (AmmoRemaining(s) < rounds)
                return false;
            int needed = ConsumeAmmo(e.LoanSupplies, p.Caliber, rounds);
            needed = ConsumeAmmo(e.Supplies, p.Caliber, needed);
            ConsumeAmmo(e.Loot, p.Caliber, needed);
            e.ShotsSinceReload += rounds;
            e.Ammo = AmmoRemaining(s);
            double hit = Accuracy(p.Accuracy * (mode == FireMode.Single ? 1 : mode == FireMode.Burst ? .83 : .68), EffectiveWeather(e));
            double damage = 0;
            for (int i = 0; i < rounds; i++)
                if (Chance(e, hit))
                    damage += p.Damage * (e.Enemy.Action == EnemyAction.Cover ? .3 : 1);
            e.AttackCooldown = (mode == FireMode.Single ? .7 : mode == FireMode.Burst ? 1.2 : 1.8) * (s.Player.Hydration < 25 ? 1.6 : 1);
            if (e.ShotsSinceReload >= p.Magazine)
            {
                e.AttackCooldown += 2;
                e.ShotsSinceReload = 0;
            }
            e.PlayerFeedback = damage > 0 ? Loc.Text("명중! 적 HP −{0:0}", Math.Min(damage, e.Enemy.Hp)) : Loc.Text("빗나갔습니다 · 탄약 {0}발 사용", rounds);
            if (damage > 0 && e.Enemy.Action == EnemyAction.Cover) e.PlayerFeedback += Loc.Text(" · 적 엄폐로 피해 감소");
            if (e.ShotsSinceReload == 0) e.PlayerFeedback += Loc.Text(" · 자동 재장전");
            HurtEnemy(s, d, damage);
            return true;
        }

        public static bool Melee(GameSave s, IDataRegistry d)
        {
            if (!Ready(s) || PlayerEquipment.Equipped(s, "Melee") == null)
                return false;
            s.Exploration.AttackCooldown = 1.2;
            s.Exploration.PlayerFeedback = Loc.Text("근접 명중! 적 HP −{0:0}", Math.Min(26, s.Exploration.Enemy.Hp));
            HurtEnemy(s, d, 26);
            return true;
        }

        static void HurtEnemy(GameSave s, IDataRegistry d, double damage)
        {
            var e = s.Exploration;
            e.Enemy.Hp = Math.Max(0, e.Enemy.Hp - damage);
            if (e.Enemy.Hp <= 0)
            {
                if(e.Dangerous) {
                    e.Phase=ExplorationPhase.LootChoice;
                    e.EncounterNote="위험한 구역의 적을 쓰러뜨렸습니다. 전리품 후보 중 하나를 골라 챙기세요.";
                    return;
                }
                Reward(s, d);
                e.Phase = ExplorationPhase.EncounterResult;
            }
            else if (damage > 0 && e.Enemy.Action != EnemyAction.Aiming && e.Enemy.Action != EnemyAction.Grenade && e.Enemy.Action != EnemyAction.Rush)
            {
                e.Enemy.Action = EnemyAction.Injured;
                e.Enemy.Remaining = .45;
            }
        }

        public static bool Cover(GameSave s)
        {
            if (!IsActive(s) || s.Exploration.Paused || s.Exploration.Phase != ExplorationPhase.Combat || s.Exploration.CoverCooldown > 0 || s.Exploration.UseRemaining > 0)
                return false;
            s.Exploration.CoverRemaining = 1.8;
            s.Exploration.CoverCooldown = 3.5;
            s.Exploration.PlayerFeedback = Loc.Text("엄폐! 1.8초 동안 받는 피해 80% 감소");
            return true;
        }

        public static bool Use(GameSave s, IDataRegistry d, string itemId)
        {
            var p = CombatProfiles.ConsumableFor(itemId);
            if (p == null)
                return false;
            if (!IsActive(s))
            {
                if (s.Exploration?.Result != null && !s.Exploration.Result.Acknowledged)
                    return false;
                if (!Warehouse.TryRemove(s.Warehouse, itemId, 1))
                    return false;
                Apply(s, p);
                return true;
            }

            var e = s.Exploration;
            if (e.Paused || e.Phase == ExplorationPhase.EncounterResult || e.Phase == ExplorationPhase.LootChoice || e.UseRemaining > 0 || !Remove(e.LoanSupplies,itemId,1) && !Remove(e.Supplies, itemId, 1) && !Remove(e.Loot, itemId, 1))
                return false;
            e.PendingItemId = itemId;
            e.UseRemaining = p.Seconds;
            return true;
        }

        static void Apply(GameSave s, ConsumableProfile p)
        {
            s.Player.Hp = Clamp(s.Player.Hp + p.Hp + (IsActive(s) && p.Hp>0 ? Math.Min(2,s.RaidBase.Clinic)*10 : 0));
            s.Player.Hydration = Clamp(s.Player.Hydration + p.Hydration);
            s.Player.Energy = Clamp(s.Player.Energy + p.Energy);
        }

        static double Clamp(double n)
        {
            return Math.Max(0, Math.Min(100, n));
        }

        public static void Tick(GameSave s, IDataRegistry d, double seconds)
        {
            if (!IsActive(s) || s.Exploration.Paused || s.Exploration.Phase == ExplorationPhase.EncounterResult || s.Exploration.Phase == ExplorationPhase.LootChoice || seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                return;
            var e = s.Exploration;
            if (s.Player.Hp <= 0)
            {
                Finish(s, d, ExplorationOutcome.Death, "부상");
                return;
            }

            if (s.Player.Energy <= 0 && s.Player.Hydration <= 0 && e.UseRemaining <= 0)
            {
                Finish(s, d, ExplorationOutcome.Exhausted, "수분과 에너지 고갈");
                return;
            }

            // Small fixed slices preserve telegraphs and timed cover even for a slow frame. No wall-clock catch-up.
            double remaining = Math.Min(seconds, 30);
            while (remaining > 0 && IsActive(s))
            {
                double dt = Math.Min(.05, remaining);
                remaining -= dt;
                e.CoverRemaining = Math.Max(0, e.CoverRemaining - dt);
                e.CoverCooldown = Math.Max(0, e.CoverCooldown - dt);
                e.DodgeCooldown = Math.Max(0, e.DodgeCooldown - dt);
                e.AttackCooldown = Math.Max(0, e.AttackCooldown - dt);
                if (e.UseRemaining > 0)
                {
                    e.UseRemaining = Math.Max(0, e.UseRemaining - dt);
                    if (e.UseRemaining == 0)
                    {
                        Apply(s, CombatProfiles.ConsumableFor(e.PendingItemId));
                        e.PendingItemId = null;
                    }
                }

                if (e.Phase != ExplorationPhase.Combat || e.Enemy == null)
                    continue;
                e.Enemy.Remaining -= dt;
                if (e.Enemy.Remaining > 0)
                    continue;
                switch (e.Enemy.Action)
                {
                    case EnemyAction.Alert:
                    case EnemyAction.Injured:
                    case EnemyAction.Reloading:
                        if(e.Enemy.Archetype!=null) { PickEnemyPattern(e); break; }
                        e.Enemy.Action = EnemyAction.Aiming;
                        e.Enemy.Remaining = 1.7;
                        break;
                    case EnemyAction.Aiming:
                        e.Enemy.Action = EnemyAction.Firing;
                        e.Enemy.Remaining = .25;
                        EnemyFire(s, d);
                        break;
                    case EnemyAction.Firing:
                        e.Enemy.Action = EnemyAction.Cover;
                        e.Enemy.Remaining = 1.5;
                        break;
                    case EnemyAction.Grenade:
                        e.Enemy.Action=EnemyAction.Explosion; e.Enemy.Remaining=.4;
                        ThreatDamage(s,d,true); break;
                    case EnemyAction.Rush:
                        ThreatDamage(s,d,false);
                        e.Enemy.Action=EnemyAction.Reloading; e.Enemy.Remaining=1.8; break;
                    default:
                        e.Enemy.Action = EnemyAction.Reloading;
                        e.Enemy.Remaining = 1.1;
                        break;
                }
            }
        }

        static void EnemyFire(GameSave s, IDataRegistry d)
        {
            var e = s.Exploration;
            if (!Chance(e, Accuracy(.86, EffectiveWeather(e)))) {
                e.EnemyFeedback = Loc.Text("적의 사격이 빗나갔습니다 · 피해 없음"); return;
            }
            double armor = RaidEquipment.ArmorReduction(s, d);
            double baseDamage=e.Enemy.Kind == "PMC" ? 27 : 18;
            if(e.Enemy.Archetype!=null) baseDamage=(e.MapId=="YONGSAN_MARKET" ? 16 : 22)+(e.Enemy.Archetype=="Sniper" ? 9 : 0);
            double damage = baseDamage * (1 - armor) * (e.CoverRemaining > 0 ? .2 : 1);
            e.EnemyFeedback = Loc.Text("피격 · 내 HP −{0:0}", Math.Min(damage, s.Player.Hp));
            if (e.CoverRemaining > 0) e.EnemyFeedback += Loc.Text(" · 엄폐로 피해 80% 감소");
            if (armor > 0) e.EnemyFeedback += Loc.Text(" · 방어구 적용");
            s.Player.Hp = Math.Max(0, s.Player.Hp - damage);
            if (s.Player.Hp <= 0)
                Finish(s, d, ExplorationOutcome.Death, "총상", e.Enemy);
        }

        static void Reward(GameSave s, IDataRegistry d)
        {
            var e = s.Exploration;
            if (e.EncounterRewarded)
                return;
            e.EncounterRewarded = true;
            var map = d.GetMap(e.MapId);
            var table = map == null ? null : d.GetLootTable(map.LootTableId);
            if (table == null || table.Entries == null)
                return;
            int weight = 0;
            foreach (var x in table.Entries)
                weight += Math.Max(0, x.Weight);
            if (weight <= 0)
                return;
            int pick = Roll(e, weight);
            foreach (var x in table.Entries)
            {
                pick -= Math.Max(0, x.Weight);
                if (pick < 0)
                {
                    int count = Math.Max(1, x.CountMin) + Roll(e, Math.Max(1, x.CountMax - Math.Max(1, x.CountMin) + 1));
                    GrantLoot(e, x.ItemId, count);
                    Add(e.EncounterLoot, x.ItemId, count);
                    break;
                }
            }
        }

        public static bool Extract(GameSave s, IDataRegistry d)
        {
            if (!CanExtract(s.Exploration) || s.Exploration.Paused || s.Exploration.UseRemaining > 0)
                return false;
            Finish(s, d, ExplorationOutcome.Success, "탈출");
            return true;
        }

        public static bool EmergencyReturn(GameSave s, IDataRegistry d)
        {
            if (!IsActive(s) || s.Exploration.Paused)
                return false;
            Finish(s, d, ExplorationOutcome.Emergency, "긴급 구조 요청");
            return true;
        }

        static void Finish(GameSave s, IDataRegistry d, ExplorationOutcome outcome, string cause, ExplorationEnemy killer = null)
        {
            var e = s.Exploration;
            if (e.Result != null)
                return;
            var r = new ExplorationResult{Id = e.Uid + "_result", MapId = e.MapId, Outcome = outcome, CharacterLevel = s.Player.CharacterLevel, Cause = cause, KillerName = killer?.Name, KillerKind = killer?.Kind, KillerWeaponId = killer?.WeaponId};
            e.Result = r;
            if (outcome == ExplorationOutcome.Success)
            {
                FirstExplorationQuest.OnSuccessfulReturn(s);
                RegionalExplorationQuest.OnSuccessfulReturn(s);
                if (s.SurvivedExplorationMapIds == null) s.SurvivedExplorationMapIds = new List<string>();
                if (!s.SurvivedExplorationMapIds.Contains(e.MapId)) s.SurvivedExplorationMapIds.Add(e.MapId);
                r.Items.AddRange(e.Loot);
                foreach (var x in e.Loot)
                    Store(s, d, x);
            }
            else
                r.LostLoot.AddRange(e.Loot);
            foreach (var x in e.Supplies)
                Store(s, d, x);
            e.Supplies.Clear();
            e.LoanSupplies.Clear();
            e.Loot.Clear();
            e.PendingItemId = null;
            e.UseRemaining = 0;
            e.Phase = ExplorationPhase.Result;
            r.Settled = true;
            if (outcome != ExplorationOutcome.Success)
                s.Player.Hp = Math.Max(25, s.Player.Hp);
        }

        static void Store(GameSave s, IDataRegistry d, ItemStack x)
        {
            int overflow = Warehouse.TryAdd(s.Warehouse, d, x.ItemId, x.Count);
            if (overflow > 0)
            {
                if (s.ExplorationOverflow == null)
                    s.ExplorationOverflow = new List<ItemStack>();
                Add(s.ExplorationOverflow, x.ItemId, overflow);
            }
        }

        public static int ClaimOverflow(GameSave s, IDataRegistry d)
        {
            if (s.ExplorationOverflow == null)
                return 0;
            int claimed = 0;
            for (int i = s.ExplorationOverflow.Count - 1; i >= 0; i--)
            {
                var x = s.ExplorationOverflow[i];
                int left = Warehouse.TryAdd(s.Warehouse, d, x.ItemId, x.Count);
                claimed += x.Count - left;
                if (left == 0)
                    s.ExplorationOverflow.RemoveAt(i);
                else
                    s.ExplorationOverflow[i] = new ItemStack(x.ItemId, left);
            }

            return claimed;
        }

        public static bool Acknowledge(GameSave s)
        {
            var r = s.Exploration?.Result;
            if (r == null || r.Acknowledged || !r.Settled)
                return false;
            r.Acknowledged = true;
            return true;
        }

        public static bool Rest(GameSave s)
        {
            if (IsActive(s) || (s.Exploration?.Result != null && !s.Exploration.Result.Acknowledged))
                return false;
            s.Player.Hp = s.Player.Hydration = s.Player.Energy = 100;
            return true;
        }

        public static bool ClaimStarterKit(GameSave s, IDataRegistry d)
        {
            if (s.ExplorationStarterClaimed || IsActive(s))
                return false;
            var kit = new[]{new ItemStack("WPN04", 1), new ItemStack("MEL01", 1), new ItemStack("MED05", 3), new ItemStack("FOOD01", 3), new ItemStack("FOOD05", 2), new ItemStack("AMO05", 50)};
            foreach (var x in kit)
                if (d.GetItem(x.ItemId) == null)
                    return false;
            foreach (var x in kit)
                Store(s, d, x);
            s.ExplorationStarterClaimed = true;
            return true;
        }

        // One-time onboarding/migration. Preserve an existing loadout, including on old saves.
        public static bool PrepareStarter(GameSave s, IDataRegistry d)
        {
            if (s.ExplorationStarterPrepared || IsActive(s) ||
                (s.Exploration?.Result != null && !s.Exploration.Result.Acknowledged)) return false;
            if (!s.ExplorationStarterClaimed && !ClaimStarterKit(s, d)) return false;
            if (string.IsNullOrEmpty(PlayerEquipment.Equipped(s, "Weapon")))
            {
                if (!PlayerEquipment.TryEquip(s, d, "WPN04"))
                {
                    // A full warehouse must not prevent receiving the starting pistol.
                    // Older saves may have already sold/transferred their manually claimed kit.
                    // The new one-time preparation marker also repairs that empty-loadout case.
                    if (d.GetItem("WPN04") == null) return false;
                    if (s.ExplorationOverflow != null) Remove(s.ExplorationOverflow, "WPN04", 1);
                    if (s.Player.Equipment == null) s.Player.Equipment = new Dictionary<string, string>();
                    s.Player.Equipment["Weapon"] = "WPN04";
                }
            }
            s.ExplorationStarterPrepared = true;
            return true;
        }

        static int Roll(ExplorationState e, int max)
        {
            var rng = new Rng(e.RngState);
            uint n = rng.NextUInt();
            e.RngState = n;
            return (int)(n % (uint)max);
        }

        static bool Chance(ExplorationState e, double chance)
        {
            return Roll(e, 1000000) < chance * 1000000;
        }

        static void Add(List<ItemStack> list, string id, int count)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].ItemId == id)
                {
                    list[i] = new ItemStack(id, list[i].Count + count);
                    return;
                }

            list.Add(new ItemStack(id, count));
        }

        static bool Remove(List<ItemStack> list, string id, int count)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].ItemId == id && list[i].Count >= count)
                {
                    var x = list[i];
                    if (x.Count == count)
                        list.RemoveAt(i);
                    else
                        list[i] = new ItemStack(id, x.Count - count);
                    return true;
                }

            return false;
        }

        public static int AmmoRemaining(GameSave s)
        {
            var p = CombatProfiles.For(PlayerEquipment.Equipped(s, "Weapon"));
            if (p == null || s.Exploration == null)
                return 0;
            int total = 0;
            foreach(var x in s.Exploration.LoanSupplies) if(AmmoCaliber(x.ItemId)==p.Caliber) total+=x.Count;
            foreach (var x in s.Exploration.Supplies)
                if (AmmoCaliber(x.ItemId) == p.Caliber)
                    total += x.Count;
            foreach (var x in s.Exploration.Loot)
                if (AmmoCaliber(x.ItemId) == p.Caliber)
                    total += x.Count;
            return total;
        }

        static int ConsumeAmmo(List<ItemStack> supplies, string caliber, int count)
        {
            for (int i = supplies.Count - 1; i >= 0 && count > 0; i--)
            {
                var x = supplies[i];
                if (AmmoCaliber(x.ItemId) != caliber)
                    continue;
                int taken = Math.Min(x.Count, count);
                Remove(supplies, x.ItemId, taken);
                count -= taken;
            }

            return count;
        }

        public static string AmmoCaliber(string id)
        {
            switch (id)
            {
                case "AMO01":
                case "AMO02":
                case "AMO26":
                    return "5.45x39";
                case "AMO03":
                case "AMO04":
                    return "5.56x45";
                case "AMO05":
                case "AMO06":
                    return "9x19";
                case "AMO07":
                case "AMO08":
                    return "7.62x39";
                case "AMO09":
                case "AMO10":
                    return "7.62x51";
                case "AMO11":
                case "AMO12":
                    return "7.62x54R";
                case "AMO13":
                case "AMO24":
                    return "4.6x30";
                case "AMO14":
                    return "5.7x28";
                case "AMO15":
                case "AMO16":
                case "AMO19":
                    return "12/70";
                case "AMO17":
                    return "23x75";
                case "AMO18":
                    return "9x39";
                case "AMO20":
                case "AMO23":
                    return ".50 AE";
                case "AMO21":
                case "AMO22":
                    return "MXMR";
                case "AMO25":
                    return "STUN";
                default:
                    return null;
            }
        }
    }
}
