using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    public static class SeoulMapSelection
    {
        public static RectTransform Draw(Transform parent, IEnumerable<MapDef> maps, string buttonPrefix, Action<string> select)
        {
            var root = Ui.Rect("SeoulRegionMap", parent);
            var texture = Resources.Load<Texture2D>("ToonArt/seoul-map");
            var background = Ui.Rect("MapBackground", root).gameObject.AddComponent<RawImage>();
            background.texture = texture; background.color = Color.white; background.raycastTarget = false;
            Ui.Stretch(background.rectTransform);
            foreach (var map in maps) {
                string id = map.Id;
                var button = Ui.Button(buttonPrefix + id, root, Loc.MapName(id), () => select(id), Theme.Panel, 27);
                var rect = (RectTransform)button.transform;
                // Original DrawMapSelection uses a 74 x 22 terrain and MapX/MapY.
                float x = Mathf.Clamp((map.MapX + 7) / 74f, .14f, .86f);
                float y = Mathf.Clamp(1 - (map.MapY + 1) / 22f, .10f, .90f);
                rect.anchorMin = rect.anchorMax = new Vector2(x, y);
                rect.sizeDelta = new Vector2(250, 82); rect.anchoredPosition = Vector2.zero;
                var outline = button.gameObject.AddComponent<Outline>(); outline.effectColor = Theme.Accent;
                outline.effectDistance = new Vector2(2, -2);
            }
            return root;
        }
    }
}
