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
                    return AfterSeoul.Core.Loc.Text("등급 {0} · 좋은 물건 확률 +{1:P0}", def.WeaponGrade, def.WeaponGrade * t.WeaponLuckPerGrade);

                case EquipSlot.Headwear:
                    return AfterSeoul.Core.Loc.Text("방어 {0} · 치명상 완화 +{1:P0}", def.ArmorClass, def.ArmorClass * t.HelmetMitigationPerClass);

                case EquipSlot.BodyArmor:
                    return AfterSeoul.Core.Loc.Text("방어 {0} · 생존 +{1}", def.ArmorClass, def.ArmorClass);

                case EquipSlot.Earpiece:
                {
                    int step = HearingStep(def.HearingRange);
                    return AfterSeoul.Core.Loc.Text("청취 {0} · 좋은 물건 확률 +{1:P0} · 생존 +{2}", def.HearingRange, step * t.HeadsetLuckPerStep, step);
                }

                case EquipSlot.TacticalRig:
                    return AfterSeoul.Core.Loc.Text("{0}칸 · 회수 +{1}회", def.GridSlots, (def.GridSlots >= t.RigLargeSlots ? 2 : 1));

                case EquipSlot.Backpack:
                {
                    int r = def.GridSlots >= t.BackpackLargeSlots ? 3
                          : def.GridSlots >= t.BackpackMediumSlots ? 2 : 1;
                    return AfterSeoul.Core.Loc.Text("{0}칸 · 회수 +{1}회", def.GridSlots, r);
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

            if (worn == 0) return AfterSeoul.Core.Loc.Text("장비 없음 — 무기가 없으면 파견할 수 없습니다");

            string s = AfterSeoul.Core.Loc.Text("장비 {0}/{1}", worn, EquipSlot.All.Length);
            if (e.Luck > 0) s += AfterSeoul.Core.Loc.Text("   좋은 물건 +{0:P0}", e.Luck);
            if (e.ExtraLootRolls > 0) s += AfterSeoul.Core.Loc.Text("   회수 +{0}회", e.ExtraLootRolls);
            if (e.SurvivalBonus > 0) s += AfterSeoul.Core.Loc.Text("   생존 +{0}", e.SurvivalBonus);
            if (e.SeverityMitigation > 0) s += AfterSeoul.Core.Loc.Text("   치명상 완화 +{0:P0}", e.SeverityMitigation);
            if (!e.HasWeapon) s += AfterSeoul.Core.Loc.Text("   · 무기 없음");
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
