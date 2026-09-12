using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Scav
{
    /// <summary>
    /// 고용 시장. 매일 후보 몇 명이 걸리고, 돈을 내면 내 사람이 된다 (GDD §12~15).
    ///
    /// <para><b>왜 <see cref="ITimelineSystem"/> 인가:</b> 후보 갱신은 날짜 경계에 일어나는 사건이다.
    /// 일일 의뢰와 똑같은 문제를 갖는다 — 사흘 만에 접속하면 세 번 갱신돼야 하고, 그 사이에
    /// 파견이 복귀했다면 순서가 맞아야 한다. 접속 시점에 "날짜 바뀌었네" 하고 한 번 처리하면
    /// 그 순서가 뭉개진다 (ARCHITECTURE §3).</para>
    ///
    /// <para><b>후보는 세이브에 저장한다.</b> 시드에서 매번 다시 뽑을 수도 있지만, 고용하면
    /// 한 명이 사라지므로 어차피 상태가 생긴다. 화면을 다시 그릴 때마다 후보가 바뀌는 것보다
    /// 저장하는 쪽이 단순하다.</para>
    /// </summary>
    public sealed class ScavMarket : ITimelineSystem
    {
        public string Name => "ScavMarket";

        /// <summary>하루에 걸리는 후보 수.</summary>
        public int OfferCount { get; set; } = 3;

        public IEnumerable<TimedEvent> CollectEvents(ResolveWindow window, ResolveContext ctx)
        {
            var save = ctx.Save;

            if (string.IsNullOrEmpty(save.Market.ActiveGameDate))
            {
                yield return new TimedEvent(
                    window.From, EventOrder.DayRollover, "market:init",
                    c => Refresh(c, GameTime.GameDateOf(window.From)));
            }

            foreach (var boundary in GameTime.DayBoundariesBetween(window.From, window.To))
            {
                var date = GameTime.GameDateOf(boundary);
                yield return new TimedEvent(
                    boundary, EventOrder.DayRollover, "market:day:" + date.ToString(),
                    c => Refresh(c, date));
            }
        }

        private void Refresh(ResolveContext ctx, GameDate date)
        {
            var save = ctx.Save;
            string key = date.ToString();

            // 멱등성: 같은 날짜로 이미 갱신돼 있으면 아무것도 하지 않는다.
            if (save.Market.ActiveGameDate == key) return;

            // 날이 바뀌었으니 다시 굴린 횟수도 0 으로. 안 그러면 광고를 한 번 본 사람은
            // 그 뒤로 영원히 다른 시드를 쓰게 되어, 같은 날의 후보가 사람마다 달라진다.
            save.Market.RerollCount = 0;
            Roll(save, ctx.Data, date);
        }

        /// <summary>
        /// 후보를 다시 굴린다 (보상 광고 — GDD §11).
        ///
        /// <para><b>날짜 시드만으로는 다시 굴릴 수가 없다.</b> 같은 날은 몇 번을 돌려도 같은
        /// 후보가 나오도록 만들어 뒀기 때문이다(그게 리세마라를 막는 장치다). 그래서 굴린 횟수를
        /// 세이브에 남기고 시드에 섞는다 — 결정론은 그대로고, 결과만 달라진다.</para>
        /// </summary>
        public void Reroll(GameSave save, IDataRegistry data, System.DateTimeOffset now)
        {
            if (save == null || data == null) return;

            save.Market.RerollCount++;
            Roll(save, data, GameTime.GameDateOf(now));
        }

        /// <summary>
        /// 실제로 뽑는 부분. 멱등성 검사는 <see cref="Refresh"/> 쪽에 있다 —
        /// 다시 굴리기는 <b>일부러</b> 같은 날짜에 다시 뽑는 것이라 그 검사를 통과하면 안 된다.
        /// </summary>
        private void Roll(GameSave save, IDataRegistry data, GameDate date)
        {
            save.Market.Offers.Clear();
            save.Market.ActiveGameDate = date.ToString();

            var pool = data.ScavPool;
            if (pool == null || pool.Names.Length == 0 || pool.Tiers.Length == 0) return;

            // 날짜 + 플레이어 생성 시각으로 시드를 만든다. 일일 의뢰와 같은 방식이다 —
            // 같은 날은 몇 번 정산해도 같은 후보가 나오고, 플레이어마다 다른 후보가 나온다.
            uint seed = Rng.Derive(unchecked((uint)date.DayNumber),
                "market:" + save.Player.CreatedAt.ToUnixTimeSeconds() + ":" + save.Market.RerollCount);
            var rng = new Rng(seed);

            int maxTier = MaxTierFor(save);

            // 이미 고용한 사람과 이름이 겹치지 않게 한다. 김철수가 둘이면 누가 누군지 모른다.
            var usedNames = new HashSet<string>();
            foreach (var s in save.Scavs) usedNames.Add(s.Name);

            for (int i = 0; i < OfferCount; i++)
            {
                var offer = RollOffer(pool, ref rng, maxTier, usedNames, date, i, save.Market.RerollCount);
                if (offer == null) continue;
                usedNames.Add(offer.Name);
                save.Market.Offers.Add(offer);
            }
        }

        /// <summary>
        /// 뽑을 수 있는 최고 티어. 일일 의뢰와 같은 성장 경계를 쓴다 —
        /// 둘이 어긋나면 "의뢰는 어려워졌는데 사람은 그대로"가 된다.
        /// </summary>
        public static int MaxTierFor(GameSave save)
        {
            if (save.Player.Level >= 10) return 3;
            if (save.Player.Level >= 5) return 2;
            return 1;
        }

        private static ScavOffer RollOffer(
            ScavPoolDef pool, ref Rng rng, int maxTier,
            HashSet<string> usedNames, GameDate date, int index, int reroll)
        {
            // 티어는 낮은 쪽에 치우치게 뽑는다. 상위 티어가 매일 걸리면
            // 돈만 모으면 되는 게임이 되고, 아래 티어를 쓸 이유가 없어진다.
            int tier = 1;
            for (int t = 2; t <= maxTier; t++)
                if (rng.Chance(0.35)) tier = t;

            ScavTierDef tierDef = null;
            foreach (var td in pool.Tiers)
                if (td.Tier == tier) { tierDef = td; break; }
            if (tierDef == null) tierDef = pool.Tiers[0];

            string name = PickName(pool, ref rng, usedNames);
            if (name == null) return null;

            int total = rng.NextIntRange(tierDef.StatTotalMin, tierDef.StatTotalMax);
            int search, combat, survival;
            SplitStats(ref rng, total, out search, out combat, out survival);

            var traits = PickTraits(pool, ref rng, tierDef.TraitCount);

            var offer = new ScavOffer
            {
                // 날짜가 들어가므로 하루가 지나면 예전 후보 id 가 되살아나지 않는다.
                // 다시 굴린 판의 id 가 이전 판과 겹치면, 화면에 남아 있던 옛 버튼이
                // 새 후보를 고용해 버린다. 굴린 횟수를 id 에 넣어 그 일을 막는다.
                OfferId = "of_" + date + "_" + (reroll > 0 ? reroll + "_" : "") + index,
                Name = name,
                Tier = tierDef.Tier,
                Search = search,
                Combat = combat,
                Survival = survival,
                HireCost = tierDef.HireCost,
                WagePerHour = tierDef.WagePerHour,
                StarterWeapon = tierDef.StarterWeapon,
                Hired = false,
            };
            offer.TraitIds.AddRange(traits);
            return offer;
        }

        private static string PickName(ScavPoolDef pool, ref Rng rng, HashSet<string> used)
        {
            // 안 겹치는 이름을 몇 번 시도하고, 풀이 다 찼으면 포기한다.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                string candidate = pool.Names[rng.NextInt(pool.Names.Length)];
                if (!used.Contains(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>
        /// 능력치 총합을 셋으로 나눈다.
        ///
        /// <para>균등하게 나누면 스캐브가 전부 비슷해져서 "누구를 보낼까"가 사라진다.
        /// 한 능력치를 특기로 정해 약 45%를 몰아주고 나머지를 갈라, 탐색형·전투형·생존형이
        /// 눈에 띄게 갈리도록 한다 (GDD §15 — 애착은 구별에서 시작한다).</para>
        ///
        /// <para><b>대신 바닥을 둔다.</b> 예전에는 비특기가 1 까지 떨어질 수 있었는데,
        /// 탐색 1 짜리 전투형은 회수량이 거의 0 이라 25만원을 주고 산 사람이 첫 파견에서
        /// 빈손으로 돌아온다. 그건 "개성"이 아니라 함정이다. 각 능력치에 총합의 약 18%
        /// (최소 2)를 먼저 깔고, 남은 몫만 특기 쪽으로 몰아준다.</para>
        /// </summary>
        private static void SplitStats(ref Rng rng, int total, out int search, out int combat, out int survival)
        {
            if (total < 6) total = 6;   // 바닥 2×3 이 성립하는 최소치

            // 어떤 능력치도 총합의 1/6 밑으로 내려가지 않는다. 올림이라 총합이 얼마든 비율이 지켜진다.
            int floor = (total + 5) / 6;
            if (floor < 2) floor = 2;
            if (floor * 3 > total) floor = total / 3;

            int specialty = rng.NextInt(3);

            int specValue = (int)(total * 0.45) + rng.NextInt(2);
            int maxSpec = total - floor * 2;          // 나머지 둘에게 바닥은 남겨둔다
            if (specValue > maxSpec) specValue = maxSpec;
            if (specValue < floor) specValue = floor;

            // 바닥을 뺀 나머지를 둘이 나눈다. 양쪽을 특기 이하로 조인다 —
            // 비특기가 특기보다 높으면 화면의 "특기" 표시와 어긋난다.
            int slack = total - specValue - floor * 2;
            int cap = specValue - floor;
            int lo = slack - cap; if (lo < 0) lo = 0;
            int hi = cap < slack ? cap : slack;
            int a = lo + rng.NextInt(hi - lo + 1);
            int b = slack - a;

            switch (specialty)
            {
                case 0: search = specValue; combat = floor + a; survival = floor + b; break;
                case 1: combat = specValue; search = floor + a; survival = floor + b; break;
                default: survival = specValue; search = floor + a; combat = floor + b; break;
            }
        }

        private static List<string> PickTraits(ScavPoolDef pool, ref Rng rng, int count)
        {
            var picked = new List<string>();
            if (pool.Traits.Length == 0 || count <= 0) return picked;

            var remaining = new List<ScavTraitDef>(pool.Traits);
            int take = count < remaining.Count ? count : remaining.Count;
            for (int i = 0; i < take; i++)
            {
                int idx = rng.NextInt(remaining.Count);
                picked.Add(remaining[idx].Id);
                remaining.RemoveAt(idx);
            }
            return picked;
        }

        // ── 고용 (플레이어 조작, 정산 아님) ─────────────────────────

        /// <summary>
        /// 고용한다. 돈이 모자라거나 이미 고용한 후보면 아무것도 하지 않고 null.
        /// 차감과 채용은 같이 일어난다 — 돈만 빠지고 사람이 안 오는 상태를 만들지 않는다.
        /// </summary>
        public ScavState TryHire(GameSave save, string offerId, System.DateTimeOffset now)
        {
            ScavOffer offer = null;
            foreach (var o in save.Market.Offers)
                if (o.OfferId == offerId) { offer = o; break; }

            if (offer == null || offer.Hired) return null;
            if (save.Player.Money < offer.HireCost) return null;

            save.Player.Money -= offer.HireCost;

            var scav = new ScavState
            {
                Uid = "sc_" + save.RngCounter.ToString("x8"),
                Name = offer.Name,
                Level = 1,
                Tier = offer.Tier,
                Search = offer.Search,
                Combat = offer.Combat,
                Survival = offer.Survival,
                WagePerHour = offer.WagePerHour,
                Status = ScavStatus.Idle,
                HiredAt = now,
            };
            scav.TraitIds.AddRange(offer.TraitIds);

            // 자기 무기를 들고 온다. 창고를 거치지 않는다 — 창고에 넣었다가 다시 꺼내면
            // 창고가 꽉 찼을 때 무기 없는 스캐브가 생기고, 그러면 고용이 헛돈이 된다.
            if (!string.IsNullOrEmpty(offer.StarterWeapon))
                scav.Equipment[EquipSlot.Weapon] = offer.StarterWeapon;

            // Uid 를 RngCounter 로 만들었으니 하나 소비해 다음 사람과 겹치지 않게 한다.
            save.TakeSeed();

            save.Scavs.Add(scav);
            offer.Hired = true;
            return scav;
        }

        /// <summary>고용할 수 없는 이유. 가능하면 null. 버튼이 조용히 죽지 않게 한다.</summary>
        public static string HireBlockReason(GameSave save, ScavOffer offer)
        {
            if (offer == null) return "후보를 찾을 수 없습니다";
            if (offer.Hired) return "이미 고용했습니다";
            if (save.Player.Money < offer.HireCost)
                return $"자금 부족 — {offer.HireCost - save.Player.Money:N0}원 더 필요합니다";
            return null;
        }
    }
}
