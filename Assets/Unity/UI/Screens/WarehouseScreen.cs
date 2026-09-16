using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Mail;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI.Screens
{
    /// <summary>
    /// 창고. GDD §33 — <b>모든 주요 의사결정이 일어나는 장소</b>다.
    ///
    /// <para>[판매]와 [본편 발송]이 여기 있다. [장비 지급]은 인원 화면에서 하고
    /// (그 칸에 뭘 넣을지 고르는 자리가 거기다), [NPC 납품]은 홈 화면의 의뢰 줄에서 한다.
    /// 아직 안 되는 버튼을 회색으로 깔아두지 않는다 — 눌리지 않는 버튼은 버그처럼 보인다.</para>
    ///
    /// <para><b>본편 발송은 큐에 넣는 데까지</b>다. 실제 전송은 P5 지만 검증은 지금 전부 건다
    /// (<see cref="Outbox"/>).</para>
    /// </summary>
    public sealed class WarehouseScreen : ScreenBase
    {
        public override string TabName => "창고";
        public override IconSet.TabGlyph Glyph => IconSet.TabGlyph.Warehouse;

        private RectTransform _list;
        private Text _summary;

        /// <summary>펼쳐서 판매 버튼을 보여줄 아이템. null 이면 전부 접힘.</summary>
        private string _expanded;

        protected override void Build()
        {
            var col = Ui.Rect("Col", Root);
            Ui.Stretch(col, Theme.Gutter, Theme.Gutter, 16f, 16f);

            _summary = Ui.Label("Summary", col, "", Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            _summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            _summary.verticalOverflow = VerticalWrapMode.Truncate;
            _summary.resizeTextMinSize = 18;
            _summary.resizeTextMaxSize = _summary.fontSize;
            _summary.resizeTextForBestFit = true;
            Ui.Top(_summary.rectTransform, 78f);

            var listHost = Ui.Rect("ListHost", col);
            Ui.Stretch(listHost, 0f, 0f, 88f, 0f);
            _list = Ui.ScrollList("Scroll", listHost, out var scroll, 6f);
        }

        public override void Refresh()
        {
            if (_list == null) return;

            var save = Session.Save;
            var stacks = save.Warehouse.Stacks;

            long totalValue = 0;
            foreach (var s in stacks)
                totalValue += Market.SellPrice(Session.Save, Session.Data, s.ItemId) * s.Count;

            string line2;
            if (save.Mail.Linked)
            {
                long leftValue = Outbox.RemainingDailyValue(save, Session.Data, Session.Clock.UtcNow);
                int leftShipments = Outbox.RemainingShipments(save, Session.Data, Session.Clock.UtcNow);

                line2 = AfterSeoul.Core.Loc.Text("본편 발송 남은 한도 {0} · {1}회", Theme.Won(leftValue), leftShipments);

                int pending = 0;
                foreach (var s in save.Mail.Outbox) if (!s.Claimed) pending++;
                if (pending > 0) line2 += AfterSeoul.Core.Loc.Text("   ·   수령 대기 {0}건", pending);
            }
            else
            {
                // 연결 안 된 사람에게 한도를 보여줄 이유가 없다. 연동은 선택이다 (GDD §12).
                line2 = AfterSeoul.Core.Loc.Text("본편과 연결되지 않음 — 모바일만으로도 전부 즐길 수 있습니다");
            }

            _summary.text =
                AfterSeoul.Core.Loc.Text("{0} / {1} 칸   ·   판매가 합계 {2}\n", stacks.Count, save.Warehouse.TotalCapacity, Theme.Won(totalValue))
                + line2;

            Ui.Clear(_list);
            StarterGuide.Draw(_list, Session, Shell, TabName);

            if (stacks.Count == 0)
            {
                var empty = Ui.Label("Empty", _list,
                    AfterSeoul.Core.Loc.Text("창고가 비어 있습니다.\n공장에서 일하거나 탐색을 보내 물자를 모으세요."),
                    Theme.FontBody, TextAnchor.UpperLeft, Theme.TextFaint);
                empty.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Size(empty.gameObject, 160f);
                return;
            }

            // 비싼 것부터. 팔 것을 고르는 화면이라 가치 순이 맞다.
            var sorted = new List<ItemStack>(stacks);
            sorted.Sort((a, b) =>
            {
                long va = Market.SellPrice(Session.Save, Session.Data, a.ItemId) * a.Count;
                long vb = Market.SellPrice(Session.Save, Session.Data, b.ItemId) * b.Count;
                int c = vb.CompareTo(va);
                return c != 0 ? c : string.CompareOrdinal(a.ItemId, b.ItemId);
            });

            for (int i = 0; i < sorted.Count; i++)
                BuildRow(sorted[i], i);
        }

        private void BuildRow(ItemStack stack, int index)
        {
            bool open = _expanded == stack.ItemId;
            long unit = Market.SellPrice(Session.Save, Session.Data, stack.ItemId);

            // 바깥 카드: 세로. 위는 항상 보이는 요약 줄, 아래는 펼쳤을 때의 판매 버튼들.
            var card = Ui.Rect("Item_" + stack.ItemId, _list);
            var cardImg = card.gameObject.AddComponent<Image>();
            cardImg.color = index % 2 == 0 ? Theme.Panel : Theme.PanelAlt;
            cardImg.raycastTarget = false;
            Ui.Column(card, 0f);

            // ── 요약 줄 (누르면 펼침) ──
            string itemId = stack.ItemId;
            var head = Ui.Button("Head", card, "", () => Toggle(itemId),
                new Color(0f, 0f, 0f, 0f));
            Ui.Size(head.gameObject, Theme.RowHeight);
            // 기본 Button 은 targetGraphic 색을 바꾸는데, 투명 배경이라 눌린 티가 안 난다.
            // 카드 자체 색으로 구분되므로 전이 효과는 끈다.
            head.transition = Selectable.Transition.None;

            var headRow = Ui.Rect("HeadRow", head.transform);
            Ui.Stretch(headRow, 18f, 18f, 0f, 0f);
            Ui.Row(headRow, 10f);

            Ui.Icon("Icon", headRow, ItemGroups.Of(Session.Data.GetItem(itemId)));

            var name = Ui.Label("Name", headRow, Loc.ItemName(itemId), Theme.FontBody);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var count = Ui.Label("Count", headRow, "×" + stack.Count, Theme.FontBody,
                TextAnchor.MiddleRight, Theme.TextDim);
            Ui.Size(count.gameObject, width: 130f, flexWidth: 0f);

            var value = Ui.Label("Value", headRow, Theme.Won(unit * stack.Count), Theme.FontSmall,
                TextAnchor.MiddleRight, Theme.Accent);
            Ui.Size(value.gameObject, width: 250f, flexWidth: 0f);

            var chevron = Ui.Label("Chevron", headRow, open ? "▲" : "▼", Theme.FontSmall,
                TextAnchor.MiddleRight, Theme.TextFaint);
            Ui.Size(chevron.gameObject, width: 46f, flexWidth: 0f);

            if (!open) return;

            // ── 펼친 영역 ──
            var def = Session.Data.GetItem(itemId);
            string sendBlock = Outbox.ItemBlockReason(Session.Data.Transfer, def);

            var actions = Ui.Rect("Actions", card);
            Ui.Size(actions.gameObject, sendBlock == null ? 290f : 246f);
            Ui.Column(actions, 10f, new RectOffset(18, 18, 4, 16));

            var info = Ui.Label("Unit", actions, AfterSeoul.Core.Loc.Text("개당 판매가 {0}", Theme.Won(unit)), Theme.FontSmall,
                TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Size(info.gameObject, 44f);

            var buttons = Ui.Rect("Buttons", actions);
            Ui.Size(buttons.gameObject, 104f);
            Ui.Row(buttons, 12f);

            int have = stack.Count;
            Ui.Button("Sell1", buttons, AfterSeoul.Core.Loc.Text("1개 판매"), () => Sell(itemId, 1), Theme.AccentDim, Theme.FontSmall);

            if (have >= 10)
                Ui.Button("Sell10", buttons, AfterSeoul.Core.Loc.Text("10개 판매"), () => Sell(itemId, 10), Theme.AccentDim, Theme.FontSmall);

            Ui.Button("SellAll", buttons, AfterSeoul.Core.Loc.Text("전부 판매 ({0})", have), () => Sell(itemId, have),
                Theme.Accent, Theme.FontSmall);

            // ── 본편 발송 ──
            // 연결 전에는 이 구역을 통째로 만들지 않는다 (LINK_CONTRACT §5-1). 연동하지 않은
            // 사람에게 "본편 발송 불가"가 아이템마다 붙으면, 하지도 않은 선택을 계속 거절당하는
            // 화면이 된다 — 연동은 선택이지 미완료 과제가 아니다 (GDD §12).
            if (!Session.Save.Mail.Linked) return;

            // 보낼 수 없는 물건이면 버튼을 만들지 않고 이유만 적는다.
            if (sendBlock != null)
            {
                var why = Ui.Label("NoSend", actions, AfterSeoul.Core.Loc.Text("본편 발송 불가 — ") + sendBlock,
                    Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextFaint);
                why.horizontalOverflow = HorizontalWrapMode.Wrap;
                why.verticalOverflow = VerticalWrapMode.Truncate;
                why.resizeTextMinSize = 18;
                why.resizeTextMaxSize = why.fontSize;
                why.resizeTextForBestFit = true;
                Ui.Size(why.gameObject, 44f);
                return;
            }

            var shipRow = Ui.Rect("Ship", actions);
            Ui.Size(shipRow.gameObject, 104f);
            Ui.Row(shipRow, 12f);

            var one = new List<ItemStack> { new ItemStack(itemId, 1) };
            string shipBlock = Outbox.BlockReason(
                Session.Save, Session.Data, one, Session.Clock.UtcNow);

            var send = Ui.Button("Send", shipRow,
                shipBlock == null ? AfterSeoul.Core.Loc.Text("본편으로 1개 발송  {0}", Theme.Won(def.BasePrice)) : AfterSeoul.Core.Loc.Text("본편 발송 불가"),
                () => Send(itemId), shipBlock == null ? Theme.Info : Theme.Line, Theme.FontSmall);
            send.interactable = shipBlock == null;

            if (shipBlock != null)
            {
                var why = Ui.Label("ShipWhy", actions, shipBlock, Theme.FontSmall,
                    TextAnchor.MiddleLeft, Theme.Warn);
                why.horizontalOverflow = HorizontalWrapMode.Wrap;
                why.verticalOverflow = VerticalWrapMode.Truncate;
                why.resizeTextMinSize = 18;
                why.resizeTextMaxSize = why.fontSize;
                why.resizeTextForBestFit = true;
                Ui.Size(why.gameObject, 44f);
            }
        }

        private void Toggle(string itemId)
        {
            _expanded = _expanded == itemId ? null : itemId;
            Refresh();
        }

        /// <summary>
        /// 본편 발송함에 1개 넣는다.
        ///
        /// <para>여러 개를 한 번에 담는 화면은 만들지 않았다 — 1회 한도가 25,000원이라
        /// 실제로 담을 수 있는 게 몇 개 안 되고, 담기 화면을 만들면 그 안에서 또
        /// 한도 계산을 보여줘야 한다. 한 개씩 보내는 것으로 충분하다.</para>
        /// </summary>
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

            Shell.Toast(AfterSeoul.Core.Loc.Text("{0} 발송함에 보관했습니다. 전송을 시도합니다.", Loc.ItemName(itemId)), 3.5f);
            (Session.MailLink as AfterSeoul.Mail.IAccountMailLink)?.Sync((ok, message) =>
            {
                Shell.Toast(message, 4f);
                Shell.AfterAction();
            });

            if (Warehouse.CountOf(Session.Save.Warehouse, itemId) == 0) _expanded = null;
            Shell.AfterAction();
        }

        private void Sell(string itemId, int count)
        {
            long unit = Market.SellPrice(Session.Save, Session.Data, itemId);

            if (!Session.Sell(itemId, count))
            {
                Sfx.Error();
                Shell.Toast(AfterSeoul.Core.Loc.Text("{0} {1}개를 팔 수 없습니다", Loc.ItemName(itemId), count));
                return;
            }

            Sfx.Buy();
            Shell.Toast(AfterSeoul.Core.Loc.Text("{0} ×{1} 판매  +{2}", Loc.ItemName(itemId), count, Theme.Won(unit * count)));

            // 다 팔았으면 접는다. 사라진 줄이 펼쳐진 채로 남아 있으면 이상하다.
            if (Inventory.Warehouse.CountOf(Session.Save.Warehouse, itemId) == 0)
                _expanded = null;

            Shell.AfterAction();
        }
    }
}
