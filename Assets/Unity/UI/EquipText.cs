using AfterSeoul.Core;
using AfterSeoul.Scav;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 장비 한 점을 플레이어의 말로 옮긴다.
    ///
    /// <para>화면에 <c>armorClass 4</c> 를 그대로 띄우면 그게 좋은 건지 나쁜 건지 알 수가 없다.
    /// 슬롯마다 "이 물건이 파견에서 뭘 해주는가"를 한 줄로 적는다 — 숫자가 아니라 효과다.</para>
    /// </summary>
    public static class EquipText
    {
        /// <summary>고를 때 보이는 한 줄. 예: "방어 4 · 사고 확률 감소".</summary>
        public static string Summary(ItemDef def, IDataRegistry data)
        {
            if (def == null || !def.Equippable) return "";
            var t = data.Balance.Equipment ?? new EquipmentTuning();

            switch (def.EquipSlot)
            {
                case EquipSlot.Weapon:
                    return $"등급 {def.WeaponGrade} · 좋은 물건 확률 +{def.WeaponGrade * t.WeaponLuckPerGrade:P0}";

                case EquipSlot.Headwear:
                    return $"방어 {def.ArmorClass} · 치명상 완화 +{def.ArmorClass * t.HelmetMitigationPerClass:P0}";

                case EquipSlot.BodyArmor:
                    return $"방어 {def.ArmorClass} · 생존 +{def.ArmorClass}";

                case EquipSlot.Earpiece:
                {
                    int step = HearingStep(def.HearingRange);
                    return $"청취 {def.HearingRange} · 좋은 물건 확률 +{step * t.HeadsetLuckPerStep:P0} · 생존 +{step}";
                }

                case EquipSlot.TacticalRig:
                    return $"{def.GridSlots}칸 · 회수 +{(def.GridSlots >= t.RigLargeSlots ? 2 : 1)}회";

                case EquipSlot.Backpack:
                {
                    int r = def.GridSlots >= t.BackpackLargeSlots ? 3
                          : def.GridSlots >= t.BackpackMediumSlots ? 2 : 1;
                    return $"{def.GridSlots}칸 · 회수 +{r}회";
                }

                default:
                    return "";
            }
        }

        /// <summary>스캐브 카드에 한 줄로 요약. 예: "장비 4/6   좋은 물건 +30%   회수 +4회".</summary>
        public static string LoadoutSummary(ScavState scav, IDataRegistry data)
        {
            var e = Equipment.EffectsOf(scav, data);

            int worn = 0;
            foreach (var slot in EquipSlot.All)
            {
                string id;
                if (scav.Equipment.TryGetValue(slot, out id) && !string.IsNullOrEmpty(id)) worn++;
            }

            if (worn == 0) return "장비 없음 — 무기가 없으면 파견할 수 없습니다";

            string s = $"장비 {worn}/{EquipSlot.All.Length}";
            if (e.Luck > 0) s += $"   좋은 물건 +{e.Luck:P0}";
            if (e.ExtraLootRolls > 0) s += $"   회수 +{e.ExtraLootRolls}회";
            if (e.SurvivalBonus > 0) s += $"   생존 +{e.SurvivalBonus}";
            if (e.SeverityMitigation > 0) s += $"   치명상 완화 +{e.SeverityMitigation:P0}";
            if (!e.HasWeapon) s += "   · 무기 없음";
            return s;
        }

        private static int HearingStep(int hearingRange)
        {
            if (hearingRange <= 0) return 1;
            int step = (hearingRange - 12) / 3 + 1;
            if (step < 1) step = 1;
            if (step > 3) step = 3;
            return step;
        }
    }
}
