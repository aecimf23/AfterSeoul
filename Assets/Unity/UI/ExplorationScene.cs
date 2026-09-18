using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    // Illustrated environment and animated poses; weather stays a lightweight overlay.
    internal sealed class ExplorationScene
    {
        internal readonly RectTransform Root;
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var color); return color; }
        private RectTransform _world, _enemy, _weather;
        private Image _enemyImage;
        private Image _flash;
        private string _place, _climate, _map;
        private float _time;
        private int _node=-1;

        internal ExplorationScene(Transform parent)
        {
            Root = Ui.Rect("ExplorationScene", parent);
            Root.gameObject.AddComponent<RectMask2D>();
        }

        private static Image Block(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Ui.Panel(name, parent, color);
            var r = image.rectTransform;
            r.anchorMin = new Vector2(x, y); r.anchorMax = new Vector2(x + w, y + h);
            r.offsetMin = r.offsetMax = Vector2.zero;
            return image;
        }

        internal void SetLocation(string place, string climate, string mapId = null, int node = 0)
        {
            if (_place == place && _climate == climate && _map == mapId && _node == node) return;
            _place = place; _climate = climate; _map = mapId; _node=node;
            Ui.Clear(Root);
            _world = Ui.Rect("Environment", Root);

            var background = GameArt.Draw("LocationArtwork", _world, GameArt.RaidBackground(mapId,node), false);
            Ui.Stretch(background.rectTransform);
            _enemyImage = GameArt.Draw("EnemyArtwork", _world, GameArt.Cell("actors", 0, 4, 2));
            _enemy = _enemyImage.rectTransform;
            _enemy.anchorMin = new Vector2(.33f, .02f); _enemy.anchorMax = new Vector2(.72f, .92f);
            _enemy.offsetMin = _enemy.offsetMax = Vector2.zero;
            _flash = Block(_enemy, "MuzzleFlash", .48f, .54f, .10f, .06f, Hex("FFE2A2"));
            _weather = Ui.Rect("Weather", Root);
            if (climate == "Rain")
                for (int i = 0; i < 35; i++)
                    Block(_weather, "Rain", (i * .173f) % 1, (i * .317f) % 1, .002f, .10f, new Color(.65f,.80f,.86f,.24f));
            if (climate == "Fog")
                for (int i = 0; i < 5; i++)
                    Block(_weather, "Fog", -.15f, i * .20f, 1.3f, .23f, new Color(.65f,.76f,.77f,.17f));
        }

        internal void Animate(float dt, bool enemyVisible, string phase, float hpRatio, string kind = null)
        {
            if (_enemy == null) return;
            _time += dt;
            _enemy.gameObject.SetActive(enemyVisible);
            bool cover = phase == "Cover" || phase == "Covered";
            bool aim = phase == "Aiming" || phase == "Aim";
            bool hurt = phase == "Injured";
            _enemy.anchoredPosition = new Vector2(cover ? 210 : Mathf.Sin(_time * 2) * 5, cover ? -90 : hurt ? -24 : 0);
            _enemy.localRotation = Quaternion.Euler(0, 0, hurt ? 12 : 0);
            _enemyImage.sprite = GameArt.Cell("actors", (kind == "PMC" ? 4 : 0) + (cover ? 3 : hurt ? 2 : aim || phase == "Firing" ? 1 : 0), 4, 2);
            _flash.gameObject.SetActive(phase == "Firing" || phase == "Fire");
            for (int i = 0; i < _weather.childCount; i++) {
                var r = (RectTransform)_weather.GetChild(i);
                if (_climate == "Rain") r.anchoredPosition = new Vector2(-(_time * 80 % 90), -(_time * 420 % 120));
                else r.anchoredPosition = new Vector2(Mathf.Sin(_time * .3f + i) * 35, 0);
            }
        }
    }
}
