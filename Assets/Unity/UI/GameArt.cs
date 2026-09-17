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
    }
}
