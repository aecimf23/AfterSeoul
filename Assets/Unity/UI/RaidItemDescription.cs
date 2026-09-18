using System;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Exploration;

namespace AfterSeoul.Unity.UI
{
    internal static class RaidItemDescription
    {
        internal static string Describe(IDataRegistry data, string id, string current = null)
        {
            var item = data.GetItem(id);
            string slot = PlayerEquipment.SlotFor(item);
            var old = data.GetItem(current);
            var gun = CombatProfiles.For(id);
            if (gun != null) {
                var previous = CombatProfiles.For(current);
                string modes = string.Join(" / ", gun.Modes.Select(m => m == FireMode.Single ? "단발" : m == FireMode.Burst ? "점사" : "연사"));
                return Loc.Text("1발 피해 {0} · 탄창 {1}발\n호환 탄약 {2} · {3}", Delta(previous?.Damage ?? 0, gun.Damage), gun.Magazine, gun.Caliber, modes)
                    + "\n" + Loc.Text("기본 명중률 {0:0}% · 날씨·사격 방식·엄폐에 따라 달라집니다", gun.Accuracy * 100);
            }
            if (slot == "Melee") return Loc.Text("근접 피해 26 · 탄약 불필요 · 공격 간격 1.2초");
            if (slot == "BodyArmor" || slot == "Headwear")
                return Loc.Text("방어 등급 {0}\n총상 피해 감소 {1}% · 헬멧과 방탄복 합산 최대 60%", Delta(old?.ArmorClass ?? 0, item.ArmorClass), Delta((old?.ArmorClass ?? 0) * 3.5, item.ArmorClass * 3.5));
            if (slot == "Earpiece") return Loc.Text("발소리 청취 {0} · 비가 오면 양쪽 모두 감소", Delta(Math.Max(7, old?.HearingRange ?? 0), Math.Max(7, item.HearingRange)));
            if (slot == "Backpack" || slot == "TacticalRig") return Loc.Text("전리품 추가 공간 {0}종\n같은 물건은 한 종류로 합쳐 보관합니다", Delta(RaidEquipment.ExtraLootTypes(old), RaidEquipment.ExtraLootTypes(item)));
            var food = CombatProfiles.ConsumableFor(id);
            if (food != null) return Loc.Text("HP {0:+0;-0;0} · 에너지 {1:+0;-0;0} · 수분 {2:+0;-0;0}", food.Hp, food.Energy, food.Hydration);
            if (item?.Category == "Ammo") return Loc.Text("호환 구경 · {0}", ExplorationSystem.AmmoCaliber(id));
            return "";
        }

        private static string Delta(double before, double after)
        {
            return before.ToString("0.#") + " → " + after.ToString("0.#") + (before == after ? " (동일)" : " (" + (after - before).ToString("+0.#;-0.#") + ")");
        }
    }
}
