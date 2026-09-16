using AfterSeoul.Core;
using AfterSeoul.Factory;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>Positions come directly from the same conveyor that judges the input.</summary>
    internal sealed class ProductionConveyorView
    {
        readonly RectTransform _root, _zone;
        readonly Image[] _parts = new Image[16];
        readonly Text[] _work = new Text[16];
        readonly Image _zoneInk;
        public RectTransform Root => _root;

        public ProductionConveyorView(Transform parent)
        {
            _root = Ui.Surface("ProductionConveyor", parent, Theme.Bg, Theme.Edge);
            _root.gameObject.AddComponent<RectMask2D>();
            _zone = Ui.Rect("TimingZone", _root);
            _zoneInk = _zone.gameObject.AddComponent<Image>(); _zoneInk.raycastTarget = false;
            var guide = Ui.Label("ConveyorGuide", _root, Loc.Text("부품이 중앙 구간에 오면 누르세요"), 23, TextAnchor.UpperCenter, Theme.Text);
            Ui.Top(guide.rectTransform, 34, 6);
            var belt = Ui.Surface("ConveyorBelt", _root, Theme.PanelAlt, Theme.Edge);
            Ui.Bottom(belt, 22, 4);
            for (int i=0; i<12; i++) {
                var roller = Ui.Rect("BeltRoller" + i, belt);
                roller.anchorMin = new Vector2(i / 12f + .01f, .16f);
                roller.anchorMax = new Vector2((i + 1) / 12f - .01f, .84f);
                roller.offsetMin = roller.offsetMax = Vector2.zero;
                var ink = roller.gameObject.AddComponent<Image>();
                ink.color = Theme.TextFaint; ink.raycastTarget = false;
            }
            for (int i=0; i<_parts.Length; i++) {
                var rt = Ui.Rect("ConveyorPart" + i, _root);
                rt.sizeDelta = new Vector2(84, 52);
                var part = rt.gameObject.AddComponent<Image>();
                part.preserveAspect = true; part.raycastTarget = false;
                _parts[i] = part;
                var work = Ui.Label("ConveyorPartWork" + i, rt, "", 19, TextAnchor.MiddleCenter, Theme.Text);
                work.rectTransform.anchorMin = new Vector2(-.2f, -.44f);
                work.rectTransform.anchorMax = new Vector2(1.2f, .05f);
                work.rectTransform.offsetMin = work.rectTransform.offsetMax = Vector2.zero;
                _work[i] = work;
            }
        }
        public void Draw(ProductionConveyor conveyor)
        {
            _zone.anchorMin = new Vector2((float)conveyor.HitStart, .23f);
            _zone.anchorMax = new Vector2((float)conveyor.HitEnd, .74f);
            _zone.offsetMin = _zone.offsetMax = Vector2.zero;
            bool ready = false;
            for (int i=0; i<_parts.Length; i++) {
                var image = _parts[i];
                bool visible = i < conveyor.Parts.Count;
                image.gameObject.SetActive(visible);
                if (!visible) continue;
                var part = conveyor.Parts[i];
                var rt = image.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2((float)part.Position, .49f);
                rt.anchoredPosition = Vector2.zero;
                image.sprite = GameArt.Material(part.Tier);
                _work[i].text = Loc.Text("{0}등급", part.Tier) + " +" + part.Work.ToString("0.#");
                _work[i].color = part.Tier == 1 ? Theme.Text : part.Tier == 2 ? Theme.Safe : part.Tier == 3 ? Theme.Info : Theme.Accent;
                ready |= part.Position >= conveyor.HitStart && part.Position <= conveyor.HitEnd;
            }
            var color = ready && conveyor.CooldownRemaining <= 0 ? Theme.Safe : Theme.TextFaint;
            color.a = ready ? .35f : .15f; _zoneInk.color = color;
        }
    }
}
