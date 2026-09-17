using System.Collections.Generic;
using AfterSeoul.Core;
using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    // Row-major order in the single transparent 6×4 inventory atlas.
    public enum ItemArtworkKind
    {
        Pistol, Rifle, Knife, Ammunition, MedicalPouch, Bandage,
        Injection, FoodTin, WaterBottle, DrinkCan, Wrench, Bolts,
        CircuitBoard, Battery, Cables, Metal, Armor, Helmet,
        Backpack, Documents, Keys, Valuables, SpareParts, Tape
    }

    public static class ItemArtwork
    {
        public const string ResourcePath = "ItemIcons/toon-survival-atlas";
        private static Texture2D atlas;
        private static readonly Dictionary<ItemArtworkKind, Sprite> Cache = new Dictionary<ItemArtworkKind, Sprite>();
        private static readonly Dictionary<string, Sprite> ItemCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Texture2D> Sheets = new Dictionary<string, Texture2D>();
        private static Dictionary<string, CatalogEntry> entries;
        [System.Serializable] private sealed class Catalog { public int columns; public int rows; public CatalogEntry[] entries; }
        [System.Serializable] private sealed class CatalogEntry { public string id; public string resource; public int column; public int row; public int top; public int bottom; }
        private static int columns = 8, rows = 5;

        public static Sprite For(AfterSeoul.Core.ItemDef item)
        {
            if (item == null) return For(ItemArtworkKind.SpareParts);
            if (ItemCache.TryGetValue(item.Id, out var cached) && cached != null) return cached;
            if (entries == null) {
                entries = new Dictionary<string, CatalogEntry>();
                var json = Resources.Load<TextAsset>("ItemIcons/catalog");
                if (json != null) {
                    var catalog = JsonUtility.FromJson<Catalog>(json.text);
                    columns = catalog.columns; rows = catalog.rows;
                    foreach (var entry in catalog.entries) entries.Add(entry.id, entry);
                }
            }
            if (!entries.TryGetValue(item.Id, out var cell)) return For(KindOf(item));
            if (!Sheets.TryGetValue(cell.resource, out var texture) || texture == null) {
                texture = Resources.Load<Texture2D>(cell.resource);
                if (texture == null) return For(KindOf(item));
                Sheets[cell.resource] = texture;
            }
            int left = Mathf.RoundToInt(cell.column * texture.width / (float)columns);
            int right = Mathf.RoundToInt((cell.column + 1) * texture.width / (float)columns);
            int top = Mathf.RoundToInt(cell.row * texture.height / (float)rows);
            int bottom = Mathf.RoundToInt((cell.row + 1) * texture.height / (float)rows);
            if (cell.bottom > cell.top) { top = cell.top; bottom = cell.bottom; }
            var sprite = Sprite.Create(texture, new Rect(left, texture.height - bottom, right - left, bottom - top),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = "Item_" + item.Id;
            ItemCache[item.Id] = sprite;
            return sprite;
        }

        public static Sprite For(ItemArtworkKind kind)
        {
            if (Cache.TryGetValue(kind, out var cached) && cached != null) return cached;
            if (atlas == null) atlas = Resources.Load<Texture2D>(ResourcePath);
            if (atlas == null) return null;
            int index = (int)kind;
            if (index < 0 || index >= 24) return null;
            float width = atlas.width / 6f;
            // The painted bottom row starts above the nominal grid. Cut through
            // the transparent gutters so neighboring silhouettes never leak in.
            int row = index / 6;
            int[] rowEdges = { 0, 250, 506, 744, 1024 };
            float scale = atlas.height / 1024f;
            var rect = new Rect((index % 6) * width, atlas.height - rowEdges[row + 1] * scale,
                width, (rowEdges[row + 1] - rowEdges[row]) * scale);
            var sprite = Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = "ItemArtwork_" + kind;
            Cache[kind] = sprite;
            return sprite;
        }

        public static ItemArtworkKind KindOf(ItemDef item)
        {
            if (item == null) return ItemArtworkKind.SpareParts;
            string id = item.Id ?? "";
            if (id.StartsWith("MEL")) return ItemArtworkKind.Knife;
            if (id == "WPN04" || id == "WPN21") return ItemArtworkKind.Pistol;
            if (id == "WPN_PARTS") return ItemArtworkKind.SpareParts;
            if (item.EquipSlot == "Backpack" || item.Category == "Container") return ItemArtworkKind.Backpack;
            if (item.EquipSlot == "Headwear") return ItemArtworkKind.Helmet;
            if (id == "MED16") return ItemArtworkKind.Bandage;
            if (id == "MED04" || id == "MED08" || id == "MED09" || id == "MED11" || id == "MED12" || id == "MED19" || id == "MED20") return ItemArtworkKind.Injection;
            if (id == "FOOD05") return ItemArtworkKind.WaterBottle;
            if (id == "FOOD02" || id == "FOOD07" || id == "FOOD12") return ItemArtworkKind.DrinkCan;
            if (id == "JUNK21") return ItemArtworkKind.Battery;
            if (id == "JUNK10" || id == "JUNK20" || id == "JUNK32") return ItemArtworkKind.Cables;
            if (id == "JUNK17" || id == "JUNK18" || id == "JUNK19") return ItemArtworkKind.Tape;
            if (id == "JUNK03" || id == "JUNK04" || id == "JUNK15") return ItemArtworkKind.Bolts;
            if (id == "JUNK06" || id == "JUNK13" || id == "JUNK14" || id == "JUNK39" || id == "JUNK40") return ItemArtworkKind.Valuables;
            return KindOf(ItemGroups.Of(item));
        }

        public static ItemArtworkKind KindOf(ItemGroup group)
        {
            switch (group) {
                case ItemGroup.Weapon: return ItemArtworkKind.Rifle;
                case ItemGroup.Ammo: return ItemArtworkKind.Ammunition;
                case ItemGroup.Medical: return ItemArtworkKind.MedicalPouch;
                case ItemGroup.Battery: return ItemArtworkKind.Battery;
                case ItemGroup.Electronics: return ItemArtworkKind.CircuitBoard;
                case ItemGroup.Metal: return ItemArtworkKind.Metal;
                case ItemGroup.Bolt: return ItemArtworkKind.Bolts;
                case ItemGroup.Food: return ItemArtworkKind.FoodTin;
                case ItemGroup.Tool: return ItemArtworkKind.Wrench;
                case ItemGroup.Armor: return ItemArtworkKind.Armor;
                case ItemGroup.Document: return ItemArtworkKind.Documents;
                case ItemGroup.Key: return ItemArtworkKind.Keys;
                default: return ItemArtworkKind.SpareParts;
            }
        }
    }
}
