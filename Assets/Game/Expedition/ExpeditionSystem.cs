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
        /// 자금이 모자라면 아무것도 하지 않고 null.
        /// </summary>
        public ExpeditionState Depart(
            GameSave save, IDataRegistry data,
            string mapId, IReadOnlyList<string> scavUids,
            System.DateTimeOffset now)
        {
            var map = data.GetMap(mapId);
            if (map == null || scavUids == null || scavUids.Count == 0) return null;

            long cost = map.BaseCostWage + map.BaseCostSupply;
            if (save.Player.Money < cost) return null;

            foreach (var uid in scavUids)
            {
                var scav = FindScav(save, uid);
                if (scav == null || scav.Status != ScavStatus.Idle) return null;
            }

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
            };
            exp.ScavUids.AddRange(scavUids);

            foreach (var uid in scavUids)
            {
                var scav = FindScav(save, uid);
                scav.Status = ScavStatus.OnExpedition;
                scav.ExpeditionCount++;
            }

            save.Expeditions.Add(exp);
            return exp;
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

            var save = ctx.Save;
            var data = ctx.Data;
            var map = data.GetMap(exp.MapId);

            var result = new ExpeditionResult
            {
                ExpeditionUid = exp.Uid,
                MapId = exp.MapId,
                ReturnedAt = exp.ReturnsAt,
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
                foreach (var uid in exp.ScavUids)
                {
                    var s = FindScav(save, uid);
                    if (s == null) continue;

                    int roll = accidentRng.NextInt(100);
                    if (roll < 60) { s.Status = ScavStatus.Injured; result.InjuredScavUids.Add(uid); }
                    else if (roll < 90) { s.Status = ScavStatus.Missing; result.LostScavUids.Add(uid); }
                    else { s.Status = ScavStatus.Dead; result.LostScavUids.Add(uid); }

                    if (s.Status != ScavStatus.Injured) ctx.Report.ScavCasualties.Add(uid);
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

            // ── 전리품 ──
            var table = map != null ? data.GetLootTable(map.LootTableId) : null;
            if (table != null && table.Entries.Length > 0)
            {
                int bonus = (searchTotal / teamSize) / 4;
                int rolls = table.RollsMin + bonus;
                int cap = table.RollsMax + 2;
                if (rolls > cap) rolls = cap;
                if (result.HadCombat && result.HadAccident) rolls = rolls / 2;

                int totalWeight = 0;
                foreach (var e in table.Entries) totalWeight += e.Weight;

                for (int i = 0; i < rolls && totalWeight > 0; i++)
                {
                    int pick = lootRng.NextInt(totalWeight);
                    LootEntry chosen = null;
                    foreach (var e in table.Entries)
                    {
                        pick -= e.Weight;
                        if (pick < 0) { chosen = e; break; }
                    }
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

            exp.Resolved = true;
            ctx.Report.Expeditions.Add(result);
        }

        private static ScavState FindScav(GameSave save, string uid)
        {
            foreach (var s in save.Scavs)
                if (s.Uid == uid) return s;
            return null;
        }
    }
}
