using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Quest
{
    /// <summary>
    /// 일일 의뢰 갱신과 납품.
    ///
    /// 날짜 경계를 <see cref="TimedEvent"/> 로 내보내는 것이 이 설계의 요점이다.
    /// "접속했더니 날짜가 바뀌었네" 하고 한 번에 처리하지 않고, 경계 시각 자체를
    /// 타임라인에 올린다. 그래야 사흘 만에 접속했을 때 파견 복귀와 날짜 갱신이
    /// 실제로 일어난 순서대로 섞인다.
    /// </summary>
    public sealed class DailyQuestSystem : ITimelineSystem
    {
        public string Name => "DailyQuest";

        /// <summary>한 번에 받는 의뢰 수.</summary>
        public int DailyQuestCount { get; set; } = 3;

        public IEnumerable<TimedEvent> CollectEvents(ResolveWindow window, ResolveContext ctx)
        {
            var save = ctx.Save;

            // 세이브에 날짜가 없다 = 첫 실행. 즉시 한 번 발급한다.
            if (string.IsNullOrEmpty(save.Quests.ActiveGameDate))
            {
                yield return new TimedEvent(
                    window.From, EventOrder.DayRollover, "quest:init",
                    c => Rollover(c, GameTime.GameDateOf(window.From)));
            }

            foreach (var boundary in GameTime.DayBoundariesBetween(window.From, window.To))
            {
                var date = GameTime.GameDateOf(boundary);
                yield return new TimedEvent(
                    boundary, EventOrder.DayRollover, "quest:day:" + date.ToString(),
                    c => Rollover(c, date));
            }
        }

        private void Rollover(ResolveContext ctx, GameDate date)
        {
            var save = ctx.Save;
            string key = date.ToString();

            // 멱등성: 이미 그 날짜로 갱신돼 있으면 아무것도 하지 않는다.
            if (save.Quests.ActiveGameDate == key) return;

            // 미납 의뢰는 그냥 사라진다. 벌칙 없음 — 하루 못 들어온 것에
            // 페널티를 주면 "빠지면 손해"가 되어 부담스러운 게임이 된다.
            save.Quests.Active.Clear();
            save.Quests.ActiveGameDate = key;

            var pool = PoolFor(ctx, save.Player.EmployerNpcId);
            if (pool != null && pool.Count > 0)
            {
                // 날짜와 플레이어 생성 시각으로 시드를 만든다.
                // 같은 날은 몇 번을 정산해도 같은 의뢰가 나오고,
                // 플레이어마다 다른 의뢰가 나온다.
                uint seed = Rng.Derive(unchecked((uint)date.DayNumber), "quest:" + save.Player.CreatedAt.ToUnixTimeSeconds());
                var rng = new Rng(seed);

                var candidates = FilterByTier(pool, TierFor(save));

                // 티어를 통과해도 실제로는 못 깨는 의뢰가 있다 — 요구 품목이 아직 잠긴
                // 지역에서만 나오는 경우다. 그런 의뢰는 하루 종일 붙어 있는 빈 칸이 된다.
                var achievable = FilterAchievable(candidates, save, ctx.Data);
                if (achievable.Count > 0) candidates = achievable;

                if (candidates.Count == 0) candidates = new List<QuestDef>(pool);

                int take = DailyQuestCount < candidates.Count ? DailyQuestCount : candidates.Count;
                var picked = new List<QuestDef>();
                var remaining = new List<QuestDef>(candidates);
                for (int i = 0; i < take; i++)
                {
                    int idx = rng.NextInt(remaining.Count);
                    picked.Add(remaining[idx]);
                    remaining.RemoveAt(idx);
                }

                foreach (var q in picked)
                    save.Quests.Active.Add(new ActiveQuest { QuestId = q.Id, Delivered = false });
            }

            ctx.Report.DayRollovers++;
        }

        /// <summary>
        /// 뽑을 수 있는 최고 의뢰 티어. <b>레벨과 신뢰도 중 높은 쪽을 쓴다.</b>
        ///
        /// <para>둘 다 보는 이유: 레벨은 회수량(파견을 얼마나 굴렸나)에서 오고 신뢰도는
        /// 납품(의뢰를 얼마나 했나)에서 온다. 레벨만 보면 의뢰만 열심히 한 플레이어의
        /// 의뢰가 영원히 티어 1 이고, 신뢰도만 보면 그 반대가 된다.</para>
        ///
        /// <para>티어가 열려도 못 깨는 의뢰는 <see cref="QuestReach"/> 가 따로 거른다.
        /// 여기서는 난이도만 정한다.</para>
        /// </summary>
        private static int TierFor(GameSave save)
        {
            int byLevel = save.Player.Level >= 10 ? 3
                        : save.Player.Level >= 5 ? 2 : 1;

            int trust = 0;
            if (!string.IsNullOrEmpty(save.Player.EmployerNpcId) && save.NpcTrust != null)
                save.NpcTrust.TryGetValue(save.Player.EmployerNpcId, out trust);

            int byTrust = trust >= 30 ? 3
                        : trust >= 12 ? 2 : 1;

            return byLevel > byTrust ? byLevel : byTrust;
        }

        private static List<QuestDef> FilterByTier(IReadOnlyList<QuestDef> pool, int maxTier)
        {
            var list = new List<QuestDef>();
            foreach (var q in pool)
                if (q.Tier <= maxTier) list.Add(q);
            return list;
        }

        private static List<QuestDef> FilterAchievable(
            IReadOnlyList<QuestDef> candidates, GameSave save, IDataRegistry data)
        {
            var list = new List<QuestDef>();
            foreach (var q in candidates)
                if (QuestReach.IsAchievable(save, data, q)) list.Add(q);
            return list;
        }

        private static IReadOnlyList<QuestDef> PoolFor(ResolveContext ctx, string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            return ctx.Data.GetQuestPool(Employers.QuestPoolId(ctx.Data, npcId));
        }

        // ── 납품 (플레이어 조작, 정산 아님) ────────────────────────

        /// <summary>
        /// 의뢰를 납품한다. 요구 아이템을 차감하고 보상을 준다.
        /// 조건을 못 채우면 아무것도 하지 않고 false — 부분 차감은 없다.
        /// </summary>
        public bool TryDeliver(GameSave save, IDataRegistry data, string questId)
        {
            ActiveQuest active = null;
            foreach (var a in save.Quests.Active)
                if (a.QuestId == questId) { active = a; break; }

            if (active == null || active.Delivered) return false;

            var def = FindDef(data, save, questId);
            if (def == null) return false;
            if (!MeetsRequirements(save, data, def)) return false;

            // 차감. MeetsRequirements 를 통과했으므로 전부 성공한다.
            foreach (var req in def.Requires)
            {
                if (!string.IsNullOrEmpty(req.ItemId))
                {
                    Warehouse.TryRemove(save.Warehouse, req.ItemId, req.Count);
                }
                else if (!string.IsNullOrEmpty(req.Tag))
                {
                    RemoveByTag(save, data, req.Tag, req.Count);
                }
            }

            save.Player.Money += def.RewardMoney;
            save.Player.Exp += def.RewardExp;
            if (!string.IsNullOrEmpty(save.Player.EmployerNpcId))
            {
                save.NpcTrust.TryGetValue(save.Player.EmployerNpcId, out int trust);
                save.NpcTrust[save.Player.EmployerNpcId] = trust + def.RewardTrust;
            }

            active.Delivered = true;
            save.Quests.CompletedIds.Add(questId);
            return true;
        }

        public static bool MeetsRequirements(GameSave save, IDataRegistry data, QuestDef def)
        {
            foreach (var req in def.Requires)
            {
                if (!string.IsNullOrEmpty(req.ItemId))
                {
                    if (Warehouse.CountOf(save.Warehouse, req.ItemId) < req.Count) return false;
                }
                else if (!string.IsNullOrEmpty(req.Tag))
                {
                    if (Warehouse.CountByTag(save.Warehouse, data, req.Tag) < req.Count) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 태그 조건 차감. <b>싼 것부터</b> 가져간다.
        /// 비싼 것부터 가져가면 플레이어가 모르는 사이에 귀중품이 사라진다.
        /// </summary>
        private static void RemoveByTag(GameSave save, IDataRegistry data, string tag, int count)
        {
            var matching = new List<ItemStack>();
            foreach (var s in save.Warehouse.Stacks)
            {
                var def = data.GetItem(s.ItemId);
                if (def == null) continue;
                foreach (var t in def.Tags)
                    if (t == tag) { matching.Add(s); break; }
            }

            matching.Sort((a, b) =>
            {
                long pa = data.GetItem(a.ItemId)?.BasePrice ?? 0;
                long pb = data.GetItem(b.ItemId)?.BasePrice ?? 0;
                int c = pa.CompareTo(pb);
                return c != 0 ? c : string.CompareOrdinal(a.ItemId, b.ItemId);
            });

            int remaining = count;
            foreach (var s in matching)
            {
                if (remaining <= 0) break;
                int take = s.Count < remaining ? s.Count : remaining;
                Warehouse.TryRemove(save.Warehouse, s.ItemId, take);
                remaining -= take;
            }
        }

        private static QuestDef FindDef(IDataRegistry data, GameSave save, string questId)
        {
            var pool = data.GetQuestPool(Employers.QuestPoolId(data, save.Player.EmployerNpcId));
            if (pool == null) return null;
            foreach (var q in pool)
                if (q.Id == questId) return q;
            return null;
        }
    }
}
