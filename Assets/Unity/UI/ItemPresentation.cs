using AfterSeoul.Core;

namespace AfterSeoul.Unity.UI
{
    internal static class ItemPresentation
    {
        internal static string SlotLabel(string slot)
        {
            switch(slot) {
                case "Weapon": return Loc.Text("총기"); case "Melee": return Loc.Text("근접 무기");
                case "Headwear": return Loc.Text("머리"); case "BodyArmor": return Loc.Text("방탄복");
                case "Earpiece": return Loc.Text("헤드셋"); case "TacticalRig": return Loc.Text("전술 조끼");
                default: return Loc.Text("가방");
            }
        }

        internal static string Stats(IDataRegistry data, string id)
        {
            var def=data.GetItem(id);
            if(def==null) return "";
            var lines=new System.Collections.Generic.List<string>();
            var gun=AfterSeoul.Exploration.CombatProfiles.For(id);
            if(gun!=null) {
                lines.Add(Loc.Text("기본 피해 {0} · 기본 명중률 {1}%",gun.Damage,gun.Accuracy*100));
                lines.Add(Loc.Text("탄창 {0}발 · 사용 탄약 {1}",gun.Magazine,gun.Caliber));
                var modes=new System.Collections.Generic.List<string>();
                foreach(var mode in gun.Modes) modes.Add(Loc.Text(mode==AfterSeoul.Exploration.FireMode.Single ? "단발" : mode==AfterSeoul.Exploration.FireMode.Burst ? "점사" : "연사"));
                lines.Add(string.Join(" / ",modes));
            }
            if(AfterSeoul.Exploration.PlayerEquipment.SlotFor(def)=="Melee")
                lines.Add(Loc.Text("근접 피해 {0} · 공격 간격 {1}초",AfterSeoul.Exploration.CombatProfiles.MeleeDamage,AfterSeoul.Exploration.CombatProfiles.MeleeCooldown));
            if(def.ArmorClass>0) lines.Add(Loc.Text("방어 등급 {0}",def.ArmorClass));
            if(def.GridSlots>0) lines.Add(Loc.Text("적재 공간 {0}칸",def.GridSlots));
            if(def.HearingRange>0) lines.Add(Loc.Text("청취 반경 {0}",def.HearingRange));
            var consumable=AfterSeoul.Exploration.CombatProfiles.ConsumableFor(id);
            if(consumable!=null) {
                if(consumable.Hp!=0) lines.Add(Loc.Text("체력 회복 {0}",consumable.Hp));
                if(consumable.Energy!=0) lines.Add(Loc.Text("에너지 변화 {0}",consumable.Energy));
                if(consumable.Hydration!=0) lines.Add(Loc.Text("수분 변화 {0}",consumable.Hydration));
                lines.Add(Loc.Text("사용 시간 {0}초",consumable.Seconds));
            }
            if(def.Category=="Ammo") lines.Add(Loc.Text("사용 탄약 {0}",AfterSeoul.Exploration.ExplorationSystem.AmmoCaliber(id)));
            return string.Join("\n",lines);
        }
        internal static string Name(IDataRegistry data, string id)
        {
            string key = "ITEM_" + id + "_NAME";
            if (Loc.Has(key)) return Loc.Get(key);
            string name = data.GetItem(id)?.ShortName;
            return string.IsNullOrEmpty(name) ? id : name;
        }
    }
}
