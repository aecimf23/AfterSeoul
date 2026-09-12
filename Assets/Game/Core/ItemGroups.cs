namespace AfterSeoul.Core
{
    /// <summary>
    /// 아이콘이 붙는 아이템군 (GDD §13).
    ///
    /// <para>아이템 300여 종에 그림을 하나씩 붙이지 않는다. 목록에서 필요한 건
    /// "이게 약인가 탄인가 고철인가"를 <b>한눈에</b> 가르는 것뿐이고, 그 이상은 이름이 말해준다.</para>
    ///
    /// <para>GDD 가 적어 둔 12군에 <see cref="Parts"/> 를 하나 더 뒀다. 본편 잡동사니 240종 중
    /// 대부분이 그냥 '부품'이라 갈 데가 없으면 전부 미분류가 되는데, 미분류가 제일 많은 분류는
    /// 분류가 아니다.</para>
    /// </summary>
    public enum ItemGroup
    {
        Parts = 0,
        Weapon,
        Ammo,
        Medical,
        Battery,
        Electronics,
        Metal,
        Bolt,
        Food,
        Tool,
        Armor,
        Document,
        Key,
    }

    /// <summary>
    /// 아이템 → 아이템군.
    ///
    /// <para>본편 태그를 그대로 쓴다. 모바일에서 목록을 따로 적으면 본편에 아이템이 추가될 때마다
    /// 여기도 손대야 하고, 안 고치면 조용히 미분류가 된다.</para>
    /// </summary>
    public static class ItemGroups
    {
        /// <summary>
        /// 판정 순서가 곧 규칙이다. 태그는 겹친다 — 볼트는 <c>부품·볼트·금속</c> 을 다 갖고 있어서,
        /// 금속을 먼저 보면 볼트가 영영 금속으로 분류된다. <b>좁은 것부터 본다.</b>
        /// </summary>
        public static ItemGroup Of(ItemDef def)
        {
            if (def == null) return ItemGroup.Parts;

            // 장착 칸이 있으면 그게 가장 확실한 단서다. 태그보다 먼저 본다.
            if (def.Equippable && !string.IsNullOrEmpty(def.EquipSlot))
                return def.EquipSlot == "Weapon" ? ItemGroup.Weapon : ItemGroup.Armor;

            // 배터리는 태그가 없다 (본편에 '배터리' 태그가 없고 전자로 묶여 있다).
            // 전자보다 먼저 걸러야 NVG 배터리가 전자부품으로 빠지지 않는다.
            if (StartsWith(def.Id, "BAT")) return ItemGroup.Battery;

            if (Has(def, "열쇠")) return ItemGroup.Key;
            if (Has(def, "문서")) return ItemGroup.Document;
            if (Has(def, "탄약")) return ItemGroup.Ammo;
            if (Has(def, "의료")) return ItemGroup.Medical;
            if (Has(def, "식량")) return ItemGroup.Food;
            if (Has(def, "총기") || Has(def, "근접") || Has(def, "총기부품")) return ItemGroup.Weapon;
            if (Has(def, "방탄장비") || Has(def, "가방") || Has(def, "컨테이너")) return ItemGroup.Armor;
            if (Has(def, "전자")) return ItemGroup.Electronics;
            if (Has(def, "볼트")) return ItemGroup.Bolt;
            if (Has(def, "금속")) return ItemGroup.Metal;
            if (Has(def, "도구")) return ItemGroup.Tool;

            // 카테고리는 태그가 비어 있을 때의 마지막 단서다.
            switch (def.Category)
            {
                case "Ammo": return ItemGroup.Ammo;
                case "Medical": return ItemGroup.Medical;
                case "Food": return ItemGroup.Food;
                case "Container": return ItemGroup.Armor;
                default: return ItemGroup.Parts;
            }
        }

        public static string LabelOf(ItemGroup group)
        {
            switch (group)
            {
                case ItemGroup.Weapon: return "총기";
                case ItemGroup.Ammo: return "탄약";
                case ItemGroup.Medical: return "약품";
                case ItemGroup.Battery: return "배터리";
                case ItemGroup.Electronics: return "전자부품";
                case ItemGroup.Metal: return "금속";
                case ItemGroup.Bolt: return "볼트";
                case ItemGroup.Food: return "식량";
                case ItemGroup.Tool: return "도구";
                case ItemGroup.Armor: return "방탄장비";
                case ItemGroup.Document: return "문서";
                case ItemGroup.Key: return "열쇠";
                default: return "부품";
            }
        }

        public static readonly ItemGroup[] All =
        {
            ItemGroup.Parts, ItemGroup.Weapon, ItemGroup.Ammo, ItemGroup.Medical,
            ItemGroup.Battery, ItemGroup.Electronics, ItemGroup.Metal, ItemGroup.Bolt,
            ItemGroup.Food, ItemGroup.Tool, ItemGroup.Armor, ItemGroup.Document, ItemGroup.Key,
        };

        private static bool Has(ItemDef def, string tag)
        {
            if (def.Tags == null) return false;
            foreach (var t in def.Tags) if (t == tag) return true;
            return false;
        }

        private static bool StartsWith(string id, string prefix) =>
            !string.IsNullOrEmpty(id) && id.Length >= prefix.Length &&
            string.CompareOrdinal(id, 0, prefix, 0, prefix.Length) == 0;
    }
}
