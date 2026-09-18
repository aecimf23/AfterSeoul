using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>The same seven body slots in the warehouse and before a raid.</summary>
    internal sealed class PlayerLoadoutPanel
    {
        internal const float Height = 690;
        internal readonly RectTransform Root;
        private readonly GameSession _session;
        private readonly Text _vitals;
        private readonly Dictionary<string, Text> _names = new Dictionary<string, Text>();
        private readonly Dictionary<string, Image> _icons = new Dictionary<string, Image>();
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();

        internal PlayerLoadoutPanel(Transform parent, GameSession session, Action<string> select)
        {
            _session = session;
            Root = Ui.Surface("PlayerLoadout", parent, Theme.Panel);
            Ui.Size(Root.gameObject, Height);
            _vitals = Ui.Label("PlayerVitals", Root, "", 29, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Top(_vitals.rectTransform, 48, 18);
            var hint = Ui.Label("EquipmentHint", Root, Loc.Text("부위를 눌러 장비 교체 · 빈 칸도 선택할 수 있습니다"), 25, TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Top(hint.rectTransform, 42, 18); hint.rectTransform.anchoredPosition = new Vector2(0, -48);
            var board = Ui.Rect("EquipmentBody", Root); Ui.Stretch(board, 16, 16, 98, 14);
            var figure = Ui.Rect("CharacterSilhouette", board);
            Place(figure, .37f, .34f, .63f, .99f);
            figure.gameObject.AddComponent<CharacterSilhouette>().raycastTarget = false;
            AddSlot(board, "Headwear", .01f,.70f,.30f,.99f, select);
            AddSlot(board, "Earpiece", .70f,.70f,.99f,.99f, select);
            AddSlot(board, "BodyArmor", .01f,.36f,.30f,.65f, select);
            AddSlot(board, "TacticalRig", .70f,.36f,.99f,.65f, select);
            AddSlot(board, "Weapon", .01f,.02f,.30f,.31f, select);
            AddSlot(board, "Melee", .70f,.02f,.99f,.31f, select);
            AddSlot(board, "Backpack", .355f,.02f,.645f,.31f, select);
            Refresh();
        }

        private void AddSlot(RectTransform board, string slot, float x0, float y0, float x1, float y1, Action<string> select)
        {
            var button = Ui.Button("Slot_" + slot, board, "", () => select(slot), Theme.PanelAlt, withLabel: false);
            var root = (RectTransform)button.transform; Place(root, x0, y0, x1, y1);
            var title = Ui.Label("SlotName", root, SlotLabel(slot), 26, TextAnchor.MiddleCenter, Theme.Info);
            Ui.Top(title.rectTransform, 34, 6);
            var name = Ui.Label("Equipped_" + slot, root, "", 24, TextAnchor.MiddleCenter);
            Ui.Bottom(name.rectTransform, 48, 8);
            var icon = Ui.Icon("EquippedIcon", root, _session.Data.GetItem("WPN04"), 68);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, .51f);
            icon.rectTransform.pivot = new Vector2(.5f, .5f);
            icon.rectTransform.sizeDelta = new Vector2(68, 68); icon.rectTransform.anchoredPosition = Vector2.zero;
            icon.raycastTarget = false;
            _buttons[slot] = button; _names[slot] = name; _icons[slot] = icon;
        }

        private static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0,y0); rt.anchorMax = new Vector2(x1,y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        internal void Refresh()
        {
            var player = _session.Save.Player;
            _vitals.text = Loc.Text("내 장비  ·  Lv.{0}   HP {1:0}   수분 {2:0}   에너지 {3:0}", player.CharacterLevel, player.Hp, player.Hydration, player.Energy);
            foreach (var slot in PlayerEquipment.Slots) {
                string id = PlayerEquipment.Equipped(_session.Save, slot);
                bool equipped = !string.IsNullOrEmpty(id);
                _names[slot].text = equipped ? ItemPresentation.Name(_session.Data, id) : Loc.Text("미착용 · 선택");
                _names[slot].color = equipped ? Theme.Text : Theme.TextFaint;
                _icons[slot].gameObject.SetActive(equipped);
                if (equipped) { _icons[slot].sprite = ItemArtwork.For(_session.Data.GetItem(id)); _icons[slot].color = Color.white; }
                Ui.SetEdge((RectTransform)_buttons[slot].transform, equipped ? Theme.AccentDim : Theme.Edge);
            }
        }

        internal static string SlotLabel(string slot)
        {
            switch (slot) {
                case "Weapon": return Loc.Text("주무기 · 손");
                case "Melee": return Loc.Text("근접 무기 · 손");
                case "Headwear": return Loc.Text("머리");
                case "BodyArmor": return Loc.Text("방탄복 · 몸통");
                case "Earpiece": return Loc.Text("헤드셋 · 귀");
                case "TacticalRig": return Loc.Text("전술 조끼");
                default: return Loc.Text("가방 · 등");
            }
        }

        internal static void Explain(RectTransform body, string name, string text, Color? color = null)
        {
            var label = Ui.Paragraph(name, body, text, 28, color ?? Theme.TextDim);
            label.resizeTextForBestFit = false;
        }

        internal static void Choices(RectTransform body, GameSession session, string slot, Action<string> equip, Action unequip)
        {
            string current = PlayerEquipment.Equipped(session.Save, slot);
            Explain(body, "CurrentEquipment", Loc.Text("현재 착용 · {0}", current == null ? Loc.Text("없음") : ItemPresentation.Name(session.Data, current)), Theme.Info);
            if (current != null) {
                var icon = Ui.Icon("CurrentEquipmentIcon", body, session.Data.GetItem(current), 120);
                string why = PlayerEquipment.UnequipBlockReason(session.Save, session.Data, slot);
                var remove = Ui.Button("Unequip", body, Loc.Text("장비 해제 · 창고로 돌려놓기"), unequip, Theme.PanelAlt, 28);
                Ui.Size(remove.gameObject, 86); remove.interactable = why == null;
                if (why != null) Explain(body, "UnequipReason", Loc.Text(why), Theme.Warn);
            }
            Explain(body, "SlotChoicesTitle", Loc.Text("이 부위에 착용할 수 있는 창고 장비"));
            var seen = new HashSet<string>();
            // Buttons may change the warehouse; never keep a mutable stack reference in callbacks.
            foreach (var stack in session.Save.Warehouse.Stacks) {
                string id = stack.ItemId;
                if (stack.Count <= 0 || PlayerEquipment.SlotFor(session.Data.GetItem(id)) != slot || !seen.Add(id)) continue;
                string reason = PlayerEquipment.EquipBlockReason(session.Save, session.Data, id);
                var row = Ui.Rect("EquipmentChoice_" + id, body); Ui.Size(row.gameObject, 108); Ui.Row(row, 12);
                Ui.Icon("Icon", row, session.Data.GetItem(id), 96);
                var button = Ui.Button("Equip_" + id, row, ItemPresentation.Name(session.Data, id) + "\n" + Loc.Text("착용"), () => equip(id), Theme.AccentDim, 28);
                Ui.Size(button.gameObject, flexWidth: 1); button.interactable = reason == null;
                if (reason != null) Explain(body, "EquipmentReason", Loc.Text(reason), Theme.Warn);
            }
            if (seen.Count == 0) Explain(body, "EquipmentEmpty", Loc.Text("창고에 이 부위의 장비가 없습니다. 탐색에서 얻은 장비가 이곳에 표시됩니다."));
            Explain(body, "EquipmentSwapHint", Loc.Text("교체한 기존 장비는 창고로 돌아갑니다. 총기와 근접 무기는 각각 착용합니다."));
        }
    }
}
