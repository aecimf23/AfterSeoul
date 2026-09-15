using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Expedition
{
    /// <summary>
    /// 파견의 출발과 복귀.
    ///
    /// <b>핵심 규칙: 결과는 출발 시점에 이미 결정돼 있다.</b>
    /// 복귀 정산은 그 결정을 "펼쳐 보는" 것뿐이다. 정산 시점에 새 난수를 뽑지 않는다.
    /// 그래서 복귀 직전에 앱을 껐다 켜도, 시계를 앞당겨도 결과가 바뀌지 않는다.
    /// </summary>
    public sealed class ExpeditionSystem : ITimelineSystem
    {
        public string Name => "Expedition";

        // ── 출발 ──────────────────────────────────────────────

        /// <summary>
        /// 파견을 보낸다. 파견비를 즉시 차감하고 시드를 확정한다.
        /// 보낼 수 없으면 <b>아무것도 건드리지 않고</b> null — 사유는 <see cref="DepartBlockReason"/>.
        /// </summary>
        /// <param name="rescueScavUid">
        /// 구조하러 가는 실종자. 비어 있으면 평범한 탐색이다 (GDD §15).
        /// </param>
        public ExpeditionState Depart(
            GameSave save, IDataRegistry data,
            string mapId, IReadOnlyList<string> scavUids,
            System.DateTimeOffset now, string rescueScavUid = null)
        {
            // 검사는 전부 DepartBlockReason 에 있다. 여기서 조건을 따로 또 쓰면
            // 화면이 "보낼 수 있다"고 그리는 근거와 어긋난다.
            if (DepartBlockReason(save, data, mapId, scavUids) != null) return null;

            // 구조는 신호가 살아 있을 때만. 시한이 지난 뒤에도 보낼 수 있으면 시한이 없는 것이다.
            if (!string.IsNullOrEmpty(rescueScavUid) &&
                !RescueSystem.IsRescuable(FindScav(save, rescueScavUid), now)) return null;

            var map = data.GetMap(mapId);
            long cost = CostFor(save, data, map, scavUids);

            save.Player.Money -= cost;

            var exp = new ExpeditionState
            {
                Uid = "ex_" + save.RngCounter.ToString("x8"),
                MapId = mapId,
                DepartedAt = now,
                ReturnsAt = now.AddMinutes(map.DurationMinutes),
                CostPaid = cost,
                Seed = save.TakeSeed(),   // ← 여기서 결과가 확정된다
                Resolved = false,
                RescueScavUid = rescueScavUid,
            };
            exp.ScavUids.AddRange(scavUids);

            foreach (var uid in scavUids)
            {
                var scav = FindScav(save, uid);
                scav.Status = ScavStatus.OnExpedition;
                scav.ExpeditionCount++;
            }

            if (save.ExploredMapIds == null) save.ExploredMapIds = new List<string>();
            if (!save.ExploredMapIds.Contains(mapId)) save.ExploredMapIds.Add(mapId);
            Orientation.SkipPending(save);
            save.Expeditions.Add(exp);
            return exp;
        }

        /// <summary>
        /// 보낼 수 없는 이유. 보낼 수 있으면 null.
        ///
        /// <para><see cref="Depart"/> 가 이 함수를 그대로 쓴다. 화면은 버튼을 회색으로 만들 때
        /// 같은 문장을 보여주면 되고, "왜 안 되는지 모르겠는" 버튼이 생기지 않는다.</para>
        /// </summary>
        public static string DepartBlockReason(
            GameSave save, IDataRegistry data, string mapId, IReadOnlyList<string> scavUids)
        {
            var map = data.GetMap(mapId);
            if (map == null) return "지역 정보를 찾을 수 없습니다";

            string locked = MapUnlock.LockReason(save, map);
            if (locked != null) return locked;

            if (scavUids == null || scavUids.Count == 0) return "보낼 스캐브를 선택하세요";

            foreach (var uid in scavUids)
            {
                var scav = FindScav(save, uid);
                if (scav == null) return "없는 스캐브가 포함돼 있습니다";
                if (scav.Status != ScavStatus.Idle)
                    return $"{scav.Name} 은(는) 지금 보낼 수 없습니다";
            }

            // 무기는 필수다. 나머지 다섯 칸은 비워도 나갈 수 있다 —
            // 단검 한 자루만 쥐여 보내는 것도 선택지다 (GDD §7).
            var unarmed = Scav.Equipment.UnarmedNames(save, data, scavUids);
            if (unarmed.Count > 0)
                return unarmed.Count == 1
                    ? $"{unarmed[0]} 에게 무기가 없습니다"
                    : $"무기 없는 인원 {unarmed.Count}명 — {string.Join(", ", unarmed.ToArray())}";

            long cost = CostFor(save, data, map, scavUids);
            if (save.Player.Money < cost)
                return $"자금 부족 — {cost - save.Player.Money:N0}원 더 필요합니다";

            return null;
        }

        /// <summary>
        /// 파견비 = 인건비 + 보급비 (GDD §7).
        ///
        /// <para><b>팀 구성이 값을 바꾼다.</b> 예전에는 지역 고정값이라 인원을 늘려도 공짜였는데,
        /// 정산 쪽은 생존·전투를 <i>합</i>으로 쓰고 회수량은 탐색의 <i>평균</i>으로 쓴다.
        /// 즉 사람을 더 넣으면 안전해지기만 하고 손해가 없어서 "항상 전원 파견"이 정답이 됐다.
        /// 그러면 GDD 가 요구하는 "파견 결정 자체가 손익 판단"이 성립하지 않는다.</para>
        ///
        /// <para>인건비는 시급에 비례한다 — 고급 스캐브를 보내면 비싸다.
        /// 보급비는 머릿수에 비례한다 — 한 사람당 물과 탄약이 든다.
        /// 티어 1 한 명을 보내면 <c>expeditions.json</c> 의 값 그대로가 나온다.
        /// 그 지점을 기준으로 데이터가 잡혀 있고, 회수가치 대비 비율 검증도 거기에 걸려 있다.</para>
        /// </summary>
        public static long CostFor(GameSave save, IDataRegistry data, MapDef map, IReadOnlyList<string> scavUids)
        {
            if (map == null) return 0;

            long baseWage = data.Balance.BaseWagePerHour;
            if (baseWage <= 0) baseWage = 1;

            long wageSum = 0;
            int headCount = 0;
            if (scavUids != null)
            {
                foreach (var uid in scavUids)
                {
                    var s = FindScav(save, uid);
                    // 없는 uid 는 값을 0 으로 흘리지 않고 기준 시급으로 친다.
                    // 여기서 싸지면 "이상한 uid 를 넣으면 공짜"가 된다.
                    wageSum += s != null && s.WagePerHour > 0 ? s.WagePerHour : baseWage;
                    headCount++;
                }
            }
            if (headCount == 0) { wageSum = baseWage; headCount = 1; }

            long labor = (long)System.Math.Round(map.BaseCostWage * (double)wageSum / baseWage);
            long supply = map.BaseCostSupply * headCount;
            return labor + supply;
        }

        /// <summary>지역 목록처럼 팀이 정해지기 전에 보여줄 기준값 — 티어 1 한 명 기준.</summary>
        public static long BaselineCost(MapDef map)
        {
            return map == null ? 0 : map.BaseCostWage + map.BaseCostSupply;
        }

        // ── 복귀 (오프라인 정산) ────────────────────────────────

        public IEnumerable<TimedEvent> CollectEvents(ResolveWindow window, ResolveContext ctx)
        {
            foreach (var exp in ctx.Save.Expeditions)
            {
                if (exp.Resolved) continue;              // 멱등성: 이미 정산된 것은 다시 내보내지 않는다
                if (exp.ReturnsAt > window.To) continue; // 아직 안 돌아왔다

                var captured = exp;
                yield return new TimedEvent(
                    // 창을 자른 경우(MaxCatchUp) 복귀 시각이 window.From 보다 앞설 수 있다.
                    // 그때도 사건은 처리하되 시각만 구간 안으로 당긴다. 잃어버리면 안 된다.
                    captured.ReturnsAt < window.From ? window.From : captured.ReturnsAt,
                    EventOrder.ExpeditionReturn,
                    "expedition:" + captured.Uid,
                    c => ApplyReturn(c, captured));
            }
        }

        private void ApplyReturn(ResolveContext ctx, ExpeditionState exp)
        {
            if (exp.Resolved) return; // 이중 안전장치

            if (exp.IsOrientation) { Orientation.Return(ctx, exp); return; }

            var save = ctx.Save;
            var data = ctx.Data;
            var map = data.GetMap(exp.MapId);

            var result = new ExpeditionResult
            {
                ExpeditionUid = exp.Uid,
                MapId = exp.MapId,
                ReturnedAt = exp.ReturnsAt,

                // 출발할 때 낸 돈. 세이브에는 처음부터 적히고 있었는데 읽는 곳이 없어서
                // 화면에는 회수품만 보이고 "남았는지"는 끝내 보이지 않았다.
                CostPaid = exp.CostPaid,
            };

            // 용도별로 난수 스트림을 가른다. 나중에 전투 판정을 한 번 더 굴리도록
            // 고쳐도 전리품 결과는 그대로 유지된다. (Rng.Derive 주석 참조)
            var lootRng = Rng.For(exp.Seed, "loot");
            var accidentRng = Rng.For(exp.Seed, "accident");
            var combatRng = Rng.For(exp.Seed, "combat");

            int searchTotal = 0, survivalTotal = 0, combatTotal = 0;
            foreach (var uid in exp.ScavUids)
            {
                var s = FindScav(save, uid);
                if (s == null) continue;
                searchTotal += s.Search;
                survivalTotal += s.Survival;
                combatTotal += s.Combat;
            }
            int teamSize = exp.ScavUids.Count > 0 ? exp.ScavUids.Count : 1;

            // 장비는 출발 시점의 것이 아니라 지금 착용 중인 것을 읽는다. 파견 중에는
            // 장비를 바꿀 수 없으므로(Equipment.EquipBlockReason) 둘은 같다.
            var gear = Scav.Equipment.TeamEffectsOf(save, data, exp.ScavUids);
            survivalTotal += gear.SurvivalBonus;

            // ── 사건 ──
            // 전투·사고보다 먼저 굴린다. 매복은 사고를 부르고 통행료는 돈을 건드리므로,
            // 결과에 영향을 주려면 그것들이 정해지기 전에 있어야 한다.
            var eventRng = Rng.For(exp.Seed, "event");
            var rolled = ExpeditionEvents.Roll(
                data, map, ref eventRng, searchTotal, combatTotal, survivalTotal);

            if (!rolled.IsNone)
            {
                result.EventId = rolled.Id;
                result.EventPassed = rolled.Passed;

                long money = ExpeditionEvents.MoneyDelta(data, rolled);
                if (money != 0)
                {
                    // 마이너스라도 빚을 지우지는 않는다. 통행료를 못 내서 잔고가 음수가 되면
                    // 그건 벌이 아니라 고장으로 읽힌다.
                    save.Player.Money += money;
                    if (save.Player.Money < 0) save.Player.Money = 0;
                }

                int trust = ExpeditionEvents.TrustDelta(data, rolled);
                if (trust != 0 && !string.IsNullOrEmpty(save.Player.EmployerNpcId))
                {
                    int current;
                    save.NpcTrust.TryGetValue(save.Player.EmployerNpcId, out current);
                    save.NpcTrust[save.Player.EmployerNpcId] = current + trust;
                }

                if (ExpeditionEvents.CausesAccident(data, rolled)) result.HadAccident = true;
            }

            // ── 전투 ──
            if (map != null && combatRng.Chance(map.CombatChance))
            {
                result.HadCombat = true;
                // 전투력이 위험도에 못 미치면 사고 판정을 한 번 더 받는다.
                bool won = combatTotal >= map.RiskLevel * 4;
                if (!won && accidentRng.Chance(0.5)) result.HadAccident = true;
            }

            // ── 사고 ──
            if (map != null)
            {
                double riskScore = map.RiskLevel * 10.0;
                double defense = survivalTotal * 0.8;
                double chance = (riskScore - defense) / 100.0;
                if (chance < 0.05) chance = 0.05;
                if (chance > 0.60) chance = 0.60;
                if (accidentRng.Chance(chance)) result.HadAccident = true;
            }

            if (result.HadAccident)
            {
                // 사고가 나도 팀 전원이 당하지는 않는다. 한 명은 반드시 당하고,
                // 나머지는 각자 생존 능력치로 따로 굴린다.
                //
                // 전원 피해로 두면 인원을 늘릴수록 기대 손실이 사람 수만큼 커져서,
                // 파견비까지 오른 지금은 팀을 짤 이유가 아예 사라진다.
                // 3000회 시뮬에서 파견당 상실률이 11.3% 였던 것도 대부분 이 연쇄였다.
                int count = exp.ScavUids.Count;
                int victim = count > 0 ? accidentRng.NextInt(count) : 0;

                for (int i = 0; i < count; i++)
                {
                    var s = FindScav(save, exp.ScavUids[i]);
                    if (s == null) continue;

                    if (i != victim && !accidentRng.Chance(SpreadChance(s.Survival)))
                    {
                        s.Status = ScavStatus.Idle;   // 휘말리지 않았다
                        continue;
                    }

                    // 특성도 개인의 것이다 (GDD §14).
                    var traits = Scav.Traits.EffectsOf(s, data, exp.MapId);

                    // 겁쟁이는 먼저 도망친다. 지목당한 사람도 빠져나갈 수 있다 —
                    // 도주가 "휘말림"의 뒤에만 붙으면 정작 제일 위험한 자리에서는 안 쓰인다.
                    // 대신 회수량 대가를 항상 치르므로 공짜가 아니다.
                    if (traits.FleeChance > 0 && accidentRng.Chance(traits.FleeChance))
                    {
                        s.Status = ScavStatus.Idle;
                        continue;
                    }

                    // 강등은 개인의 장비가 한다 — 내 헬멧이 나를 지키는 것이지 팀원의 헬멧이
                    // 나를 지키지는 않는다. 그래서 팀 합계(gear)가 아니라 본인 것을 읽는다.
                    var own = Scav.Equipment.EffectsOf(s, data);
                    var status = RollSeverity(
                        ref accidentRng, s.Survival,
                        own.SeverityMitigation + traits.SeverityMitigation
                            + Employers.SeverityMitigationBonus(save, data),
                        data.Balance.Equipment);
                    s.Status = status;

                    if (status == ScavStatus.Injured) result.InjuredScavUids.Add(s.Uid);
                    else
                    {
                        result.LostScavUids.Add(s.Uid);
                        ctx.Report.ScavCasualties.Add(s.Uid);

                        // 실종은 아직 끝나지 않았다 — 어디서 언제 잃었는지를 남겨야
                        // 나중에 무전을 잡을 수 있다 (GDD §15, RescueSystem).
                        if (status == ScavStatus.Missing)
                        {
                            s.LostAt = ctx.EventTime;
                            s.LostAtMapId = exp.MapId;
                        }

                        // 돌아오지 못한 사람의 장비는 같이 사라진다 (GDD §7).
                        foreach (var lostItem in Scav.Equipment.StripLostGear(s))
                            result.LostGear.Add(lostItem);
                    }
                }
            }
            else
            {
                foreach (var uid in exp.ScavUids)
                {
                    var s = FindScav(save, uid);
                    if (s != null) s.Status = ScavStatus.Idle;
                }
            }

            // ── 구조 ──
            // 사고 판정 뒤에 둔다. 데리러 간 팀이 먼저 당했는데 사람은 데려왔다면
            // 그건 이야기가 안 된다.
            if (!string.IsNullOrEmpty(exp.RescueScavUid))
            {
                var lost = FindScav(save, exp.RescueScavUid);
                result.RescueScavUid = exp.RescueScavUid;

                // 실종자를 데려오는 건 버티는 일이라 생존으로 본다. 데리러 간 사람이
                // 하나도 못 돌아왔으면 구조도 실패다.
                bool teamStanding = result.LostScavUids.Count < exp.ScavUids.Count;
                result.RescueSucceeded = teamStanding &&
                    RescueSystem.WouldSucceed(save, data, map, exp.ScavUids);

                if (result.RescueSucceeded) RescueSystem.BringHome(lost);
                else RescueSystem.LetGo(lost);   // 신호가 끊긴다. 기회는 한 번이다.
            }

            // ── 전리품 ──
            long lootValue = 0;
            var table = map != null ? data.GetLootTable(map.LootTableId) : null;
            if (table != null && table.Entries.Length > 0)
            {
                int bonus = (searchTotal / teamSize) / 4;
                // 장비는 회수 횟수의 바닥과 천장을 같이 올린다. 천장을 그대로 두면
                // 가방을 사도 상한에 걸려 아무 변화가 없다 — 산 사람은 그걸 버그로 읽는다.
                int rolls = table.RollsMin + bonus + gear.ExtraLootRolls;
                int cap = table.RollsMax + 2 + gear.ExtraLootRolls;
                if (rolls > cap) rolls = cap;
                if (result.HadCombat && result.HadAccident) rolls = rolls / 2;

                // 사건의 증감은 상한 바깥에 둔다. 잠긴 창고를 열었는데 천장에 걸려서
                // 아무 일도 안 일어나면, 연 사람 입장에서 그건 버그다.
                rolls += ExpeditionEvents.LootRollDelta(data, rolled);

                // 고용주가 아는 동네면 한 번 더 뒤진다 (GDD §4 — 차이는 숫자 보너스로만).
                rolls += Employers.ExtraLootRolls(save, data, exp.MapId);

                // 특성 (GDD §14). 용산 토박이는 용산에서 더 가져오고, 겁쟁이는 어디서나 덜 가져온다.
                //
                // 상한 <b>바깥</b>에서 곱한다. 사건 증감·고용주 보너스와 같은 취급이다 —
                // 상한 안에 두면 장비를 갖춘 사람에게는 특성이 아무것도 바꾸지 않게 되고,
                // 그러면 특성을 보고 사람을 고를 이유가 다시 사라진다.
                double traitScale = Scav.Traits.TeamLootScale(save, data, exp.ScavUids, exp.MapId);
                if (traitScale != 1.0) rolls = (int)System.Math.Round(rolls * traitScale);

                // Extra hands carry more, with diminishing returns; search expertise increases
                // recoverable value beyond the old small, capped search bonus. Search 4 solo
                // stays at the original baseline. Costs still grow per person, so a larger
                // haul is not automatically better profit per deployed scav.
                double carrying = System.Math.Sqrt(teamSize);
                double expertise = 1.0 + System.Math.Max(0, searchTotal / (double)teamSize - 4) * 0.20;
                rolls = (int)System.Math.Round(rolls * carrying * expertise);

                if (rolls < 0) rolls = 0;

                int totalWeight = 0;
                foreach (var e in table.Entries) totalWeight += e.Weight;

                // ── 운 ──
                // 좋은 무기를 들면 그 자리를 더 오래 지킬 수 있다 = 눈에 띄는 것 아무거나
                // 집어 나오지 않고 값나가는 쪽을 골라올 수 있다. 그래서 운은 "재추첨"이 아니라
                // <b>기대 단가 이상인 항목만 모아둔 표에서 뽑을 확률</b>이다.
                //
                // 재추첨으로 만들어봤더니 효과가 거의 없었다. 전리품 표의 값은 잭팟 하나와
                // 싼 것 뭉치로 갈라져 있어서(구로: 그래픽카드 350,000원 1% vs 나머지 9,000~62,000원),
                // 싼 것끼리 다시 뽑아봐야 그 뭉치 안에서 맴돈다. 플레이어가 "좋은 물건"이라고
                // 느끼는 건 잭팟이 나오는 빈도지 싸구려의 평균이 아니다.
                int goodWeight = GoodEntryWeight(table, data, totalWeight);

                for (int i = 0; i < rolls && totalWeight > 0; i++)
                {
                    bool lucky = goodWeight > 0 && gear.Luck > 0 && lootRng.Chance(gear.Luck);
                    var chosen = lucky
                        ? PickGoodEntry(table, data, ref lootRng, totalWeight, goodWeight)
                        : PickEntry(table, ref lootRng, totalWeight);
                    if (chosen == null) continue;

                    int count = lootRng.NextIntRange(chosen.CountMin, chosen.CountMax);
                    int overflow = Warehouse.TryAdd(save.Warehouse, data, chosen.ItemId, count);
                    int stored = count - overflow;

                    if (stored > 0)
                    {
                        result.Loot.Add(new ItemStack(chosen.ItemId, stored));
                        ctx.Report.AddGain(chosen.ItemId, stored);

                        var def = data.GetItem(chosen.ItemId);
                        long value = (def?.BasePrice ?? 0) * stored;
                        lootValue += value;
                        foreach (var uid in exp.ScavUids)
                        {
                            var s = FindScav(save, uid);
                            if (s != null) s.TotalLootValue += value / teamSize;
                        }
                    }
                    if (overflow > 0)
                        ctx.Report.Overflowed.Add(new ItemStack(chosen.ItemId, overflow));
                }
            }

            // 회수 가치만큼 경험치. 의뢰를 안 받아도 굴리기만 하면 조금씩 자란다 —
            // 레벨이 지역 해금을 막고 있어서, 성장 경로가 의뢰 하나뿐이면
            // 의뢰가 안 맞는 날은 아무것도 안 열린다.
            save.Player.Exp += Leveling.ExpFromLootValue(lootValue, data.Balance);

            // 경험치와 같은 값을 보고서에도 넘긴다. 여기서 다시 세지 않는 이유는,
            // 넘쳐서 버려진 몫이 빠진 "실제로 받은 값"이 이미 이 변수이기 때문이다.
            result.LootValue = lootValue;

            exp.Resolved = true;
            ctx.Report.Expeditions.Add(result);
        }

        /// <summary>가중치대로 항목 하나를 뽑는다.</summary>
        private static LootEntry PickEntry(LootTableDef table, ref Rng rng, int totalWeight)
        {
            int pick = rng.NextInt(totalWeight);
            foreach (var e in table.Entries)
            {
                pick -= e.Weight;
                if (pick < 0) return e;
            }
            return null;
        }

        /// <summary>항목 하나의 기대 가치 = 단가 × 평균 개수.</summary>
        private static double UnitValue(LootEntry entry, IDataRegistry data)
        {
            var def = data.GetItem(entry.ItemId);
            long price = def != null ? def.BasePrice : 0;
            return price * (entry.CountMin + entry.CountMax) / 2.0;
        }

        /// <summary>표에서 한 번 뽑았을 때의 기대 가치. 운의 "좋다" 기준선이다.</summary>
        private static double MeanEntryValue(LootTableDef table, IDataRegistry data, int totalWeight)
        {
            if (totalWeight <= 0) return 0;
            double sum = 0;
            foreach (var e in table.Entries) sum += e.Weight * UnitValue(e, data);
            return sum / totalWeight;
        }

        /// <summary>기대 단가 이상인 항목들의 가중치 합. 0 이면 운이 작동하지 않는다.</summary>
        private static int GoodEntryWeight(LootTableDef table, IDataRegistry data, int totalWeight)
        {
            double mean = MeanEntryValue(table, data, totalWeight);
            int sum = 0;
            foreach (var e in table.Entries)
                if (UnitValue(e, data) >= mean) sum += e.Weight;

            // 전부 같은 값인 표에서는 "좋은 쪽"이 표 전체가 된다. 그러면 운이 아무것도
            // 바꾸지 않으므로 꺼둔다 — 켜두면 계산만 하고 결과는 같아 혼란스럽다.
            return sum >= totalWeight ? 0 : sum;
        }

        /// <summary>기대 단가 이상인 항목 중에서만 가중치대로 뽑는다.</summary>
        private static LootEntry PickGoodEntry(
            LootTableDef table, IDataRegistry data, ref Rng rng, int totalWeight, int goodWeight)
        {
            double mean = MeanEntryValue(table, data, totalWeight);
            int pick = rng.NextInt(goodWeight);
            foreach (var e in table.Entries)
            {
                if (UnitValue(e, data) < mean) continue;
                pick -= e.Weight;
                if (pick < 0) return e;
            }
            return null;
        }

        /// <summary>사고에 휘말릴 확률. 생존이 높을수록 낮다. 아무리 높아도 0 은 아니다.</summary>
        private static double SpreadChance(int survival)
        {
            double c = 0.35 - survival * 0.025;
            if (c < 0.05) c = 0.05;
            return c;
        }

        /// <summary>
        /// 피해 정도. 기본 부상 60 / 실종 30 / 사망 10.
        ///
        /// <para>생존은 확률을 깎는 대신 <b>한 단계 가볍게</b> 만든다. 확률에서 빼면 생존이
        /// 높은 스캐브가 아예 안 죽어버려서 — GDD §15 가 사건이 되길 바라는 그 죽음이 —
        /// 영영 일어나지 않는다. 완화율에 상한을 둬서 면역은 만들지 않는다.</para>
        /// </summary>
        private static ScavStatus RollSeverity(
            ref Rng rng, int survival, double gearMitigation, EquipmentTuning tuning)
        {
            int roll = rng.NextInt(100);
            var status = roll < 60 ? ScavStatus.Injured
                       : roll < 90 ? ScavStatus.Missing
                                   : ScavStatus.Dead;

            if (status == ScavStatus.Injured) return status;

            double mitigate = survival * 0.05 + gearMitigation;
            double cap = tuning != null ? tuning.MitigationCap : 0.80;
            if (mitigate > cap) mitigate = cap;
            if (!rng.Chance(mitigate)) return status;

            return status == ScavStatus.Dead ? ScavStatus.Missing : ScavStatus.Injured;
        }

        private static ScavState FindScav(GameSave save, string uid)
        {
            foreach (var s in save.Scavs)
                if (s.Uid == uid) return s;
            return null;
        }
    }
}
