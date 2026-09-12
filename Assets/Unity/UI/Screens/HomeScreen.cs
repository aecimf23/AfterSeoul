using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Mail;
using AfterSeoul.Quest;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI.Screens
{
    /// <summary>
    /// 기지 / 홈. GDD §29 — <b>장식 화면이 아니라 오늘의 업무판</b>이다.
    /// 접속하자마자 "지금 뭘 해야 하는가"가 보여야 한다.
    ///
    /// <para>복귀 보고서는 <see cref="GameSession.LastReport"/> 를 그대로 그린다.
    /// 세이브를 뒤져서 "뭐가 바뀌었지"를 역산하지 않는다 (ARCHITECTURE §9).</para>
    /// </summary>
    public sealed class HomeScreen : ScreenBase
    {
        public override string TabName => "기지";
        public override IconSet.TabGlyph Glyph => IconSet.TabGlyph.Home;

        private Text _employerLine;

        /// <summary>다음 레벨까지. 레벨이 지역·의뢰·고용을 막고 있어서 장식이 아니다.</summary>
        private ProgressBar _levelBar;

        private RectTransform _questBody;
        private RectTransform _reportBody;
        private RectTransform _statusBody;
        private RectTransform _reportCard;

        /// <summary>본편 연동 상태와 발송함. 연동은 선택이라(GDD §12) 안 해도 아무 문제가 없어야 한다.</summary>
        private RectTransform _linkBody;

        /// <summary>연동 카드에 그리는 배송 줄 수의 상한. 나머지는 "외 N건"으로 접는다.</summary>
        private const int MaxShipmentRows = 5;

        /// <summary>첫 30분 안내 (GDD §16). 한 바퀴 돌면 카드째 사라진다.</summary>
        private RectTransform _guideBody;
        private RectTransform _guideCard;

        /// <summary>월간 지원계약 (GDD §11). 파는 것과 안 파는 것을 같이 적는다.</summary>
        private RectTransform _supportBody;

        protected override void Build()
        {
            var host = Ui.Rect("Host", Root);
            Ui.Stretch(host, Theme.Gutter, Theme.Gutter, 16f, 16f);

            ScrollRect scroll;
            var col = Ui.ScrollList("Scroll", host, out scroll, 16f);

            _employerLine = Ui.Label("Employer", col, "", Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Size(_employerLine.gameObject, 46f);

            // 레벨이 지역·의뢰 티어·고용 티어를 막고 있다. "다음 레벨까지 12,400" 이라고만 적으면
            // 그게 많은 건지 적은 건지 알 수가 없다 — 막대는 얼마나 왔는지를 읽지 않고 보여준다.
            // (Leveling.ProgressInLevel 은 이걸 위해 만들어져 있었는데 부르는 데가 없었다.)
            _levelBar = Ui.Bar(col, 6f, Theme.Accent);

            // 안내가 제일 위다. 무엇을 할지 모르는 사람에게 복귀 보고를 먼저 보여줄 이유가 없다.
            _guideCard = Ui.Card(col, "지금 할 일", out _guideBody);

            _reportCard = Ui.Card(col, "복귀 보고", out _reportBody);
            Ui.Card(col, "오늘의 지시", out _questBody);
            Ui.Card(col, "현재 상태", out _statusBody);
            Ui.Card(col, "본편 연동", out _linkBody);
            Ui.Card(col, "지원계약", out _supportBody);
        }

        public override void Refresh()
        {
            if (_questBody == null) return;

            var save = Session.Save;
            string npc = save.Player.EmployerNpcId;
            // 다음 레벨까지 남은 경험치를 같이 보여준다. 레벨이 지역·의뢰·고용을 막고 있어서,
            // "얼마나 더 하면 열리는지"가 안 보이면 무엇을 향해 가는지 알 수가 없다.
            long toNext = Leveling.ExpToNextLevel(save.Player.Exp, Session.Data.Balance);
            string next = toNext > 0 ? $"   ·   다음 레벨까지 {toNext:N0}" : "";
            _employerLine.text =
                $"{Loc.TraderName(npc)} 라인   ·   Lv.{save.Player.Level}   ·   신뢰도 {Trust(npc)}{next}";

            if (_levelBar != null && _levelBar.Alive)
                _levelBar.Set((float)Leveling.ProgressInLevel(save.Player.Exp, Session.Data.Balance),
                    animate: true);

            BuildReport();
            BuildQuests();
            BuildGuide();
            BuildStatus();
            BuildLink();
            BuildSupport();
        }

        private int Trust(string npcId)
        {
            int v;
            return Session.Save.NpcTrust.TryGetValue(npcId, out v) ? v : 0;
        }

        // ── 복귀 보고 ───────────────────────────────────────────

        private void BuildReport()
        {
            Ui.Clear(_reportBody);
            var report = Session.LastReport;

            // 볼 게 없으면 카드 자체를 숨긴다. 빈 카드는 화면만 먹는다.
            if (report == null || report.IsEmpty)
            {
                _reportCard.gameObject.SetActive(false);
                return;
            }
            _reportCard.gameObject.SetActive(true);

            // 문장은 ReportLines 한 곳에서만 만든다. 돌아온 순간의 연출도 같은 줄을 쓴다 —
            // 각자 만들면 한쪽에만 새 소식이 붙는 일이 반드시 생긴다.
            foreach (var line in ReportLines.Build(report, Session.Save, Session.Data))
                AddLine(_reportBody, (line.Indent > 0 ? "    " : "") + line.Text, line.Color);
        }

        // ── 오늘의 지시 ─────────────────────────────────────────

        private void BuildQuests()
        {
            Ui.Clear(_questBody);

            var save = Session.Save;
            var pool = Session.Data.GetQuestPool(
                Employers.QuestPoolId(Session.Data, save.Player.EmployerNpcId));

            if (save.Quests.Active.Count == 0)
            {
                AddLine(_questBody, "지시 대기 중", Theme.TextFaint);
                return;
            }

            for (int i = 0; i < save.Quests.Active.Count; i++)
            {
                var active = save.Quests.Active[i];
                QuestDef def = null;
                if (pool != null)
                    foreach (var q in pool)
                        if (q.Id == active.QuestId) { def = q; break; }

                if (def == null)
                {
                    AddLine(_questBody, active.QuestId, Theme.TextFaint);
                    continue;
                }

                BuildQuestRow(def, active, i);
            }
        }

        private void BuildQuestRow(QuestDef def, ActiveQuest active, int index)
        {
            var row = Ui.Rect("Quest" + index, _questBody);
            Ui.Column(row, 4f, new RectOffset(0, 0, 6, 6));

            // 고용주의 말이 먼저다. 품목 목록만 있으면 심부름표지만, 한 줄이 붙으면 부탁이 된다.
            // 대사가 없는 의뢰도 있을 수 있으니 없으면 통째로 건너뛴다 — 키가 그대로 보이면 안 된다.
            string line = Loc.QuestAccept(def.Id);
            if (!string.IsNullOrEmpty(line))
            {
                var voice = Ui.Label("Voice", row, "“" + line + "”", Theme.FontSmall,
                    TextAnchor.UpperLeft, active.Delivered ? Theme.TextFaint : Theme.Info);
                voice.horizontalOverflow = HorizontalWrapMode.Wrap;
                voice.verticalOverflow = VerticalWrapMode.Truncate;
                Ui.Size(voice.gameObject, 68f);
            }

            // 요구 조건 + 보유량
            var parts = new List<string>();
            bool canDeliver = !active.Delivered;
            foreach (var req in def.Requires)
            {
                int have;
                string label;
                if (!string.IsNullOrEmpty(req.ItemId))
                {
                    have = Inventory.Warehouse.CountOf(Session.Save.Warehouse, req.ItemId);
                    label = Loc.ItemName(req.ItemId);
                }
                else
                {
                    have = Inventory.Warehouse.CountByTag(Session.Save.Warehouse, Session.Data, req.Tag);
                    label = req.Tag + " 계열";
                }
                if (have < req.Count) canDeliver = false;
                parts.Add($"{label} {have}/{req.Count}");
            }

            // 요구 품목을 아이콘으로 먼저 보여준다 — 무엇을 달라는 건지가 읽기 전에 잡힌다.
            var reqRow = Ui.Rect("ReqRow", row);
            Ui.Size(reqRow.gameObject, 48f);
            Ui.Row(reqRow, 8f);

            foreach (var req in def.Requires)
            {
                var group = !string.IsNullOrEmpty(req.ItemId)
                    ? ItemGroups.Of(Session.Data.GetItem(req.ItemId))
                    : GroupForTag(req.Tag);

                Ui.Icon("I_" + (req.ItemId ?? req.Tag), reqRow, group, 40f);
            }

            var title = Ui.Label("Req", reqRow, string.Join("   ", parts.ToArray()), Theme.FontBody,
                TextAnchor.MiddleLeft, active.Delivered ? Theme.TextFaint : Theme.Text);
            Ui.Size(title.gameObject, flexWidth: 1f);

            var reward = Ui.Label("Reward", row,
                $"보상 {Theme.Won(def.RewardMoney)}   ·   신뢰도 +{def.RewardTrust}",
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Size(reward.gameObject, 40f);

            if (active.Delivered)
            {
                var done = Ui.Label("Done", row, "납품 완료", Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Safe);
                Ui.Size(done.gameObject, 44f);
                return;
            }

            string questId = def.Id;
            var btn = Ui.Button("Deliver", row, canDeliver ? "납품" : "물자 부족",
                canDeliver ? (System.Action)(() => Deliver(questId)) : null,
                canDeliver ? Theme.Accent : Theme.Line, Theme.FontSmall);
            btn.interactable = canDeliver;
            Ui.Size(btn.gameObject, 84f);
        }

        private void Deliver(string questId)
        {
            if (!Session.Deliver(questId))
            {
                Shell.Toast("납품에 실패했습니다");
                return;
            }
            // 받았다는 확인보다 고용주의 대꾸가 낫다 — 이 게임에서 사람이 말을 거는 몇 안 되는 순간이다.
            string line = Loc.QuestComplete(questId);
            Shell.Toast(string.IsNullOrEmpty(line)
                ? "납품 완료"
                : $"{Loc.TraderName(Session.Save.Player.EmployerNpcId)}  “{line}”", 4.5f);

            Shell.AfterAction();
        }

        // ── 현재 상태 ───────────────────────────────────────────

        // ── 첫 30분 안내 ────────────────────────────────────────

        /// <summary>
        /// 다음에 할 일 한 줄 (GDD §16).
        ///
        /// <para>대본을 재생하지 않는다 — <see cref="Tutorial"/> 이 지금 상태를 보고 되묻는다.
        /// 그래서 안내를 무시하고 딴 짓을 해도 화면이 조용히 따라오고, 한 바퀴를 돌면
        /// 카드가 통째로 사라진다. "건너뛰기" 버튼을 따로 만들 필요가 없다.</para>
        /// </summary>
        private void BuildGuide()
        {
            Ui.Clear(_guideBody);

            var step = Tutorial.Current(Session.Save, Session.Data);
            if (step == TutorialStep.Done)
            {
                _guideCard.gameObject.SetActive(false);
                return;
            }

            _guideCard.gameObject.SetActive(true);

            var text = Ui.Label("Hint", _guideBody, Tutorial.HintOf(step), Theme.FontBody,
                TextAnchor.UpperLeft, Theme.Text);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Ui.Size(text.gameObject, 96f);

            string tab = Tutorial.TabOf(step);
            if (string.IsNullOrEmpty(tab)) return;

            var go = Ui.Button("Go", _guideBody, $"{tab}(으)로 가기",
                () => Shell.SelectByName(tab), Theme.Accent, Theme.FontSmall);
            Ui.Size(go.gameObject, 84f);

            // 루프 밖의 칸(치료·회복)은 번호가 없다. 옆길에 번호를 붙이면 진행도가 거짓이 된다.
            int index = Tutorial.IndexOf(step);
            if (index <= 0) return;

            var progress = Ui.Label("Step", _guideBody,
                $"{index} / {Tutorial.TotalSteps}",
                Theme.FontSmall, TextAnchor.MiddleRight, Theme.TextFaint);
            Ui.Size(progress.gameObject, 34f);
        }

        // ── 지원계약 (GDD §11) ──────────────────────────────────

        /// <summary>
        /// 파는 것과 <b>안 파는 것</b>을 같이 적는다.
        ///
        /// <para>안 판다는 걸 분명히 써두는 편이 나중에 의심받는 것보다 낫다 —
        /// 특히 본편 배송량은 돈으로 늘릴 수 없다는 것(GDD §11 금지 목록)을 화면에 남긴다.</para>
        /// </summary>
        private void BuildSupport()
        {
            Ui.Clear(_supportBody);

            var now = Session.Clock.UtcNow;
            bool active = Support.IsActive(Session.Save, now);

            var head = Ui.Label("State", _supportBody,
                active
                    ? $"계약 중 — {(int)Support.Remaining(Session.Save, now).TotalDays}일 남음"
                    : "계약 없음 — 핵심 콘텐츠는 전부 무료로 즐길 수 있습니다",
                Theme.FontSmall, TextAnchor.MiddleLeft, active ? Theme.Safe : Theme.TextDim);
            Ui.Size(head.gameObject, 44f);

            foreach (var benefit in Support.Benefits)
            {
                var line = Ui.Label("B_" + benefit, _supportBody, "· " + benefit, Theme.FontSmall,
                    TextAnchor.MiddleLeft, active ? Theme.Text : Theme.TextFaint);
                Ui.Size(line.gameObject, 36f);
            }

            var never = Ui.Label("Never", _supportBody,
                "돈으로 살 수 없는 것: " + string.Join(", ", Support.NeverSold),
                Theme.FontSmall, TextAnchor.UpperLeft, Theme.Warn);
            never.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Size(never.gameObject, 64f);

            int adsLeft = RewardedAd.RemainingToday(Session.Save, now);
            var ads = Ui.Label("Ads", _supportBody,
                $"오늘 볼 수 있는 보상 광고 {adsLeft}/{RewardedAd.MaxPerDay}회 — 강제 광고는 없습니다",
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextFaint);
            Ui.Size(ads.gameObject, 40f);

            BuildSupportButtons(active, adsLeft);
        }

        /// <summary>
        /// 계약과 광고를 실제로 누를 수 있게 한다.
        ///
        /// <para><b>이게 없던 동안 §11 은 전부 설명문이었다.</b> <c>Support.Grant</c> 와
        /// <c>RewardedAd.TryConsume</c> 은 테스트 말고는 부르는 데가 한 군데도 없었고,
        /// 그래서 게임 안에서는 계약이 절대 켜지지 않고 광고도 절대 볼 수 없었다 —
        /// 파는 물건을 적어두기만 하고 계산대를 안 만든 셈이다.</para>
        ///
        /// <para><b>상점이 안 붙은 빌드에서는 버튼을 아예 만들지 않는다</b> (<c>IStore.Available</c>).
        /// 눌러도 "상점이 없습니다"만 뜨는 버튼은 없느니만 못하다.</para>
        /// </summary>
        private void BuildSupportButtons(bool active, int adsLeft)
        {
            bool canSell = Session.Store != null && Session.Store.Available;

            // 계약자는 광고 없이 보상을 받으므로, 상점이 안 붙은 빌드에서도 이 버튼은 살아 있다.
            if (!canSell && !active) return;

            var row = Ui.Rect("SupportButtons", _supportBody);
            Ui.Size(row.gameObject, 88f);
            Ui.Row(row, 12f);

            if (canSell)
            {
                var buy = Ui.Button("Buy", row, active ? "계약 연장" : "지원계약", BuySupport,
                    Theme.AccentDim, Theme.FontSmall);
                Ui.Size(buy.gameObject, flexWidth: 1f);
            }

            var watch = Ui.Button("Ad", row,
                (active ? "혜택 받기" : "광고 보기") + $" ({adsLeft})", OpenAdMenu,
                adsLeft > 0 ? Theme.Line : Theme.Panel, Theme.FontSmall);
            Ui.Size(watch.gameObject, flexWidth: 1f);
            watch.interactable = adsLeft > 0;
        }

        private void BuySupport()
        {
            Session.PurchaseSupport((ok, message) =>
            {
                Shell.Toast(message, 3.5f);
                if (ok) Sfx.Complete();
                Shell.AfterAction();
            });
        }

        /// <summary>
        /// 어떤 보상을 받을지 고르게 한다.
        ///
        /// <para>보상을 하나로 고정하지 않는 이유: 지금 급한 것이 사람마다 다르다. 치료가 급한
        /// 사람에게 제작 단축을 주면 그건 보상이 아니라 낭비다.</para>
        /// </summary>
        private void OpenAdMenu()
        {
            RectTransform body;
            var modal = Ui.Modal("AdMenu", Root, "보상 광고", CloseAdMenu, out body);
            _adModal = modal;

            bool contracted = Support.IsActive(Session.Save, Session.Clock.UtcNow);

            var note = Ui.Paragraph("Note", body,
                (contracted
                    ? "계약 중이라 광고를 보지 않고 받습니다. 하루 "
                    : "광고를 끝까지 보면 아래 중 하나를 받습니다. 강제 광고는 없고, 하루 ")
                + RewardedAd.MaxPerDay + "회까지입니다.",
                Theme.FontSmall, Theme.TextDim);
            Ui.Size(note.gameObject, 76f);

            AddRewardButton(body, RewardedAd.Reward.RerollHiringMarket,
                "고용 시장 다시 열기", "오늘 온 후보가 마음에 안 들 때");
            AddRewardButton(body, RewardedAd.Reward.SpeedUpCraft,
                "제작 30분 단축", "가장 먼저 끝나는 것 하나");
            AddRewardButton(body, RewardedAd.Reward.SpeedUpRecovery,
                "회복 2시간 단축", "치료 중인 사람 하나");
        }

        private RectTransform _adModal;

        private void CloseAdMenu()
        {
            if (_adModal == null) return;
            _adModal.SetParent(null, false);
            UnityEngine.Object.Destroy(_adModal.gameObject);
            _adModal = null;
        }

        private void AddRewardButton(RectTransform parent, RewardedAd.Reward reward,
            string label, string why)
        {
            var card = Ui.Surface("R_" + reward, parent, Theme.PanelAlt);
            Ui.Size(card.gameObject, 116f);
            Ui.Column(card, 2f, new RectOffset(18, 18, 10, 10));

            var text = Ui.Label("Label", card, label, Theme.FontBody, TextAnchor.MiddleLeft);
            Ui.Size(text.gameObject, 46f);

            var sub = Ui.Label("Why", card, why, Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextFaint);
            Ui.Size(sub.gameObject, 34f);

            // Ui.Surface 의 바탕은 raycastTarget 이 꺼져 있다(장식이니까). 누를 것으로 쓰려면
            // 켜야 한다 — 안 켜면 버튼이 붙어 있는데 눌리지 않고, 그 원인이 화면에 안 보인다.
            var bg = card.GetComponent<Image>();
            bg.raycastTarget = true;

            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => Watch(reward));
            btn.onClick.AddListener(() => Tween.Punch(card));
        }

        private void Watch(RewardedAd.Reward reward)
        {
            CloseAdMenu();

            Session.WatchAd(reward, (ok, message) =>
            {
                Shell.Toast(message, 3.5f);
                if (ok) Sfx.Complete();
                Shell.AfterAction();
            });
        }

        // ── 본편 연동 ───────────────────────────────────────────

        /// <summary>
        /// 연동 상태와 발송함.
        ///
        /// <para><b>연동은 선택이다</b> (GDD §12). 그래서 안 한 사람에게 미완료 과제처럼 보이면
        /// 안 되고, 한 사람에게는 "보낸 게 어디까지 갔는지"가 보여야 한다 — 물건을 남의 게임으로
        /// 넘기는 일이라, 확인할 데가 없으면 사라진 것과 구별이 안 된다.</para>
        /// </summary>
        private void BuildLink()
        {
            Ui.Clear(_linkBody);

            var mail = Session.Save.Mail;

            if (!mail.Linked)
            {
                bool ready = Session.MailLink != null && Session.MailLink.Available;

                var info = Ui.Paragraph("Off", _linkBody,
                    ready
                        ? "PC 본편과 연결하면 창고의 보급품을 하이드아웃 우편함으로 보낼 수 있습니다.\n"
                          + "연결하지 않아도 이 게임은 전부 즐길 수 있습니다."
                        // 아직 안 되는 것을 "할 수 있습니다"로 적어두고 버튼만 없애면, 누르려다
                        // 못 누른 사람은 고장으로 읽는다. 안 되는 이유를 그 자리에 적는다.
                        : Session.MailLink.UnavailableReason,
                    Theme.FontSmall, Theme.TextDim);
                Ui.Size(info.gameObject, 76f);

                // 통로가 없으면 버튼을 만들지 않는다. 눌러도 거절당하는 버튼은 없느니만 못하고,
                // 무엇보다 <b>연결되면 발송이 물건을 지운다</b> — 받을 쪽이 아직 없기 때문이다.
                if (!ready) return;

                var btn = Ui.Button("Link", _linkBody, "본편과 연결", OnLink, Theme.Line, Theme.FontSmall);
                Ui.Size(btn.gameObject, 80f);
                return;
            }

            var who = Ui.Label("On", _linkBody,
                $"연결됨 — {mail.LinkedProfileLabel}", Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Safe);
            Ui.Size(who.gameObject, 44f);

            if (mail.Outbox.Count == 0)
            {
                var empty = Ui.Label("None", _linkBody, "보낸 것이 없습니다. 창고에서 보낼 수 있습니다.",
                    Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextFaint);
                Ui.Size(empty.gameObject, 42f);
            }
            else
            {
                // 최근 것이 위로. 오래된 배송을 먼저 보여줄 이유가 없다.
                //
                // 그리고 몇 개까지만 그린다. 하루 2건씩 쌓이는데 전부 그리면 한 달이면 60줄이고,
                // 그 아래 있는 '연결 끊기' 버튼은 영영 안 보인다. 목록의 길이가 화면 구조를
                // 망가뜨리는 건 목록이 길어서가 아니라 상한을 안 둬서다.
                int shown = 0;
                for (int i = mail.Outbox.Count - 1; i >= 0 && shown < MaxShipmentRows; i--, shown++)
                    BuildShipmentRow(mail.Outbox[i], i);

                int hidden = mail.Outbox.Count - shown;
                if (hidden > 0)
                {
                    var more = Ui.Label("More", _linkBody, $"외 {hidden}건", Theme.FontSmall,
                        TextAnchor.MiddleLeft, Theme.TextFaint);
                    Ui.Size(more.gameObject, 38f);
                }

                // 본편이 안 가져가면 발송이 막힌다 (LINK_CONTRACT §V6). 막히고 나서 알면 늦으므로
                // 가까워지면 미리 말한다 — 창고에서 보내려다 거절당하는 것보다 낫다.
                int pending = Outbox.PendingCount(Session.Save);
                int cap = Session.Data.Transfer.Limits.MaxPendingShipments;
                if (pending >= cap - 3)
                {
                    var warn = Ui.Paragraph("Pending", _linkBody,
                        pending >= cap
                            ? $"본편이 안 가져간 화물이 {pending}건입니다 — 본편에서 수령해야 더 보낼 수 있습니다."
                            : $"본편이 안 가져간 화물 {pending}/{cap}건. 다 차면 발송이 막힙니다.",
                        Theme.FontSmall, pending >= cap ? Theme.Danger : Theme.Warn);
                    Ui.Size(warn.gameObject, 62f);
                }
            }

            var unlink = Ui.Button("Unlink", _linkBody, "연결 끊기", OnUnlink, Theme.Line, Theme.FontSmall);
            Ui.Size(unlink.gameObject, 72f);
        }

        /// <summary>태그 조건은 품목이 하나로 정해지지 않는다 — 그 태그를 가진 아무거나로 아이콘을 고른다.</summary>
        private ItemGroup GroupForTag(string tag)
        {
            foreach (var item in Session.Data.AllItems)
            {
                if (item.Tags == null) continue;
                foreach (var t in item.Tags)
                    if (t == tag) return ItemGroups.Of(item);
            }
            return ItemGroup.Parts;
        }

        private void BuildShipmentRow(MailShipment shipment, int index)
        {
            var row = Ui.Rect("Ship" + index, _linkBody);
            Ui.Size(row.gameObject, 44f);
            Ui.Row(row, 8f);

            var parts = new List<string>();
            foreach (var stack in shipment.Items)
                parts.Add($"{Loc.ItemName(stack.ItemId)} ×{stack.Count}");

            var what = Ui.Label("What", row, string.Join(", ", parts.ToArray()),
                Theme.FontSmall, TextAnchor.MiddleLeft,
                shipment.Claimed ? Theme.TextFaint : Theme.Text);
            Ui.Size(what.gameObject, flexWidth: 1f);

            // 값과 보낸 시각.
            //
            // 둘 다 발송할 때부터 세이브에 적히고 있었는데 읽는 곳이 없었다. 그래서 목록은
            // "무엇을 보냈나"만 말하고 "얼마어치였나"와 "언제부터 기다리고 있나"는 말하지
            // 않았다. 일일 한도가 금액 기준이라 앞의 것이 없으면 한도 계산을 눈으로 못 따라가고,
            // 뒤의 것이 없으면 본편이 안 받고 있는 건지 원래 그런 건지 구분이 안 된다.
            var value = Ui.Label("Value", row, Theme.Won(shipment.TotalValue),
                Theme.FontSmall, TextAnchor.MiddleRight, Theme.TextFaint);
            Ui.Size(value.gameObject, width: 130f, flexWidth: 0f);

            // 옛 세이브에는 QueuedAt 이 없어서 0001년으로 읽힌다 — 그대로 빼면 "739000일 대기"다.
            string when = shipment.QueuedAt == default(System.DateTimeOffset)
                ? null
                : WaitedText(Session.Clock.UtcNow - shipment.QueuedAt);
            var state = Ui.Label("State", row,
                shipment.Claimed ? "수령됨" : (when == null ? "대기 중" : "대기 중 · " + when),
                Theme.FontSmall, TextAnchor.MiddleRight,
                shipment.Claimed ? Theme.Safe : Theme.Info);
            Ui.Size(state.gameObject, width: 170f, flexWidth: 0f);
        }

        /// <summary>"3일" / "5시간" / "방금". 분 단위까지 적으면 목록이 시끄러워진다.</summary>
        private static string WaitedText(System.TimeSpan waited)
        {
            if (waited.TotalDays >= 1) return $"{(int)waited.TotalDays}일";
            if (waited.TotalHours >= 1) return $"{(int)waited.TotalHours}시간";
            if (waited.TotalMinutes >= 1) return $"{(int)waited.TotalMinutes}분";
            return "방금";
        }

        private void OnLink()
        {
            // 코드 입력 화면은 P5 에서 이 자리에 들어온다. 지금은 통로(IMailLink)가 코드를
            // 무시하고 답만 준다 — 중요한 건 <b>답이 성공이라고 단정하지 않는다</b>는 것이다.
            Session.LinkToMainline(code: null, done: (ok, message) =>
            {
                Shell.Toast(
                    ok ? "연결했습니다. 창고에서 보급품을 보낼 수 있습니다." : message,
                    ok ? 3.5f : 5f);

                if (ok) Sfx.Complete();
                Shell.AfterAction();
            });
        }

        private void OnUnlink()
        {
            Session.UnlinkFromMainline();
            Shell.Toast("연결을 끊었습니다. 보낸 기록은 남아 있습니다.", 3f);
            Shell.AfterAction();
        }

        private void BuildStatus()
        {
            Ui.Clear(_statusBody);
            var save = Session.Save;

            int idle = 0, away = 0, hurt = 0;
            foreach (var s in save.Scavs)
            {
                if (s.Status == ScavStatus.Idle) idle++;
                else if (s.Status == ScavStatus.OnExpedition) away++;
                else if (s.Status == ScavStatus.Injured || s.Status == ScavStatus.Treating) hurt++;
            }

            int pendingCraft = 0;
            var now = Session.Clock.UtcNow;
            foreach (var j in save.Factory.Queue)
                if (!j.Collected && j.CompletesAt > now) pendingCraft++;

            AddLine(_statusBody, $"창고 {save.Warehouse.Stacks.Count} / {save.Warehouse.TotalCapacity} 칸",
                save.Warehouse.Stacks.Count >= save.Warehouse.TotalCapacity ? Theme.Danger : Theme.Text);
            AddLine(_statusBody, $"제작 진행 중 {pendingCraft}건", Theme.Text);

            if (save.Scavs.Count == 0)
                AddLine(_statusBody, "고용한 스캐브 없음", Theme.TextFaint);
            else
                AddLine(_statusBody, $"스캐브 대기 {idle} · 탐색중 {away} · 부상 {hurt}",
                    hurt > 0 ? Theme.Warn : Theme.Text);
        }

        private static void AddLine(RectTransform parent, string text, Color color)
        {
            var t = Ui.Label("L", parent, text, Theme.FontSmall, TextAnchor.MiddleLeft, color);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Size(t.gameObject, 42f);
        }
    }
}
