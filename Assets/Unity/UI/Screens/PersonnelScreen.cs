using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Scav;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI.Screens
{
    /// <summary>
    /// 인원. GDD §32 — 고용 시장과 스캐브 목록.
    ///
    /// <para>고용 시장을 <b>위</b>에, 보유 인원을 <b>아래</b>에 둔다. 인원이 0명일 때
    /// 제일 먼저 보여야 하는 건 "아무도 없습니다"가 아니라 지금 살 수 있는 사람이다.</para>
    ///
    /// <para>누적 이력(파견 횟수·회수 총액)은 처음부터 보여준다 — GDD §15 의 애착은
    /// 이름이 아니라 같이 보낸 시간에서 나오고, 그 기록은 처음부터 쌓여야 한다.</para>
    /// </summary>
    public sealed class PersonnelScreen : ScreenBase
    {
        public override string TabName => "인원";
        public override IconSet.TabGlyph Glyph => IconSet.TabGlyph.People;

        private RectTransform _list;

        /// <summary>치료 중인 사람의 살아 있는 줄들. 회복도 파견처럼 시간이 흐르는 일이다.</summary>
        private readonly System.Collections.Generic.List<CareRow> _careRows =
            new System.Collections.Generic.List<CareRow>();

        private sealed class CareRow
        {
            public ScavState Scav;
            public Text Text;
            public ProgressBar Bar;
            public bool MarkedDone;
        }

        protected override void Build()
        {
            var host = Ui.Rect("Host", Root);
            Ui.Stretch(host, Theme.Gutter, Theme.Gutter, 16f, 16f);
            _list = Ui.ScrollList("Scroll", host, out var scroll, 12f);
        }

        public override void Refresh()
        {
            if (_list == null) return;
            Ui.Clear(_list);
            _careRows.Clear();

            BuildMarket();
            BuildRoster();
        }

        public override void Tick(float deltaTime) => TickCare();

        /// <summary>매 프레임. 회복까지 남은 시간과 막대를 민다.</summary>
        private void TickCare()
        {
            if (_careRows.Count == 0) return;

            var now = Session.Clock.UtcNow;

            foreach (var row in _careRows)
            {
                if (row.Bar == null || !row.Bar.Alive || row.Text == null) continue;

                row.Bar.Set((float)Treatment.Progress(row.Scav, now));

                var left = row.Scav.RecoversAt - now;
                if (left.TotalSeconds > 0)
                {
                    row.Text.text = left.TotalHours >= 1
                        ? AfterSeoul.Core.Loc.Text("치료 중 — 회복까지 {0}시간 {1}분", (int)left.TotalHours, left.Minutes)
                        : AfterSeoul.Core.Loc.Text("치료 중 — 회복까지 {0}분 {1}초", left.Minutes, left.Seconds);
                    continue;
                }

                if (row.MarkedDone) continue;
                row.MarkedDone = true;

                row.Text.text = AfterSeoul.Core.Loc.Text("회복 완료 — 곧 복귀합니다");
                row.Text.color = Theme.Safe;
                row.Bar.SetColor(Theme.Safe);
                row.Bar.SetPulsing(true);
            }
        }

        // ── 고용 시장 ────────────────────────────────────────────

        private void BuildMarket()
        {
            var market = Session.Save.Market;

            int open = 0;
            foreach (var o in market.Offers) if (!o.Hired) open++;

            var header = Ui.Label("MarketHead", _list,
                AfterSeoul.Core.Loc.Text("고용 시장   ·   {0}명 대기", open), Theme.FontHeading,
                TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Size(header.gameObject, 56f);

            if (open == 0)
            {
                var none = Ui.Paragraph("MarketEmpty", _list,
                    market.Offers.Count == 0
                        ? AfterSeoul.Core.Loc.Text("오늘은 찾아온 사람이 없습니다. 새벽 5시에 새 후보가 옵니다.")
                        : AfterSeoul.Core.Loc.Text("오늘 온 사람은 모두 고용했습니다. 새벽 5시에 새 후보가 옵니다."),
                    Theme.FontSmall, Theme.TextFaint);
                none.horizontalOverflow = HorizontalWrapMode.Wrap;
                none.verticalOverflow = VerticalWrapMode.Truncate;
                none.resizeTextMinSize = 18;
                none.resizeTextMaxSize = none.fontSize;
                none.resizeTextForBestFit = true;
                Ui.Size(none.gameObject, 64f);
            }
            else
            {
                foreach (var offer in market.Offers)
                    if (!offer.Hired) BuildOfferCard(offer);
            }

            // 시장과 보유 인원 사이 여백
            var gap = Ui.Rect("Gap", _list);
            Ui.Size(gap.gameObject, 20f);
        }

        private void BuildOfferCard(ScavOffer offer)
        {
            var card = Ui.Rect("Offer_" + offer.OfferId, _list);
            var img = card.gameObject.AddComponent<Image>();
            img.color = Theme.PanelAlt;
            img.raycastTarget = false;
            Ui.Column(card, 6f, new RectOffset(20, 20, 14, 16));

            // 이름 + 티어
            var head = Ui.Rect("Head", card);
            Ui.Size(head.gameObject, 52f);
            Ui.Row(head, 8f);

            var name = Ui.Label("Name", head, AfterSeoul.Core.Loc.Text(offer.Name), Theme.FontHeading);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var tier = Ui.Label("Tier", head, TierLabel(offer.Tier), Theme.FontSmall,
                TextAnchor.MiddleRight, TierColor(offer.Tier));
            tier.horizontalOverflow = HorizontalWrapMode.Wrap;
            tier.verticalOverflow = VerticalWrapMode.Truncate;
            tier.resizeTextMinSize = 18;
            tier.resizeTextMaxSize = tier.fontSize;
            tier.resizeTextForBestFit = true;
            Ui.Size(tier.gameObject, width: 260f, flexWidth: 0f);

            // 능력치 — 특기를 같이 적는다. 숫자 셋만 늘어놓으면 후보끼리 비교가 안 된다.
            var stats = Ui.Label("Stats", card, StatLine(offer),
                Theme.FontBody, TextAnchor.MiddleLeft, Theme.Text);
            stats.horizontalOverflow = HorizontalWrapMode.Wrap;
            stats.verticalOverflow = VerticalWrapMode.Truncate;
            stats.resizeTextMinSize = 18;
            stats.resizeTextMaxSize = stats.fontSize;
            stats.resizeTextForBestFit = true;
            Ui.Size(stats.gameObject, 46f);

            // 고용 시장에서는 특히 중요하다 — 돈을 내기 전에 무엇을 사는지 알아야 한다.
            foreach (var traitId in offer.TraitIds)
            {
                var def = Traits.Find(Session.Data.ScavPool, traitId);
                string what = TraitNames.DescribeOf(def);

                var line = Ui.Label("Trait_" + traitId, card,
                    what.Length == 0
                        ? AfterSeoul.Core.Loc.Text("특성  ") + TraitNames.Of(traitId)
                        : AfterSeoul.Core.Loc.Text("특성  {0} — {1}", TraitNames.Of(traitId), what),
                    Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Info);
                line.horizontalOverflow = HorizontalWrapMode.Wrap;
                line.verticalOverflow = VerticalWrapMode.Truncate;
                line.resizeTextMinSize = 18;
                line.resizeTextMaxSize = line.fontSize;
                line.resizeTextForBestFit = true;
                Ui.Size(line.gameObject, 38f);
            }

            // 비용 + 고용 버튼
            var foot = Ui.Rect("Foot", card);
            Ui.Size(foot.gameObject, 88f);
            Ui.Row(foot, 12f);

            var cost = Ui.Label("Cost", foot,
                AfterSeoul.Core.Loc.Text("계약금 {0}\n시급 {1}", Theme.Won(offer.HireCost), Theme.Won(offer.WagePerHour)),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            cost.horizontalOverflow = HorizontalWrapMode.Wrap;
            cost.verticalOverflow = VerticalWrapMode.Truncate;
            cost.resizeTextMinSize = 18;
            cost.resizeTextMaxSize = cost.fontSize;
            cost.resizeTextForBestFit = true;
            Ui.Size(cost.gameObject, flexWidth: 1f);

            string block = ScavMarket.HireBlockReason(Session.Save, offer);
            string offerId = offer.OfferId;
            var btn = Ui.Button("Hire", foot, AfterSeoul.Core.Loc.Text("고 용"), () => OnHire(offerId),
                block == null ? Theme.Accent : Theme.Line);
            btn.interactable = block == null;
            Ui.Size(btn.gameObject, width: 250f, flexWidth: 0f);

            if (block != null)
            {
                var why = Ui.Label("Why", card, block, Theme.FontSmall,
                    TextAnchor.MiddleLeft, Theme.Warn);
                why.horizontalOverflow = HorizontalWrapMode.Wrap;
                why.verticalOverflow = VerticalWrapMode.Truncate;
                why.resizeTextMinSize = 18;
                why.resizeTextMaxSize = why.fontSize;
                why.resizeTextForBestFit = true;
                Ui.Size(why.gameObject, 38f);
            }
        }

        private void OnHire(string offerId)
        {
            // Session.Hire 는 먼저 정산을 돌린다. 화면을 그린 뒤 새벽 5시를 넘겼다면
            // 이 후보는 이미 사라져 있고 TryHire 가 null 을 준다. 사유를 삼키지 않는다.
            var scav = Session.Hire(offerId);
            if (scav == null)
            {
                string reason = ScavMarket.HireBlockReason(Session.Save, FindOffer(offerId));
                Shell.Toast(reason ?? AfterSeoul.Core.Loc.Text("고용하지 못했습니다"), 3f);
                Shell.AfterAction();
                return;
            }

            Shell.Toast(AfterSeoul.Core.Loc.Text("{0} 고용 — 탐색 화면에서 파견할 수 있습니다", AfterSeoul.Core.Loc.Text(scav.Name)), 3.5f);
            Shell.AfterAction();
        }

        private ScavOffer FindOffer(string offerId)
        {
            foreach (var o in Session.Save.Market.Offers)
                if (o.OfferId == offerId) return o;
            return null;
        }

        // ── 보유 인원 ────────────────────────────────────────────

        private void BuildRoster()
        {
            var scavs = Session.Save.Scavs;

            var header = Ui.Label("RosterHead", _list,
                AfterSeoul.Core.Loc.Text("보유 인원   ·   {0}명", scavs.Count), Theme.FontHeading,
                TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Size(header.gameObject, 56f);

            if (scavs.Count == 0)
            {
                var text = Ui.Paragraph("Empty", _list,
                    AfterSeoul.Core.Loc.Text("아직 고용한 스캐브가 없습니다.\n")
                    + AfterSeoul.Core.Loc.Text("공장에서 직접 일해 계약금을 모으면 첫 스캐브를 고용할 수 있습니다."),
                    Theme.FontSmall, Theme.TextDim);
                Ui.Size(text.gameObject, 96f);
                return;
            }

            for (int i = 0; i < scavs.Count; i++)
                BuildScavCard(scavs[i]);
        }

        private void BuildScavCard(ScavState scav)
        {
            var card = Ui.Surface("Scav_" + scav.Uid, _list, Theme.Panel,
                scav.Status == ScavStatus.Idle ? Theme.EdgeLive : Theme.Edge);
            Ui.Column(card, 6f, new RectOffset(20, 20, 14, 16));

            // 이름 + 상태
            var head = Ui.Rect("Head", card);
            Ui.Size(head.gameObject, 52f);
            Ui.Row(head, 8f);

            var name = Ui.Label("Name", head, $"{AfterSeoul.Core.Loc.Text(scav.Name)}   Lv.{scav.Level}", Theme.FontHeading);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var status = Ui.Label("Status", head, StatusLabel(scav.Status), Theme.FontSmall,
                TextAnchor.MiddleRight, StatusColor(scav.Status));
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Truncate;
            status.resizeTextMinSize = 18;
            status.resizeTextMaxSize = status.fontSize;
            status.resizeTextForBestFit = true;
            Ui.Size(status.gameObject, width: 240f, flexWidth: 0f);

            // 능력치 3종
            var stats = Ui.Label("Stats", card,
                AfterSeoul.Core.Loc.Text("탐색 {0}    전투 {1}    생존 {2}", scav.Search, scav.Combat, scav.Survival),
                Theme.FontBody, TextAnchor.MiddleLeft, Theme.Text);
            stats.horizontalOverflow = HorizontalWrapMode.Wrap;
            stats.verticalOverflow = VerticalWrapMode.Truncate;
            stats.resizeTextMinSize = 18;
            stats.resizeTextMaxSize = stats.fontSize;
            stats.resizeTextForBestFit = true;
            Ui.Size(stats.gameObject, 46f);

            // 특성. 이름만 적으면 무슨 뜻인지 알 길이 없어서 사람을 고르는 근거가 되지 못한다.
            foreach (var traitId in scav.TraitIds)
            {
                var def = Traits.Find(Session.Data.ScavPool, traitId);
                string what = TraitNames.DescribeOf(def);

                var line = Ui.Label("Trait_" + traitId, card,
                    what.Length == 0
                        ? AfterSeoul.Core.Loc.Text("특성  ") + TraitNames.Of(traitId)
                        : AfterSeoul.Core.Loc.Text("특성  {0} — {1}", TraitNames.Of(traitId), what),
                    Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Info);
                line.horizontalOverflow = HorizontalWrapMode.Wrap;
                line.verticalOverflow = VerticalWrapMode.Truncate;
                line.resizeTextMinSize = 18;
                line.resizeTextMaxSize = line.fontSize;
                line.resizeTextForBestFit = true;
                Ui.Size(line.gameObject, 38f);
            }

            // 누적 이력 — 애착의 근거.
            //
            // 함께한 기간이 여기 빠져 있었다. 고용 시각(HiredAt)은 처음부터 세이브에 적히고
            // 있었는데 읽는 곳이 없었다. 횟수와 금액만 있으면 그건 실적표지 이력이 아니다.
            // 옛 세이브에는 이 값이 아예 없다 — 그대로 빼면 0001년이 되어 "함께 739000일"이 뜬다.
            int days = scav.HiredAt == default(System.DateTimeOffset)
                ? 0
                : (int)(Session.Clock.UtcNow - scav.HiredAt).TotalDays;
            var history = Ui.Label("History", card,
                AfterSeoul.Core.Loc.Text("함께 {0}   ·   ", (days < 1 ? AfterSeoul.Core.Loc.Text("오늘") : days + AfterSeoul.Core.Loc.Text("일"))) +
                AfterSeoul.Core.Loc.Text("파견 {0}회   ·   회수 {1}", scav.ExpeditionCount, Theme.Won(scav.TotalLootValue)),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextFaint);
            history.horizontalOverflow = HorizontalWrapMode.Wrap;
            history.verticalOverflow = VerticalWrapMode.Truncate;
            history.resizeTextMinSize = 18;
            history.resizeTextMaxSize = history.fontSize;
            history.resizeTextForBestFit = true;
            Ui.Size(history.gameObject, 40f);

            // 장비 요약 + 편성 버튼
            var gearEffects = Equipment.EffectsOf(scav, Session.Data);
            var foot = Ui.Rect("Gear", card);
            Ui.Size(foot.gameObject, 74f);
            Ui.Row(foot, 12f);

            var summary = Ui.Label("GearText", foot,
                EquipText.LoadoutSummary(scav, Session.Data), Theme.FontSmall,
                TextAnchor.MiddleLeft, gearEffects.HasWeapon ? Theme.TextDim : Theme.Warn);
            summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            summary.verticalOverflow = VerticalWrapMode.Truncate;
            summary.resizeTextMinSize = 18;
            summary.resizeTextMaxSize = summary.fontSize;
            summary.resizeTextForBestFit = true;
            Ui.Size(summary.gameObject, flexWidth: 1f);

            string uid = scav.Uid;
            var btn = Ui.Button("Loadout", foot, AfterSeoul.Core.Loc.Text("장비"), () => OpenLoadout(uid),
                gearEffects.HasWeapon ? Theme.Line : Theme.Accent, Theme.FontSmall);
            Ui.Size(btn.gameObject, width: 180f, flexWidth: 0f);

            BuildTreatmentRow(card, scav);
        }

        /// <summary>
        /// 부상·치료 줄 (GDD §15).
        ///
        /// <para><b>이 줄이 없던 동안 부상은 영구 퇴출이었다.</b> 상태에 "부상"이라고 적히기만 하고
        /// 거기서 나올 방법이 게임 어디에도 없었다 — 명단은 시간이 갈수록 줄기만 했고, 사고를
        /// 피하는 유일한 길은 파견을 안 보내는 것이었다.</para>
        ///
        /// <para>값을 먼저 보여주고 누르게 한다. 비용과 시간을 모르는 채로 누르는 버튼은
        /// 선택이 아니다.</para>
        /// </summary>
        private void BuildTreatmentRow(RectTransform card, ScavState scav)
        {
            if (scav.Status != ScavStatus.Injured && scav.Status != ScavStatus.Treating) return;

            var row = Ui.Rect("Care", card);
            Ui.Size(row.gameObject, 74f);
            Ui.Row(row, 12f);

            if (scav.Status == ScavStatus.Treating)
            {
                var text = Ui.Label("CareText", row, "", Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Warn);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.resizeTextMinSize = 18;
                text.resizeTextMaxSize = text.fontSize;
                text.resizeTextForBestFit = true;
                Ui.Size(text.gameObject, flexWidth: 1f);

                var bar = Ui.Bar(row, 8f, Theme.Warn);
                Ui.Size(bar.Root.gameObject, 8f, width: 220f, flexWidth: 0f);

                _careRows.Add(new CareRow { Scav = scav, Text = text, Bar = bar });
                TickCare();
                return;
            }

            long cost = Treatment.CostFor(scav, Session.Data);
            string supplyId = Treatment.FindSupplies(Session.Save, Session.Data);
            var duration = Treatment.DurationFor(scav, Session.Data, supplyId != null);

            string label = supplyId != null
                ? AfterSeoul.Core.Loc.Text("치료 {0} · {1:0.#}시간 ({2} 1개 사용)", Theme.Won(cost), duration.TotalHours, Loc.ItemName(supplyId))
                : AfterSeoul.Core.Loc.Text("치료 {0} · {1:0.#}시간", Theme.Won(cost), duration.TotalHours);

            var info = Ui.Label("CareText", row, label, Theme.FontSmall,
                TextAnchor.MiddleLeft, Theme.TextDim);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            info.verticalOverflow = VerticalWrapMode.Truncate;
            info.resizeTextMinSize = 18;
            info.resizeTextMaxSize = info.fontSize;
            info.resizeTextForBestFit = true;
            Ui.Size(info.gameObject, flexWidth: 1f);

            string uid = scav.Uid;
            string blocked = Session.TreatBlockReason(uid);

            var care = Ui.Button("Treat", row, AfterSeoul.Core.Loc.Text("치료"), () => Treat(uid),
                blocked == null ? Theme.Accent : Theme.Line, Theme.FontSmall);
            Ui.Size(care.gameObject, width: 180f, flexWidth: 0f);
            care.interactable = blocked == null;
        }

        private void Treat(string uid)
        {
            // 막힌 이유를 먼저 확인해서 말해준다. 버튼이 조용히 안 먹는 것이 제일 나쁘다.
            string blocked = Session.TreatBlockReason(uid);
            if (blocked != null) { Shell.Toast(blocked, 3f); return; }

            if (!Session.TreatScav(uid)) { Shell.Toast(AfterSeoul.Core.Loc.Text("치료를 시작하지 못했습니다"), 3f); return; }

            Sfx.Complete();
            Shell.Toast(AfterSeoul.Core.Loc.Text("치료를 시작했습니다 — 자는 동안에도 회복합니다"), 3.5f);
            Shell.AfterAction();
        }

        // ── 장비 편성 창 ─────────────────────────────────────────

        /// <summary>열려 있는 창. 창을 Root 에 붙이므로 목록을 다시 그려도 살아남는다.</summary>
        private RectTransform _modal;

        private void CloseModal()
        {
            if (_modal == null) return;
            // Destroy 는 프레임 끝까지 미뤄진다. 바로 다음 창을 여는 경우(고른 뒤 목록으로 복귀)
            // 이번 프레임에 두 창이 겹쳐 보이므로 먼저 꺼둔다.
            _modal.gameObject.SetActive(false);
            Object.Destroy(_modal.gameObject);
            _modal = null;
        }

        /// <summary>스캐브 한 명의 장비 6칸. 칸을 누르면 창고에서 고른다.</summary>
        private void OpenLoadout(string uid)
        {
            CloseModal();

            var scav = FindScav(uid);
            if (scav == null) return;

            RectTransform body;
            _modal = Ui.Modal("Loadout", Root, AfterSeoul.Core.Loc.Text("{0} — 장비", AfterSeoul.Core.Loc.Text(scav.Name)), CloseModal, out body);

            foreach (var slot in EquipSlot.All)
            {
                string itemId;
                scav.Equipment.TryGetValue(slot, out itemId);
                BuildSlotRow(body, scav, slot, itemId);
            }

            var note = Ui.Paragraph("Note", body,
                AfterSeoul.Core.Loc.Text("무기는 필수입니다. 나머지 다섯 칸은 비워도 파견할 수 있습니다.\n")
                + AfterSeoul.Core.Loc.Text("실종·사망하면 착용한 장비를 같이 잃습니다."),
                Theme.FontSmall, Theme.TextFaint);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            note.verticalOverflow = VerticalWrapMode.Truncate;
            note.resizeTextMinSize = 18;
            note.resizeTextMaxSize = note.fontSize;
            note.resizeTextForBestFit = true;
            Ui.Size(note.gameObject, 92f);
        }

        private void BuildSlotRow(RectTransform parent, ScavState scav, string slot, string itemId)
        {
            bool empty = string.IsNullOrEmpty(itemId);
            var def = empty ? null : Session.Data.GetItem(itemId);
            bool isWeapon = slot == EquipSlot.Weapon;

            string uid = scav.Uid;
            string slotKey = slot;

            var btn = Ui.Button("Slot_" + slot, parent, "", () => OpenPicker(uid, slotKey),
                empty && isWeapon ? Theme.AccentDim : Theme.Panel);
            Ui.Size(btn.gameObject, 108f);

            var row = Ui.Rect("Content", btn.transform);
            Ui.Stretch(row, 20f, 20f, 10f, 10f);
            Ui.Column(row, 2f);

            var head = Ui.Rect("Head", row);
            Ui.Size(head.gameObject, 46f);
            Ui.Row(head, 10f);

            var label = Ui.Label("Slot", head, EquipSlot.LabelOf(slot), Theme.FontSmall,
                TextAnchor.MiddleLeft, Theme.TextFaint);
            Ui.Size(label.gameObject, width: 150f, flexWidth: 0f);

            var name = Ui.Label("Item", head,
                empty ? (isWeapon ? AfterSeoul.Core.Loc.Text("비어 있음 — 필수") : AfterSeoul.Core.Loc.Text("비어 있음")) : Loc.ItemName(itemId),
                Theme.FontBody, TextAnchor.MiddleLeft,
                empty ? (isWeapon ? Theme.Warn : Theme.TextFaint) : Theme.Text);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var effect = Ui.Label("Effect", row,
                def != null ? EquipText.Summary(def, Session.Data) : "",
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Size(effect.gameObject, 38f);
        }

        /// <summary>창고에서 그 칸에 맞는 물건을 고른다.</summary>
        private void OpenPicker(string uid, string slot)
        {
            CloseModal();

            var scav = FindScav(uid);
            if (scav == null) return;

            RectTransform body;
            _modal = Ui.Modal("Picker", Root,
                $"{AfterSeoul.Core.Loc.Text(scav.Name)} — {EquipSlot.LabelOf(slot)}", () => OpenLoadout(uid), out body);

            // 지금 착용 중인 것을 벗기는 줄
            string worn;
            if (scav.Equipment.TryGetValue(slot, out worn) && !string.IsNullOrEmpty(worn))
            {
                var off = Ui.Button("Unequip", body, AfterSeoul.Core.Loc.Text("해제 — {0}", Loc.ItemName(worn)),
                    () => DoUnequip(uid, slot), Theme.Line, Theme.FontSmall);
                Ui.Size(off.gameObject, 84f);
            }

            int found = 0;
            foreach (var stack in Warehouse.Snapshot(Session.Save.Warehouse))
            {
                var def = Session.Data.GetItem(stack.ItemId);
                if (def == null || !def.Equippable || def.EquipSlot != slot) continue;

                BuildPickerRow(body, uid, def, stack.Count);
                found++;
            }

            if (found == 0)
            {
                var none = Ui.Label("Empty", body,
                    AfterSeoul.Core.Loc.Text("창고에 {0} 이(가) 없습니다.", EquipSlot.LabelOf(slot)),
                    Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
                Ui.Size(none.gameObject, 52f);
            }

            BuildShopSection(body, uid, slot);
        }

        /// <summary>
        /// 창고 목록 아래에 상점을 붙인다.
        ///
        /// <para>화면을 따로 만들지 않는 이유: "이 칸에 넣을 게 없다"를 깨닫는 자리가 바로 여기다.
        /// 여기서 상점 화면으로 보내면 무엇을 사러 갔는지 잊는다.</para>
        /// </summary>
        private void BuildShopSection(RectTransform parent, string uid, string slot)
        {
            var offers = Shop.OffersFor(Session.Save, Session.Data, slot);

            var head = Ui.Label("ShopHead", parent,
                AfterSeoul.Core.Loc.Text("상점 — 황 상사 중개 (신뢰도 {0})", Shop.TrustOfEmployer(Session.Save)),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Size(head.gameObject, 58f);

            foreach (var offer in offers)
            {
                var def = Session.Data.GetItem(offer.ItemId);
                if (def == null) continue;
                BuildShopRow(parent, uid, def, offer);
            }

            // 다음에 열릴 상인을 알려준다. "더 좋은 물건이 없다"와
            // "아직 못 구한다"는 플레이어에게 전혀 다른 말이다.
            var next = Shop.NextLockedTrader(Session.Save, Session.Data);
            if (next != null)
            {
                int need = next.RequiresTrust - Shop.TrustOfEmployer(Session.Save);
                var note = Ui.Paragraph("Locked", parent,
                    offers.Count == 0
                        ? AfterSeoul.Core.Loc.Text("황 상사가 아직 이 물건을 대주지 않습니다. 신뢰도 {0} 더 필요합니다.\n의뢰를 납품하면 오릅니다.", need)
                        : AfterSeoul.Core.Loc.Text("신뢰도 {0} 더 쌓으면 더 좋은 물건이 들어옵니다.", need),
                    Theme.FontSmall, Theme.TextFaint);
                note.horizontalOverflow = HorizontalWrapMode.Wrap;
                note.verticalOverflow = VerticalWrapMode.Truncate;
                note.resizeTextMinSize = 18;
                note.resizeTextMaxSize = note.fontSize;
                note.resizeTextForBestFit = true;
                Ui.Size(note.gameObject, offers.Count == 0 ? 92f : 56f);
            }
        }

        private void BuildShopRow(RectTransform parent, string uid, ItemDef def, ShopOffer offer)
        {
            string itemId = def.Id;
            bool affordable = Session.Save.Player.Money >= offer.Price;

            var btn = Ui.Button("Buy_" + itemId, parent, "", () => DoBuy(uid, itemId),
                affordable ? Theme.Panel : Theme.Line);
            btn.interactable = affordable;
            Ui.Size(btn.gameObject, 108f);

            var col = Ui.Rect("Content", btn.transform);
            Ui.Stretch(col, 20f, 20f, 10f, 10f);
            Ui.Column(col, 2f);

            var head = Ui.Rect("Head", col);
            Ui.Size(head.gameObject, 46f);
            Ui.Row(head, 10f);

            var name = Ui.Label("Name", head, Loc.ItemName(itemId), Theme.FontBody,
                TextAnchor.MiddleLeft, affordable ? Theme.Text : Theme.TextFaint);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var price = Ui.Label("Price", head, AfterSeoul.Core.Loc.Text("구매 ") + Theme.Won(offer.Price), Theme.FontSmall,
                TextAnchor.MiddleRight, affordable ? Theme.Accent : Theme.Warn);
            price.horizontalOverflow = HorizontalWrapMode.Wrap;
            price.verticalOverflow = VerticalWrapMode.Truncate;
            price.resizeTextMinSize = 18;
            price.resizeTextMaxSize = price.fontSize;
            price.resizeTextForBestFit = true;
            Ui.Size(price.gameObject, width: 320f, flexWidth: 0f);

            var effect = Ui.Label("Effect", col, EquipText.Summary(def, Session.Data),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Size(effect.gameObject, 38f);
        }

        private void DoBuy(string uid, string itemId)
        {
            if (!Session.Buy(itemId))
            {
                string reason = Shop.BuyBlockReason(Session.Save, Session.Data, itemId);
                Shell.Toast(reason ?? AfterSeoul.Core.Loc.Text("구매하지 못했습니다"), 3f);
                Shell.AfterAction();
                return;
            }

            // 사자마자 바로 입힌다. 사 놓고 다시 눌러 지급하게 하면 한 번 더 헤매게 된다.
            Session.Equip(uid, itemId);
            Shell.Toast(AfterSeoul.Core.Loc.Text("{0} 구매 — 바로 지급했습니다", Loc.ItemName(itemId)), 3f);
            Shell.AfterAction();
            OpenLoadout(uid);
        }

        private void BuildPickerRow(RectTransform parent, string uid, ItemDef def, int count)
        {
            string itemId = def.Id;

            var btn = Ui.Button("Pick_" + itemId, parent, "", () => DoEquip(uid, itemId), Theme.PanelAlt);
            Ui.Size(btn.gameObject, 108f);

            var col = Ui.Rect("Content", btn.transform);
            Ui.Stretch(col, 20f, 20f, 10f, 10f);
            Ui.Column(col, 2f);

            var head = Ui.Rect("Head", col);
            Ui.Size(head.gameObject, 46f);
            Ui.Row(head, 10f);

            var name = Ui.Label("Name", head, Loc.ItemName(itemId), Theme.FontBody);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var right = Ui.Label("Count", head,
                count > 1 ? AfterSeoul.Core.Loc.Text("{0}개   {1}", count, Theme.Won(def.BasePrice)) : Theme.Won(def.BasePrice),
                Theme.FontSmall, TextAnchor.MiddleRight, Theme.TextFaint);
            Ui.Size(right.gameObject, width: 300f, flexWidth: 0f);

            var effect = Ui.Label("Effect", col, EquipText.Summary(def, Session.Data),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Size(effect.gameObject, 38f);
        }

        private void DoEquip(string uid, string itemId)
        {
            if (!Session.Equip(uid, itemId))
            {
                var scav = FindScav(uid);
                string reason = Equipment.EquipBlockReason(Session.Save, Session.Data, scav, itemId);
                Shell.Toast(reason ?? AfterSeoul.Core.Loc.Text("지급하지 못했습니다"), 3f);
            }

            Shell.AfterAction();
            OpenLoadout(uid);   // 목록으로 돌아가 결과를 보여준다
        }

        private void DoUnequip(string uid, string slot)
        {
            if (!Session.Unequip(uid, slot))
                Shell.Toast(AfterSeoul.Core.Loc.Text("창고에 자리가 없어 벗길 수 없습니다"), 3f);

            Shell.AfterAction();
            OpenLoadout(uid);
        }

        private ScavState FindScav(string uid)
        {
            foreach (var s in Session.Save.Scavs) if (s.Uid == uid) return s;
            return null;
        }

        // ── 표기 ─────────────────────────────────────────────────

        /// <summary>가장 높은 능력치를 특기로 본다. 데이터가 아니라 표시용 판정이다.</summary>
        private static string StatLine(ScavOffer o)
        {
            string best = o.Search >= o.Combat && o.Search >= o.Survival ? AfterSeoul.Core.Loc.Text("탐색")
                        : o.Combat >= o.Survival ? AfterSeoul.Core.Loc.Text("전투") : AfterSeoul.Core.Loc.Text("생존");
            return AfterSeoul.Core.Loc.Text("탐색 {0}    전투 {1}    생존 {2}      특기 {3}", o.Search, o.Combat, o.Survival, best);
        }

        private static string TierLabel(int tier)
        {
            switch (tier)
            {
                case 3: return AfterSeoul.Core.Loc.Text("숙련  ★★★");
                case 2: return AfterSeoul.Core.Loc.Text("중급  ★★☆");
                default: return AfterSeoul.Core.Loc.Text("초보  ★☆☆");
            }
        }

        private static Color TierColor(int tier)
        {
            switch (tier)
            {
                case 3: return Theme.Safe;
                case 2: return Theme.Info;
                default: return Theme.TextDim;
            }
        }

        private static string StatusLabel(ScavStatus status)
        {
            switch (status)
            {
                case ScavStatus.Idle: return AfterSeoul.Core.Loc.Text("대기");
                case ScavStatus.OnExpedition: return AfterSeoul.Core.Loc.Text("탐색중");
                case ScavStatus.Injured: return AfterSeoul.Core.Loc.Text("부상");
                case ScavStatus.Treating: return AfterSeoul.Core.Loc.Text("치료중");
                case ScavStatus.Missing: return AfterSeoul.Core.Loc.Text("실종");
                default: return AfterSeoul.Core.Loc.Text("사망");
            }
        }

        private static Color StatusColor(ScavStatus status)
        {
            switch (status)
            {
                case ScavStatus.Idle: return Theme.Safe;
                case ScavStatus.OnExpedition: return Theme.Info;
                case ScavStatus.Injured:
                case ScavStatus.Treating: return Theme.Warn;
                default: return Theme.Danger;
            }
        }
    }
}
