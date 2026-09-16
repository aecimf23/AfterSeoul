using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Scav
{
    /// <summary>AFTER SEOUL 의 장비 칸 6종 (GDD §7). 값은 <c>items.json</c> 의 <c>equipSlot</c> 과 같다.</summary>
    public static class EquipSlot
    {
        /// <summary>무기. 본편의 주무기·보조무기·근접무기가 여기로 합쳐진다.</summary>
        public const string Weapon = "Weapon";

        public const string Headwear = "Headwear";
        public const string BodyArmor = "BodyArmor";
        public const string Earpiece = "Earpiece";
        public const string TacticalRig = "TacticalRig";
        public const string Backpack = "Backpack";

        /// <summary>화면에 보일 순서. 무기가 먼저다 — 없으면 파견 자체가 안 된다.</summary>
        public static readonly string[] All =
        {
            Weapon, Headwear, BodyArmor, Earpiece, TacticalRig, Backpack,
        };

        public static string LabelOf(string slot)
        {
            switch (slot)
            {
                case Weapon: return Loc.Text("무기");
                case Headwear: return Loc.Text("헬멧");
                case BodyArmor: return Loc.Text("방탄복");
                case Earpiece: return Loc.Text("헤드셋");
                case TacticalRig: return Loc.Text("리그");
                case Backpack: return Loc.Text("가방");
                default: return slot;
            }
        }

        public static bool IsValid(string slot)
        {
            foreach (var s in All) if (s == slot) return true;
            return false;
        }
    }

    /// <summary>
    /// 스캐브 한 명의 장비가 파견에 주는 효과.
    ///
    /// <para>정산이 읽는 값은 이 네 개뿐이다. 슬롯을 늘리든 아이템을 늘리든
    /// <see cref="Equipment.EffectsOf"/> 안에서 끝나고 <c>ExpeditionSystem</c> 은 안 바뀐다.</para>
    /// </summary>
    public struct LoadoutEffects
    {
        /// <summary>운. 싼 물건을 집었을 때 한 번 더 뒤져볼 확률 (0~1).</summary>
        public double Luck;

        /// <summary>사고 확률 계산의 생존에 더해진다.</summary>
        public int SurvivalBonus;

        /// <summary>피해 강등 확률 가산 — 사망→실종→부상.</summary>
        public double SeverityMitigation;

        /// <summary>회수 횟수 가산.</summary>
        public int ExtraLootRolls;

        /// <summary>무기를 들었는가. 없으면 파견 자체가 막힌다.</summary>
        public bool HasWeapon;
    }

    /// <summary>
    /// 장비 지급과 효과 계산 (GDD §7).
    ///
    /// <para><b>장비는 창고와 스캐브 사이를 오간다.</b> 지급하면 창고에서 빠지고,
    /// 해제하면 창고로 돌아온다. 사본을 만들지 않는 이유는 그게 곧 복제 버그이기 때문이다 —
    /// 같은 방탄복을 세 명에게 입힐 수 있으면 장비를 살 이유가 사라진다.</para>
    ///
    /// <para>효과 수치는 <c>balance.json</c> 의 <c>equipment</c> 에 있다. 여기서는 모양만 정한다.</para>
    /// </summary>
    public static class Equipment
    {
        // ── 지급 / 해제 ──────────────────────────────────────────

        /// <summary>
        /// 창고의 아이템 하나를 스캐브에게 지급한다. 이미 그 칸에 뭔가 있으면 창고로 되돌린다.
        /// 실패하면 아무것도 바꾸지 않고 false — 사유는 <see cref="EquipBlockReason"/>.
        /// </summary>
        public static bool TryEquip(GameSave save, IDataRegistry data, string scavUid, string itemId)
        {
            var scav = FindScav(save, scavUid);
            if (EquipBlockReason(save, data, scav, itemId) != null) return false;

            var def = data.GetItem(itemId);
            string slot = def.EquipSlot;

            // 창고에서 먼저 뺀다. 여기서 실패하면 아무 일도 일어나지 않아야 한다.
            if (!Warehouse.TryRemove(save.Warehouse, itemId, 1)) return false;

            // 기존 장비를 창고로. 넘쳐서 못 들어가면 지급을 되돌린다 —
            // 창고가 꽉 찼다고 쓰던 장비를 증발시키면 그건 버그가 아니라 손실이다.
            string previous;
            if (scav.Equipment.TryGetValue(slot, out previous) && !string.IsNullOrEmpty(previous))
            {
                if (Warehouse.TryAdd(save.Warehouse, data, previous, 1) > 0)
                {
                    Warehouse.TryAdd(save.Warehouse, data, itemId, 1);
                    return false;
                }
            }

            scav.Equipment[slot] = itemId;
            return true;
        }

        /// <summary>장비를 벗겨 창고로 되돌린다. 창고가 꽉 찼으면 벗기지 않는다.</summary>
        public static bool TryUnequip(GameSave save, IDataRegistry data, string scavUid, string slot)
        {
            var scav = FindScav(save, scavUid);
            if (scav == null || !EquipSlot.IsValid(slot)) return false;
            if (scav.Status == ScavStatus.OnExpedition) return false;

            string itemId;
            if (!scav.Equipment.TryGetValue(slot, out itemId) || string.IsNullOrEmpty(itemId)) return false;

            if (Warehouse.TryAdd(save.Warehouse, data, itemId, 1) > 0) return false;

            scav.Equipment.Remove(slot);
            return true;
        }

        /// <summary>지급할 수 없는 이유. 가능하면 null.</summary>
        public static string EquipBlockReason(GameSave save, IDataRegistry data, ScavState scav, string itemId)
        {
            if (scav == null) return Loc.Text("스캐브를 찾을 수 없습니다");
            if (scav.Status == ScavStatus.OnExpedition) return Loc.Text("파견 중에는 장비를 바꿀 수 없습니다");
            if (scav.Status == ScavStatus.Dead || scav.Status == ScavStatus.Missing)
                return Loc.Text("돌아오지 않은 사람입니다");

            var def = data.GetItem(itemId);
            if (def == null) return Loc.Text("아이템 정보를 찾을 수 없습니다");
            if (!def.Equippable || !EquipSlot.IsValid(def.EquipSlot)) return Loc.Text("장비로 쓸 수 없는 물건입니다");
            if (Warehouse.CountOf(save.Warehouse, itemId) < 1) return Loc.Text("창고에 없습니다");

            return null;
        }

        /// <summary>
        /// 실종·사망한 스캐브의 장비를 잃는다 (GDD §7 — 위험과 보상의 저울).
        /// 창고로 돌아오지 않는다. 그게 좋은 장비를 들려 보낼 때의 무게다.
        /// </summary>
        public static List<string> StripLostGear(ScavState scav)
        {
            var lost = new List<string>();
            if (scav == null || scav.Equipment.Count == 0) return lost;

            // Dictionary 를 직접 순회하지 않는다. 열거 순서가 보장되지 않으면
            // 복귀 보고의 장비 목록 순서가 실행마다 달라져서, 같은 시드가 같은 보고를
            // 낸다는 약속이 깨진다. 슬롯 순서대로 훑는다.
            foreach (var slot in EquipSlot.All)
            {
                string itemId;
                if (scav.Equipment.TryGetValue(slot, out itemId) && !string.IsNullOrEmpty(itemId))
                    lost.Add(itemId);
            }

            scav.Equipment.Clear();
            return lost;
        }

        /// <summary>
        /// 같은 칸 안에서 이 장비가 얼마나 좋은가. 목록을 값이 아니라 <b>효과</b>로 줄 세우기 위한 것이다.
        ///
        /// <para>값으로 정렬하면 거짓말이 된다. 이 게임에서 제일 비싼 무기는 950,000원짜리 단검인데
        /// 근접무기는 전부 최하 등급이라 260,000원짜리 소총보다 한참 못하다 (본편 가격을 그대로
        /// 가져왔고, 그건 그대로 두기로 했다). 값싼 것부터 늘어놓는 건 좋지만, 그 줄의 끝이
        /// 제일 좋은 물건이 아니면 플레이어는 돈을 버리게 된다.</para>
        ///
        /// <para>칸이 다르면 비교할 수 없다 (방어 6 과 64칸 가방 중 뭐가 큰가). 칸 안에서만 쓴다.</para>
        /// </summary>
        public static int EffectRank(ItemDef def)
        {
            if (def == null || !def.Equippable) return 0;

            switch (def.EquipSlot)
            {
                case EquipSlot.Weapon: return def.WeaponGrade;
                case EquipSlot.Headwear:
                case EquipSlot.BodyArmor: return def.ArmorClass;
                case EquipSlot.Earpiece: return def.HearingRange;
                case EquipSlot.TacticalRig:
                case EquipSlot.Backpack: return def.GridSlots;
                default: return 0;
            }
        }

        /// <summary>슬롯의 표준 순서상 위치. 칸이 섞인 목록을 정렬할 때 쓴다.</summary>
        public static int SlotOrder(string slot)
        {
            for (int i = 0; i < EquipSlot.All.Length; i++)
                if (EquipSlot.All[i] == slot) return i;
            return EquipSlot.All.Length;
        }

        // ── 효과 계산 ────────────────────────────────────────────

        /// <summary>스캐브 한 명의 장비 효과.</summary>
        public static LoadoutEffects EffectsOf(ScavState scav, IDataRegistry data)
        {
            var e = new LoadoutEffects();
            if (scav == null) return e;

            var t = data.Balance.Equipment ?? new EquipmentTuning();

            foreach (var slot in EquipSlot.All)
            {
                string itemId;
                if (!scav.Equipment.TryGetValue(slot, out itemId) || string.IsNullOrEmpty(itemId)) continue;

                var def = data.GetItem(itemId);
                if (def == null || !def.Equippable) continue;

                switch (slot)
                {
                    case EquipSlot.Weapon:
                        e.HasWeapon = true;
                        e.Luck += def.WeaponGrade * t.WeaponLuckPerGrade;
                        break;

                    case EquipSlot.Earpiece:
                        // 청취 반경 12/15/18 → 1/2/3 단계.
                        int step = HearingStep(def.HearingRange);
                        e.Luck += step * t.HeadsetLuckPerStep;
                        e.SurvivalBonus += step;
                        break;

                    case EquipSlot.Headwear:
                        e.SeverityMitigation += def.ArmorClass * t.HelmetMitigationPerClass;
                        break;

                    case EquipSlot.BodyArmor:
                        e.SurvivalBonus += def.ArmorClass;
                        break;

                    case EquipSlot.TacticalRig:
                        e.ExtraLootRolls += def.GridSlots >= t.RigLargeSlots ? 2 : 1;
                        break;

                    case EquipSlot.Backpack:
                        e.ExtraLootRolls += BackpackRolls(def.GridSlots, t);
                        break;
                }
            }

            if (e.Luck > t.LuckCap) e.Luck = t.LuckCap;
            return e;
        }

        /// <summary>
        /// 팀 전체의 효과.
        ///
        /// <para><b>방어는 사람마다, 회수는 팀 평균이다.</b> 내 헬멧이 나를 지키는 건 개인의 일이고
        /// (그래서 <see cref="LoadoutEffects.SeverityMitigation"/> 은 여기서 합치지 않는다),
        /// 물건을 얼마나 가져오는가는 팀이 같이 한 일이다. 운과 회수를 합으로 두면 한 명에게만
        /// 좋은 무기를 쥐여주고 나머지를 맨몸으로 보내는 게 최적이 된다.</para>
        ///
        /// <para>생존만 합이다 — 정산의 사고 확률이 이미 팀 생존 <i>합</i>으로 계산된다.</para>
        /// </summary>
        public static LoadoutEffects TeamEffectsOf(
            GameSave save, IDataRegistry data, IReadOnlyList<string> scavUids)
        {
            var team = new LoadoutEffects { HasWeapon = true };
            if (scavUids == null || scavUids.Count == 0) return new LoadoutEffects();

            double luckSum = 0;
            int rollSum = 0, counted = 0;

            foreach (var uid in scavUids)
            {
                var scav = FindScav(save, uid);
                if (scav == null) continue;

                var e = EffectsOf(scav, data);
                luckSum += e.Luck;
                rollSum += e.ExtraLootRolls;
                team.SurvivalBonus += e.SurvivalBonus;
                if (!e.HasWeapon) team.HasWeapon = false;
                counted++;
            }

            if (counted == 0) return new LoadoutEffects();

            team.Luck = luckSum / counted;
            team.ExtraLootRolls = rollSum / counted;   // 내림
            return team;
        }

        /// <summary>무기를 안 든 스캐브의 이름. 없으면 빈 목록.</summary>
        public static List<string> UnarmedNames(
            GameSave save, IDataRegistry data, IReadOnlyList<string> scavUids)
        {
            var names = new List<string>();
            if (scavUids == null) return names;

            foreach (var uid in scavUids)
            {
                var scav = FindScav(save, uid);
                if (scav == null) continue;
                if (!EffectsOf(scav, data).HasWeapon) names.Add(Loc.Text(scav.Name));
            }
            return names;
        }

        // ── 내부 ─────────────────────────────────────────────────

        private static int HearingStep(int hearingRange)
        {
            if (hearingRange <= 0) return 1;
            int step = (hearingRange - 12) / 3 + 1;
            if (step < 1) step = 1;
            if (step > 3) step = 3;
            return step;
        }

        private static int BackpackRolls(int gridSlots, EquipmentTuning t)
        {
            if (gridSlots >= t.BackpackLargeSlots) return 3;
            if (gridSlots >= t.BackpackMediumSlots) return 2;
            return 1;
        }

        private static ScavState FindScav(GameSave save, string uid)
        {
            if (save == null || string.IsNullOrEmpty(uid)) return null;
            foreach (var s in save.Scavs) if (s.Uid == uid) return s;
            return null;
        }
    }
}
