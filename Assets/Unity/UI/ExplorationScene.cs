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
        private Image _danger;
        private Image _grenade;
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
            int backdrop = (place ?? "").Contains("주차") ? 15 : GameArt.MapIndex(mapId);
            if(!(place??"").Contains("주차")) {
                var site=AfterSeoul.Exploration.RaidRegions.Find(mapId,place);
                if(site?.Indoors==true) backdrop=site.Container=="Medical" ? 9 : site.Container=="Weapon" ? 10 : mapId=="YONGSAN_MARKET" ? 0 : site.Container=="Pocket" ? 12 : 8;
                else if(mapId=="YONGSAN_MARKET") backdrop=14;
            }
            var siteInfo=AfterSeoul.Exploration.RaidRegions.Find(mapId,place);
            var artwork=siteInfo?.Indoors==true || (place??"").Contains("주차") ? GameArt.World(backdrop) : GameArt.RaidBackground(mapId,node);
            var background = GameArt.Draw("LocationArtwork", _world, artwork, false);
            Ui.Stretch(background.rectTransform);
            _enemyImage = GameArt.Draw("EnemyArtwork", _world, GameArt.Cell("actors", 0, 4, 2));
            _enemy = _enemyImage.rectTransform;
            _enemy.anchorMin = new Vector2(.33f, .02f); _enemy.anchorMax = new Vector2(.72f, .92f);
            _enemy.offsetMin = _enemy.offsetMax = Vector2.zero;
            _flash = Block(_enemy, "MuzzleFlash", .48f, .54f, .10f, .06f, Hex("FFE2A2"));
            _weather = Ui.Rect("Weather", Root);
            _danger=Block(Root,"ThreatFlash",0,0,1,1,new Color(1,.3f,.05f,0));
            _danger.raycastTarget=false;
            _grenade=GameArt.Draw("ThrownGrenade",Root,ItemArtwork.For(new AfterSeoul.Core.ItemDef{Id="GND01",Category="Grenade"}));
            _grenade.rectTransform.anchorMin=_grenade.rectTransform.anchorMax=new Vector2(.5f,.5f);
            _grenade.rectTransform.sizeDelta=new Vector2(85,85);_grenade.raycastTarget=false;
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
            bool grenade=phase=="Grenade", rush=phase=="Rush", explosion=phase=="Explosion";
            _enemy.anchoredPosition = new Vector2(cover ? 210 : Mathf.Sin(_time * 2) * 5, cover ? -90 : hurt ? -24 : 0);
            _enemy.localRotation = Quaternion.Euler(0, 0, hurt ? 12 : grenade ? -12 : 0);
            _enemy.localScale=Vector3.one*(rush ? 1.12f+Mathf.Sin(_time*10)*.04f : 1);
            _danger.color=new Color(1,.3f,.05f,explosion ? .48f : grenade || rush ? .08f+.05f*Mathf.Sin(_time*10) : 0);
            _grenade.gameObject.SetActive(enemyVisible && grenade);
            _grenade.rectTransform.anchoredPosition=new Vector2(75*Mathf.Sin(_time*4),30+65*Mathf.Abs(Mathf.Sin(_time*3)));
            _grenade.rectTransform.localRotation=Quaternion.Euler(0,0,_time*140);
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
