using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    public static class GameArt
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        public static Sprite Cell(string sheet, int index, int columns, int rows)
        {
            string key = sheet + ":" + index;
            if (Cache.TryGetValue(key, out var sprite) && sprite != null) return sprite;
            var texture = Resources.Load<Texture2D>("ToonArt/" + sheet);
            if (texture == null) return null;
            float w = texture.width / (float)columns, h = texture.height / (float)rows;
            var rect = new Rect(index % columns * w, texture.height - (index / columns + 1) * h, w, h);
            // Bust framing keeps faces readable in compact mobile conversation panels.
            if (sheet == "npcs") { rect.y = texture.height * .36f; rect.height = texture.height * .64f; }
            sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = key; Cache[key] = sprite; return sprite;
        }
        public static Image Draw(string name, Transform parent, Sprite sprite, bool preserve = true)
        {
            var image = Ui.Rect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.color = Color.white; image.preserveAspect = preserve; image.raycastTarget = false;
            return image;
        }
        public static int NpcIndex(string id) => id == "DR_CHOI" ? 1 : id == "YONGSAN_KIM" ? 2 : 0;
        public static Sprite Npc(string id)
        {
            int index = System.Array.IndexOf(new[] { "DONGDAEMUN_CHOI", "DOKKAEBI", "WILDMAN", "US_LIAISON", "BROKER" }, id);
            return index >= 0 ? Cell("regional-npcs", index, 3, 2) : Cell("npcs", NpcIndex(id), 3, 1);
        }
        public static Image Portrait(string name, Transform parent, string id, float height)
        {
            var image = Draw(name, parent, Npc(id));
            Ui.Size(image.gameObject, height); return image;
        }
        public static int MapIndex(string id)
        {
            switch (id) {
                case "GURO_FACTORY": return 1; case "HAN_RIVER": return 2;
                case "NAMSAN_WOODS": return 3; case "GANGNAM_STREETS": return 4;
                case "YONGSAN_BASE": return 5; case "MYEONGDONG": return 6;
                case "UIJEONGBU": return 7; default: return 0;
            }
        }
        public static Sprite World(int index) => Cell("world", index, 4, 4);
        public static Image MapThumbnail(Transform parent, string mapId) => Draw("MapArtwork", parent, World(MapIndex(mapId)), false);
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        private static readonly string[] WeaponOrder = {
            "WPN04", "WPN21", "WPN23", "WPN08", "WPN09", "WPN14", "WPN20", "WPN13",
            "WPN19", "WPN26", "WPN01", "WPN02", "WPN05", "WPN11", "WPN17", "WPN06",
            "WPN07", "WPN15", "WPN27", "WPN03", "WPN18", "WPN12", "WPN16", "WPN10", "WPN22"
        };

        public static Sprite Portrait(string npc) => Npc(npc);
        public static Sprite Location(string npc) => World(8 + NpcIndex(npc));
        public static Sprite City() => Tile("city", 1, 1, 0);
        public static Sprite LaunchMap() => Tile("launch_map_mobile", 1, 1, 0);
        public static Sprite Worker(int frame, int variant = -1) =>
            Tile("workers_anime", 4, 4, (Mathf.Clamp(variant, -1, 2) + 1) * 4 + (frame >= 0 && frame < 4 ? frame : 0));
        public static Sprite Weapon(string weaponId)
        {
            int index = System.Array.IndexOf(WeaponOrder, weaponId);
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

        private static Sprite Tile(string atlas, int columns, int rows, int index)
        {
            string key = atlas + ":" + index;
            if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            if (!Textures.TryGetValue(atlas, out var texture) || texture == null)
            {
                texture = Resources.Load<Texture2D>("Art/" + atlas);
                if (texture == null) throw new System.InvalidOperationException("Missing mobile art atlas: " + atlas);
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

