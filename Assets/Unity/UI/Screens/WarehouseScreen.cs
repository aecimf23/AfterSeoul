using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Exploration;
using AfterSeoul.Mail;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI.Screens
{
    public sealed class WarehouseScreen : ScreenBase
    {
        public override string TabName => "창고";
        public override IconSet.TabGlyph Glyph => IconSet.TabGlyph.Warehouse;

        private RectTransform _list;
        private ScrollRect _scroll;
        private Text _summary;
        private Text _empty;
        private Button _filterButton;
        private ItemGroup? _filter;
        private RectTransform _modal;
        private string _selected;
        private int _detailCount;
        private PlayerLoadoutPanel _loadout;
        private readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();

        private sealed class Row
        {
            public RectTransform Root;
            public Text Name;
            public Text Count;
        }

        protected override void Build()
        {
            var col = Ui.Rect("Col", Root);
            Ui.Stretch(col, Theme.Gutter, Theme.Gutter, 16f, 16f);
            _loadout = new PlayerLoadoutPanel(col, Session, OpenEquipment);
            Ui.Top(_loadout.Root, PlayerLoadoutPanel.Height);
            var inventory = Ui.Rect("Inventory", col);
            Ui.Stretch(inventory, 0, 0, PlayerLoadoutPanel.Height + 12, 0);
            _summary = Ui.Label("Summary", inventory, "", Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Top(_summary.rectTransform, 78f);
            var filterHost = Ui.Rect("Filters", inventory);
            Ui.Top(filterHost, 76f);
            filterHost.anchoredPosition = new Vector2(0f, -86f);
            _filterButton = Ui.Button("CategoryFilter", filterHost, Loc.Text("종류: 전체"), OpenFilters, Theme.Panel, Theme.FontSmall);
            _filterButton.GetComponent<RectTransform>().anchorMax=new Vector2(.55f,1);
            var equipment=Ui.Button("PlayerLoadout",filterHost,Loc.Text("내 장비"),OpenPlayerLoadout,Theme.Accent,Theme.FontSmall);
            equipment.GetComponent<RectTransform>().anchorMin=new Vector2(.58f,0);
            var listHost = Ui.Rect("ListHost", inventory);
            Ui.Stretch(listHost, 0f, 0f, 174f, 0f);
            _list = Ui.ScrollList("Scroll", listHost, out _scroll, 6f);
            _empty = Ui.Label("Empty", _list, "", Theme.FontBody, TextAnchor.UpperLeft, Theme.TextFaint);
            Ui.Size(_empty.gameObject, 150f);
        }

        public override void Refresh()
        {
            if (_list == null) return;
            _loadout.Refresh();
            // Unity destroyed objects compare equal to null; never reopen a closed/destroyed popup.
            if (_modal == null) _selected = null;
            var save = Session.Save;
            var totals = new Dictionary<string, int>();
            long totalValue = 0;
            foreach (var stack in save.Warehouse.Stacks)
            {
                totals.TryGetValue(stack.ItemId, out int count);
                totals[stack.ItemId] = count + stack.Count;
                totalValue += Market.SellPrice(save, Session.Data, stack.ItemId) * stack.Count;
            }
            string mail = save.Mail.Linked
                ? Loc.Text("본편 발송 남은 한도 {0} · {1}회", Theme.Won(Outbox.RemainingDailyValue(save, Session.Data, Session.Clock.UtcNow)),
                    Outbox.RemainingShipments(save, Session.Data, Session.Clock.UtcNow))
                : Loc.Text("아이템을 눌러 설명과 가능한 행동을 확인하세요.");
            int pending = 0;
            foreach (var shipment in save.Mail.Outbox) if (!shipment.Claimed) pending++;
            if (save.Mail.Linked && pending > 0) mail += Loc.Text("   ·   수령 대기 {0}건", pending);
            _summary.text = Loc.Text("{0} / {1} 칸   ·   판매가 합계 {2}\n", save.Warehouse.Stacks.Count,
                save.Warehouse.TotalCapacity, Theme.Won(totalValue)) + mail;
            Ui.SetButtonLabel(_filterButton, Loc.Text("종류: {0}", _filter.HasValue ? ItemGroups.LabelOf(_filter.Value) : Loc.Text("전체")));

            var removed = new List<string>();
            foreach (var pair in _rows)
                if (!totals.ContainsKey(pair.Key)) removed.Add(pair.Key);
            foreach (var id in removed)
            {
                DestroyView(_rows[id].Root);
                _rows.Remove(id);
            }
            var sorted = new List<string>(totals.Keys);
            sorted.Sort((a, b) =>
            {
                long va = Market.SellPrice(save, Session.Data, a) * totals[a];
                long vb = Market.SellPrice(save, Session.Data, b) * totals[b];
                int c = vb.CompareTo(va);
                return c == 0 ? string.CompareOrdinal(a, b) : c;
            });
            int visible = 0;
            foreach (var id in sorted)
            {
                if (!_rows.TryGetValue(id, out var row)) _rows[id] = row = BuildRow(id);
                row.Name.text = ItemPresentation.Name(Session.Data, id);
                row.Count.text = "×" + totals[id];
                bool show = !_filter.HasValue || ItemGroups.Of(Session.Data.GetItem(id)) == _filter.Value;
                row.Root.gameObject.SetActive(show);
                if (show)
                {
                    row.Root.SetSiblingIndex(visible++);
                    row.Root.GetComponent<Image>().color = visible % 2 == 0 ? Theme.PanelAlt : Theme.Panel;
                }
            }
            _empty.gameObject.SetActive(visible == 0);
            _empty.text = totals.Count == 0 ? Loc.Text("창고가 비어 있습니다.\n공장에서 일하거나 탐색을 보내 물자를 모으세요.")
                : Loc.Text("이 종류의 아이템이 없습니다.");
            if (_selected != null)
            {
                int count = Warehouse.CountOf(save.Warehouse, _selected);
                if (count == 0) CloseModal();
                else if (count != _detailCount) OpenDetail(_selected);
            }
        }

        private Row BuildRow(string id)
        {
            var button = Ui.Button("Item_" + id, _list, "", () => OpenDetail(id), Theme.Panel, withLabel: false);
            var root = (RectTransform)button.transform;
            Ui.Size(button.gameObject, 96f);
            var content = Ui.Rect("Row", root);
            Ui.Stretch(content, 16f, 16f, 8f, 8f);
            Ui.Row(content, 14f);
            Ui.Icon("Icon", content, Session.Data.GetItem(id), 78f);
            var name = Ui.Label("Name", content, ItemPresentation.Name(Session.Data, id), Theme.FontBody);
            Ui.Size(name.gameObject, flexWidth: 1f);
            var count = Ui.Label("Count", content, "", Theme.FontBody, TextAnchor.MiddleRight, Theme.TextDim);
            Ui.Size(count.gameObject, width: 130f, flexWidth: 0f);
            return new Row { Root = root, Name = name, Count = count };
        }

        private void OpenFilters()
        {
            CloseModal();
            _modal = Ui.Modal("ItemFilters", Root, Loc.Text("아이템 종류"), CloseModal, out var body);
            AddFilter(body, null);
            foreach (var group in ItemGroups.All) AddFilter(body, group);
        }

        private void AddFilter(RectTransform body, ItemGroup? group)
        {
            var button = Ui.Button("Filter_" + (group.HasValue ? group.Value.ToString() : "All"), body,
                group.HasValue ? ItemGroups.LabelOf(group.Value) : Loc.Text("전체"), () =>
                {
                    _filter = group;
                    CloseModal();
                    Refresh();
                    _scroll.StopMovement();
                    _scroll.verticalNormalizedPosition = 1f;
                }, _filter == group ? Theme.AccentDim : Theme.Panel);
            Ui.Size(button.gameObject, 86f);
        }

        private void OpenDetail(string itemId)
        {
            CloseModal();
            int have = Warehouse.CountOf(Session.Save.Warehouse, itemId);
            if (have <= 0) return;
            _scroll.StopMovement();
            _selected = itemId;
            _detailCount = have;
            var def = Session.Data.GetItem(itemId);
            _modal = Ui.Modal("ItemDetail", Root, ItemPresentation.Name(Session.Data, itemId), CloseModal, out var body);
            var iconHost = Ui.Rect("LargeIcon", body);
            Ui.Size(iconHost.gameObject, 210f);
            var icon = Ui.Icon("Icon", iconHost, def, 210f);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, .5f);
            icon.rectTransform.sizeDelta = new Vector2(210f, 210f);
            icon.rectTransform.anchoredPosition = Vector2.zero;
            DetailText(body, "Quantity", Loc.Text("보유 수량 {0}", have));
            string slot = PlayerEquipment.SlotFor(def);
            if (slot != null) {
                DetailText(body, "EquipmentSlot", Loc.Text("착용 부위 · {0}", PlayerLoadoutPanel.SlotLabel(slot)));
                string current = PlayerEquipment.Equipped(Session.Save, slot);
                DetailText(body, "CurrentEquipment", Loc.Text("현재 착용 · {0}", current == null ? Loc.Text("없음") : ItemPresentation.Name(Session.Data, current)));
                string reason = PlayerEquipment.EquipBlockReason(Session.Save, Session.Data, itemId);
                DetailText(body, "EquipmentComparison", RaidItemDescription.Describe(Session.Data, itemId, current));
                var wear = Ui.Button("WearItem", body, Loc.Text("내 캐릭터에 착용 · {0}", PlayerLoadoutPanel.SlotLabel(slot)), () => EquipItem(itemId), Theme.AccentDim, 28);
                Ui.Size(wear.gameObject, 96); wear.interactable = reason == null;
                if (reason != null) PlayerLoadoutPanel.Explain(body, "EquipmentReason", Loc.Text(reason), Theme.Warn);
            }
            string descKey = "ITEM_" + itemId + "_DESC";
            DetailText(body, "Description", Loc.Has(descKey) ? Loc.Get(descKey) : Loc.Text("등록된 설명이 없습니다."));
            string stats=ItemPresentation.Stats(Session.Data,itemId);
            if(!string.IsNullOrEmpty(stats)) DetailText(body,"CombatStats",stats);
            long unit = Market.SellPrice(Session.Save, Session.Data, itemId);
            DetailText(body, "Unit", Loc.Text(def.Category == "Ammo" ? "1발 판매가 {0}" : "개당 판매가 {0}", Theme.Won(unit)));
            if (def.Category == "Ammo") DetailText(body, "AmmoQuantity", Loc.Text("보유 {0}발 · 판매 합계 {1}", have, Theme.Won(unit * have)));
            var buttons = Ui.Rect("Sales", body);
            Ui.Size(buttons.gameObject, 96f);
            Ui.Row(buttons, 10f);
            Ui.Button("Sell1", buttons, Loc.Text(def.Category == "Ammo" ? "1발 판매" : "1개 판매"), () => Sell(itemId, 1), Theme.AccentDim, Theme.FontSmall);
            if (have >= 10) Ui.Button("Sell10", buttons, Loc.Text(def.Category == "Ammo" ? "10발 판매" : "10개 판매"), () => Sell(itemId, 10), Theme.AccentDim, Theme.FontSmall);
            Ui.Button("SellAll", buttons, Loc.Text("전부 판매 ({0})", have), () => Sell(itemId, have), Theme.Accent, Theme.FontSmall);
            foreach (Transform child in buttons) Ui.Size(child.gameObject, flexWidth: 1f);
            if (!Session.Save.Mail.Linked) return;
            string block = Outbox.ItemBlockReason(Session.Data.Transfer, def)
                ?? Outbox.BlockReason(Session.Save, Session.Data, new List<ItemStack> { new ItemStack(itemId, 1) }, Session.Clock.UtcNow);
            if (block != null)
            {
                DetailText(body, "ShipWhy", Loc.Text("본편 발송 불가 — ") + block);
                return;
            }
            var send = Ui.Button("Send", body, Loc.Text("본편으로 1개 발송  {0}", Theme.Won(def.BasePrice)),
                () => Send(itemId), Theme.Info, Theme.FontSmall);
            Ui.Size(send.gameObject, 96f);
        }

        private void OpenPlayerLoadout()
        {
            CloseModal();
            _modal=Ui.Modal("PlayerEquipment",Root,Loc.Text("내 장비"),CloseModal,out var body);
            DetailText(body,"LoadoutHint",Loc.Text("슬롯을 눌러 장비를 확인하고 교체하세요. 장착한 아이템은 창고에서 이동합니다."));
            if(ExplorationSystem.IsActive(Session.Save)) DetailText(body,"EquipBlocked",Loc.Text("탐색 중에는 장비를 변경할 수 없습니다."));
            foreach(string slot in PlayerEquipment.Slots) {
                string selected=slot;
                string id=PlayerEquipment.Equipped(Session.Save,slot);
                var button=Ui.Button("PlayerSlot_"+slot,body,ItemPresentation.SlotLabel(slot)+" · "+(id==null ? Loc.Text("비어 있음") : ItemPresentation.Name(Session.Data,id)),()=>OpenPlayerSlot(selected),Theme.PanelAlt,Theme.FontBody);
                Ui.Size(button.gameObject,84);
                if(id!=null) DetailText(body,"EquippedStats_"+slot,ItemPresentation.Stats(Session.Data,id));
            }
        }

        private void OpenPlayerSlot(string slot)
        {
            CloseModal();
            _modal=Ui.Modal("PlayerEquipmentPicker",Root,Loc.Text("내 장비")+" · "+ItemPresentation.SlotLabel(slot),OpenPlayerLoadout,out var body);
            bool editable=!ExplorationSystem.IsActive(Session.Save);
            string current=PlayerEquipment.Equipped(Session.Save,slot);
            if(current!=null) {
                DetailText(body,"EquippedName",Loc.Text("현재 장비")+" · "+ItemPresentation.Name(Session.Data,current));
                DetailText(body,"EquippedStats",ItemPresentation.Stats(Session.Data,current));
                var off=Ui.Button("UnequipPlayer",body,Loc.Text("해제하여 창고로"),()=>ChangePlayerEquipment(s=>PlayerEquipment.TryUnequip(s,Session.Data,slot)),Theme.Panel,Theme.FontBody);
                Ui.Size(off.gameObject,84); off.interactable=editable;
            }
            if(!editable) DetailText(body,"EquipBlocked",Loc.Text("탐색 중에는 장비를 변경할 수 없습니다."));
            var seen=new HashSet<string>();
            foreach(var stack in Session.Save.Warehouse.Stacks) {
                string id=stack.ItemId;
                if(stack.Count<=0 || !seen.Add(id) || PlayerEquipment.SlotFor(Session.Data.GetItem(id))!=slot) continue;
                var equip=Ui.Button("EquipPlayer_"+id,body,ItemPresentation.Name(Session.Data,id)+" · "+Loc.Text("착용"),()=>ChangePlayerEquipment(s=>PlayerEquipment.TryEquip(s,Session.Data,id)),Theme.AccentDim,Theme.FontBody);
                Ui.Size(equip.gameObject,84); equip.interactable=editable;
                DetailText(body,"CandidateStats_"+id,ItemPresentation.Stats(Session.Data,id));
            }
            if(!Session.Save.Warehouse.Stacks.Exists(x=>x.Count>0 && PlayerEquipment.SlotFor(Session.Data.GetItem(x.ItemId))==slot))
                DetailText(body,"NoEquipment",Loc.Text("이 슬롯에 장착할 아이템이 창고에 없습니다."));
        }

        private void ChangePlayerEquipment(System.Func<GameSave,bool> action)
        {
            try {
                if(!Session.ExecuteSavedAction(action)) { Shell.Toast(Loc.Text("장비를 변경할 수 없습니다. 탐색 상태와 창고 공간을 확인하세요.")); return; }
            } catch(System.Exception) { Shell.Toast(Loc.Text("저장하지 못했습니다. 다시 시도하세요.")); return; }
            CloseModal(); Shell.AfterAction(); OpenPlayerLoadout(); Sfx.Confirm();
        }
        private static void DetailText(RectTransform body, string name, string value)
        {
            var text = Ui.Paragraph(name, body, value, Theme.FontBody, Theme.TextDim);
            text.resizeTextForBestFit = false;
            // Text's preferred height is calculated from its actual width by the parent layout.
            // No fixed height: original descriptions remain readable and scroll in full.
            Ui.Size(text.gameObject, flexHeight: 0f);
        }

        private void OpenEquipment(string slot)
        {
            CloseModal();
            _modal = Ui.Modal("PlayerEquipmentDetail", Root, PlayerLoadoutPanel.SlotLabel(slot), CloseModal, out var body);
            PlayerLoadoutPanel.Choices(body, Session, slot, EquipItem, () => ChangeEquipment(slot, null));
        }

        private void EquipItem(string itemId) => ChangeEquipment(PlayerEquipment.SlotFor(Session.Data.GetItem(itemId)), itemId);

        private void ChangeEquipment(string slot, string itemId)
        {
            string reason = itemId == null ? PlayerEquipment.UnequipBlockReason(Session.Save, Session.Data, slot)
                : PlayerEquipment.EquipBlockReason(Session.Save, Session.Data, itemId);
            if (reason != null) { Shell.Toast(Loc.Text(reason)); OpenEquipment(slot); return; }
            try {
                if (!Session.ExecuteSavedAction(s => itemId == null ? PlayerEquipment.TryUnequip(s, Session.Data, slot) : PlayerEquipment.TryEquip(s, Session.Data, itemId))) {
                    Shell.Toast(Loc.Text("장비 상태가 바뀌었습니다. 다시 선택해 주세요."));
                    OpenEquipment(slot); return;
                }
            } catch (System.Exception) {
                Shell.Toast(Loc.Text("저장하지 못했습니다. 기존 장비를 유지합니다. 다시 시도해 주세요."), 4);
                OpenEquipment(slot); return;
            }
            CloseModal();
            Shell.AfterAction();
            OpenEquipment(slot);
            Shell.Toast(itemId == null ? Loc.Text("장비를 해제하고 창고에 보관했습니다.") : Loc.Text("{0} 착용 완료", ItemPresentation.Name(Session.Data, itemId)));
        }

        private void CloseModal()
        {
            _selected = null;
            _detailCount = 0;
            DestroyView(_modal);
            _modal = null;
        }

        private static void DestroyView(RectTransform view)
        {
            if (view == null) return;
            view.gameObject.SetActive(false);
            view.SetParent(null, false);
            if (Application.isPlaying) Object.Destroy(view.gameObject);
            else Object.DestroyImmediate(view.gameObject);
        }

        private void Send(string itemId)
        {
            var one = new List<ItemStack> { new ItemStack(itemId, 1) };
            MailShipment shipment;
            try { shipment = Session.QueueShipment(one); }
            catch (System.Exception)
            {
                Shell.Toast(AfterSeoul.Core.Loc.Text("저장하지 못했습니다. 물건은 창고에 남아 있습니다."), 4f);
                Shell.AfterAction();
                return;
            }

            if (shipment == null)
            {
                string reason = Outbox.BlockReason(
                    Session.Save, Session.Data, one, Session.Clock.UtcNow);
                Shell.Toast(reason ?? AfterSeoul.Core.Loc.Text("발송하지 못했습니다"), 3f);
                Shell.AfterAction();
                return;
            }

            Shell.Toast(AfterSeoul.Core.Loc.Text("{0} 발송함에 보관했습니다. 전송을 시도합니다.", ItemPresentation.Name(Session.Data, itemId)), 3.5f);
            (Session.MailLink as AfterSeoul.Mail.IAccountMailLink)?.Sync((ok, message) =>
            {
                Shell.Toast(message, 4f);
                Shell.AfterAction();
            });

            if (Warehouse.CountOf(Session.Save.Warehouse, itemId) == 0) CloseModal();
            Shell.AfterAction();
        }

        private void Sell(string itemId, int count)
        {
            long unit = Market.SellPrice(Session.Save, Session.Data, itemId);

            if (!Session.Sell(itemId, count))
            {
                Sfx.Error();
                Shell.Toast(AfterSeoul.Core.Loc.Text("{0} {1}개를 팔 수 없습니다", ItemPresentation.Name(Session.Data, itemId), count));
                return;
            }

            Sfx.Buy();
            Shell.Toast(AfterSeoul.Core.Loc.Text("{0} ×{1} 판매  +{2}", ItemPresentation.Name(Session.Data, itemId), count, Theme.Won(unit * count)));

            // 다 팔았으면 접는다. 사라진 줄이 펼쳐진 채로 남아 있으면 이상하다.
            if (Inventory.Warehouse.CountOf(Session.Save.Warehouse, itemId) == 0)
                CloseModal();

            Shell.AfterAction();
        }
    }
}
