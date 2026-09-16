using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>Shared mobile raster atlases. Grid coordinates are authored from the top left.</summary>
    public static class GameArt
    {
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        private static readonly string[] WeaponOrder = {
            "WPN04", "WPN21", "WPN23", "WPN08", "WPN09", "WPN14", "WPN20", "WPN13",
            "WPN19", "WPN26", "WPN01", "WPN02", "WPN05", "WPN11", "WPN17", "WPN06",
            "WPN07", "WPN15", "WPN27", "WPN03", "WPN18", "WPN12", "WPN16", "WPN10", "WPN22"
        };

        public static Sprite Portrait(string npc) => Tile("npc_portraits_chibi", 3, 1, NpcIndex(npc));
        public static Sprite Location(string npc) => Tile("npc_locations_anime", 3, 2, 3 + NpcIndex(npc));
        public static Sprite City() => Tile("city", 1, 1, 0);
        public static Sprite LaunchMap() => Tile("launch_map_mobile", 1, 1, 0);
        public static Sprite Worker(int frame, int variant = -1) =>
            Tile("workers_anime", 4, 4, (Mathf.Clamp(variant, -1, 2) + 1) * 4 + (frame >= 0 && frame < 4 ? frame : 0));
        public static Sprite Weapon(string weaponId)
        {
            int index = Array.IndexOf(WeaponOrder, weaponId);
            return Tile("weapons_materials", 5, 6, index < 0 ? 0 : index);
        }
        public static Sprite Material(int tier) => Tile("weapons_materials", 5, 6, 25 + Mathf.Clamp(tier - 1, 0, 3));
        public static Sprite EmptyBench() => Tile("weapons_materials", 5, 6, 29);

        public static Image Place(string name, Transform parent, Sprite sprite, bool preserveAspect = true)
        {
            var image = Ui.Rect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = preserveAspect;
            image.raycastTarget = false;
            return image;
        }

        private static int NpcIndex(string npc)
        {
            switch (npc)
            {
                case "DR_CHOI": return 1;
                case "YONGSAN_KIM": return 2;
                default: return 0;
            }
        }

        private static Sprite Tile(string atlas, int columns, int rows, int index)
        {
            string key = atlas + ":" + index;
            if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            if (!Textures.TryGetValue(atlas, out var texture) || texture == null)
            {
                texture = Resources.Load<Texture2D>("Art/" + atlas);
                if (texture == null) throw new InvalidOperationException("Missing mobile art atlas: " + atlas);
                Textures[atlas] = texture;
            }
            float width = texture.width / (float)columns;
            float height = texture.height / (float)rows;
            var rect = new Rect(index % columns * width, (rows - 1 - index / columns) * height, width, height);
            var sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = "MobileArt/" + key;
            sprite.hideFlags = HideFlags.DontSave;
            Sprites[key] = sprite;
            return sprite;
        }
    }
}
