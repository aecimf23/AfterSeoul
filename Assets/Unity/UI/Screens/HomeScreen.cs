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
        private Text _briefing;
        private Button _today;
        private RectTransform _details;
        private bool _detailsOpen;
        private Button _explore;
        private Text _exploreNote;

        internal void OpenTasks()
        {
            _detailsOpen = true;
            _details.gameObject.SetActive(true);
            Refresh();
        }

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

            _explore = Ui.Button("DirectExploration", col, Loc.Text("직접 탐색하기  →"), Shell.OpenExploration, Theme.AccentDim, 40);
            Ui.Size(_explore.gameObject, 130);
            _exploreNote = Ui.Paragraph("ExplorationNote", col, "", Theme.FontSmall, Theme.TextDim);
            Ui.Size(_exploreNote.gameObject, 65);

            var summary = Ui.Rect("HomeSummary", col);
            Ui.Size(summary.gameObject, 180f);
            _briefing = Ui.Paragraph("Brief", summary, "", Theme.FontBody, Theme.TextDim);
            _briefing.rectTransform.anchorMax = new Vector2(.48f, 1f);
            _today = Ui.Button("TodayCompact", summary, "", () => {
                _detailsOpen = !_detailsOpen;
                _details.gameObject.SetActive(_detailsOpen);
                Refresh();
            }, Theme.PanelAlt, Theme.FontSmall);
            var todayRect = (RectTransform)_today.transform;
            todayRect.anchorMin = new Vector2(.54f, 0);
            todayRect.anchorMax = Vector2.one;
            todayRect.offsetMin = todayRect.offsetMax = Vector2.zero;

            var talk = Ui.Button("TalkToEmployer", col, Loc.Text("지금 무엇을 하면 될까요?"),
                () => Shell.ShowStepPrompt(Tutorial.ActionKey(Session.Save, Session.Data)), Theme.PanelAlt, Theme.FontSmall);
            Ui.Size(talk.gameObject, 76);

            _employerLine = Ui.Label("Employer", col, "", Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Size(_employerLine.gameObject, 46f);

            // 레벨이 지역·의뢰 티어·고용 티어를 막고 있다. "다음 레벨까지 12,400" 이라고만 적으면
            // 그게 많은 건지 적은 건지 알 수가 없다 — 막대는 얼마나 왔는지를 읽지 않고 보여준다.
            // (Leveling.ProgressInLevel 은 이걸 위해 만들어져 있었는데 부르는 데가 없었다.)
            _levelBar = Ui.Bar(col, 6f, Theme.Accent);

            var space = Ui.Rect("BreathingRoom", col);
            Ui.Size(space.gameObject, 130);
            _reportCard = Ui.Card(col, AfterSeoul.Core.Loc.Text("복귀 보고"), out _reportBody);
            _details = Ui.Rect("TaskDetails", col);
            Ui.Column(_details, 20);
            _details.gameObject.SetActive(false);

            // 안내가 제일 위다. 무엇을 할지 모르는 사람에게 복귀 보고를 먼저 보여줄 이유가 없다.
            _guideCard = Ui.Card(_details, AfterSeoul.Core.Loc.Text("지금 할 일"), out _guideBody);

            Ui.Card(_details, AfterSeoul.Core.Loc.Text("오늘의 지시"), out _questBody);
            Ui.Card(_details, AfterSeoul.Core.Loc.Text("현재 상태"), out _statusBody);
            Ui.Card(_details, AfterSeoul.Core.Loc.Text("본편 연동"), out _linkBody);
            Ui.Card(_details, AfterSeoul.Core.Loc.Text("지원계약"), out _supportBody);
        }

        public override void Refresh()
        {
            bool resume = Session.Save.Exploration != null && (Session.Save.Exploration.Result == null || !Session.Save.Exploration.Result.Acknowledged);
            Ui.SetButtonLabel(_explore, resume ? Loc.Text("탐색 이어하기  →") : Loc.Text("직접 탐색하기  →"));
            _exploreNote.text = Loc.Text("캐릭터 Lv.{0}", Session.Save.Player.CharacterLevel) + "  ·  " + Loc.Text("장비를 챙기고 서울로 · 물자를 찾아 무사히 돌아오세요");
            if (FirstExplorationQuest.IsPending(Session.Save)) {
                _exploreNote.text = FirstExplorationQuest.NextAction(Session.Save);
                if (!resume && Session.Save.FirstExplorationQuest?.ReadyToReport == true)
                    Ui.SetButtonLabel(_explore, Loc.Text("첫 의뢰 보고하기  →"));
            }
            if (_questBody == null) return;

            var save = Session.Save;
            string npc = save.Player.EmployerNpcId;
            int deployed = 0;
            foreach (var expedition in save.Expeditions) if (!expedition.Resolved) deployed++;
            _briefing.text = AfterSeoul.Core.Loc.Text("파견 {0}팀  /  보유 인원 {1}명\n제작 대기 {2}건", deployed, save.Scavs.Count, save.Factory.Queue.Count);
            int remaining = save.Quests.Active.FindAll(q => !q.Delivered).Count;
            var action = Tutorial.ActionKey(save, Session.Data);
            Ui.SetButtonLabel(_today, Loc.Text("오늘의 지시") + $" · {remaining}\n" +
                Tutorial.ActionTitle(action) +
                "\n" + Loc.Text(_detailsOpen ? "접기 ▴" : "자세히 보기 ▾"));
            if (FirstExplorationQuest.IsPending(save))
                Ui.SetButtonLabel(_today, Loc.Text("첫 의뢰") + "\n" + FirstExplorationQuest.Title(save));
            // 다음 레벨까지 남은 경험치를 같이 보여준다. 레벨이 지역·의뢰·고용을 막고 있어서,
            // "얼마나 더 하면 열리는지"가 안 보이면 무엇을 향해 가는지 알 수가 없다.
            long toNext = Leveling.ExpToNextLevel(save.Player.Exp, Session.Data.Balance);
            string next = toNext > 0 ? AfterSeoul.Core.Loc.Text("   ·   다음 레벨까지 {0:N0}", toNext) : "";
            _employerLine.text =
                AfterSeoul.Core.Loc.Text("{0} · Lv.{1} · 신뢰 {2}{3}", Loc.TraderName(npc), save.Player.Level, Trust(npc), next);

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
                AddLine(_questBody, AfterSeoul.Core.Loc.Text("지시 대기 중"), Theme.TextFaint);
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
                    label = AfterSeoul.Core.Loc.Text("{0} 계열", Loc.Text(req.Tag));
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

                if (!string.IsNullOrEmpty(req.ItemId)) Ui.Icon("I_" + req.ItemId, reqRow, Session.Data.GetItem(req.ItemId), 40f);
                else Ui.Icon("I_" + req.Tag, reqRow, group, 40f);
            }

            var title = Ui.Label("Req", reqRow, string.Join("   ", parts.ToArray()), Theme.FontBody,
                TextAnchor.MiddleLeft, active.Delivered ? Theme.TextFaint : Theme.Text);
            Ui.Size(title.gameObject, flexWidth: 1f);

            var reward = Ui.Label("Reward", row,
                AfterSeoul.Core.Loc.Text("보상 {0}   ·   신뢰도 +{1}", Theme.Won(def.RewardMoney), def.RewardTrust),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Size(reward.gameObject, 40f);

            if (active.Delivered)
            {
                var done = Ui.Label("Done", row, AfterSeoul.Core.Loc.Text("납품 완료"), Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Safe);
                Ui.Size(done.gameObject, 44f);
                return;
            }

            string questId = def.Id;
            var btn = Ui.Button("Deliver", row, canDeliver ? AfterSeoul.Core.Loc.Text("납품") : AfterSeoul.Core.Loc.Text("물자 부족"),
                canDeliver ? (System.Action)(() => Deliver(questId)) : null,
                canDeliver ? Theme.Accent : Theme.Line, Theme.FontSmall);
            btn.interactable = canDeliver;
            Ui.Size(btn.gameObject, 84f);
        }

        private void Deliver(string questId)
        {
            if (!Session.Deliver(questId))
            {
                Sfx.Error();
                Shell.Toast(AfterSeoul.Core.Loc.Text("납품에 실패했습니다"));
                return;
            }
            // 받았다는 확인보다 고용주의 대꾸가 낫다 — 이 게임에서 사람이 말을 거는 몇 안 되는 순간이다.
            string line = Loc.QuestComplete(questId);
            Sfx.Confirm();
            Shell.Toast(string.IsNullOrEmpty(line)
                ? AfterSeoul.Core.Loc.Text("납품 완료")
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
        private bool BuildOrientationGuide()
        {
            var state = Session.Save.Orientation;
            if (state == null || state.Stage == OrientationStage.Skipped || state.Stage == OrientationStage.Completed) return false;
            if (state.Stage == OrientationStage.Pending && Session.Save.Scavs.Count == 0) return false;
            if (state.Stage == OrientationStage.Completed &&
                Session.Save.Expeditions.Exists(e => !e.IsOrientation)) return false;
            _guideCard.gameObject.SetActive(true);
            string hint, button, tab;
            switch (state.Stage)
            {
                case OrientationStage.Pending:
                    hint = AfterSeoul.Core.Loc.Text("첫 동료가 합류했습니다. 탐색에서 1명을 선택하고 초도 보급에 보내세요.\n3분 · 무료 · 안전 복귀 · 납품 보상 50,000원");
                    button = AfterSeoul.Core.Loc.Text("첫 출동 준비"); tab = "탐색"; break;
                case OrientationStage.Outbound:
                    hint = AfterSeoul.Core.Loc.Text("보급팀이 이동 중입니다. 복귀까지 공장에서 물자를 만들어 두세요.\n앱을 꺼 두어도 복귀하며, 꾸러미는 기지에서 납품합니다.");
                    button = AfterSeoul.Core.Loc.Text("기다리는 동안 제작"); tab = "공장"; break;
                case OrientationStage.ReadyToDeliver:
                    hint = AfterSeoul.Core.Loc.Text("보급 꾸러미가 도착했습니다. 고용주에게 초도 물자를 넘겨 첫 임무를 마무리하세요.\n임무 물자는 창고 공간을 차지하지 않습니다.");
                    button = AfterSeoul.Core.Loc.Text("꾸러미 납품 · 50,000원 수령"); tab = null; break;
                default:
                    hint = AfterSeoul.Core.Loc.Text("첫 거래를 마쳤습니다. 인원 화면에서 장비를 구매·지급하고 다음 파견을 준비하세요.\n일반 파견은 유료이며 부상·실종 위험이 있습니다.");
                    button = AfterSeoul.Core.Loc.Text("동료 장비 준비"); tab = "인원"; break;
            }
            var text = Ui.Paragraph("OrientationHint", _guideBody, hint, Theme.FontBody, Theme.Text);
            Ui.Size(text.gameObject, 128f);
            var go = Ui.Button("OrientationNext", _guideBody, button, () => {
                if (tab != null) { Shell.SelectByName(tab); return; }
                if (Session.DeliverOrientation()) {
                    Sfx.Confirm();
                    Shell.Toast(AfterSeoul.Core.Loc.Text("초도 납품 완료 · 50,000원 지급. 다음 출동을 준비하세요."), 4f);
                } else Sfx.Error();
                Shell.AfterAction();
            }, Theme.Accent, Theme.FontSmall);
            Ui.Size(go.gameObject, 84f);
            return true;
        }

        private bool BuildGrowthGuide()
        {
            var state = Session.Save.Orientation;
            if (state == null || (state.Stage != OrientationStage.Completed && state.Stage != OrientationStage.Skipped)) return false;
            _guideCard.gameObject.SetActive(true);
            var goal = GrowthGuide.Current(Session.Save, Session.Data);
            string text, tab = "탐색", button = AfterSeoul.Core.Loc.Text("탐색 준비");
            switch (goal.Kind)
            {
                case GrowthGoalKind.Equip:
                    text = string.IsNullOrEmpty(goal.ItemId) ? AfterSeoul.Core.Loc.Text("동료에게 무기를 지급하세요.") :
                        AfterSeoul.Core.Loc.Text("{0}부터 준비해 보세요. ", Loc.ItemName(goal.ItemId)) +
                        (goal.Cost == 0 ? AfterSeoul.Core.Loc.Text("창고에 보관 중입니다.") : AfterSeoul.Core.Loc.Text("구매가 {0} · 보유 {1}", Theme.Won(goal.Cost), Theme.Won(Session.Save.Player.Money)));
                    text += AfterSeoul.Core.Loc.Text("\n인원 → 장비 → 해당 칸에서 구매·지급할 수 있습니다. 장비를 준비해도 일반 파견의 위험은 남습니다.");
                    tab = "인원"; button = AfterSeoul.Core.Loc.Text("동료 장비 준비"); break;
                case GrowthGoalKind.Earn:
                    text = AfterSeoul.Core.Loc.Text("명동 1인 파견까지 {0}이 더 필요합니다.\n공장에서 제작하고 창고에서 판매해 다음 출동 비용을 마련하세요.", Theme.Won(goal.Cost));
                    tab = "공장"; button = AfterSeoul.Core.Loc.Text("출동 자금 마련"); break;
                case GrowthGoalKind.Depart:
                    text = AfterSeoul.Core.Loc.Text("이제 명동 일반 파견에 도전할 차례입니다. 대기 인원 1명 기준 {0}.\n일반 파견에는 부상·실종 위험이 있습니다. 팀을 늘리면 비용도 달라집니다.", Theme.Won(goal.Cost));
                    break;
                case GrowthGoalKind.Wait:
                    text = AfterSeoul.Core.Loc.Text("동료가 돌아올 준비를 하고 있습니다. 탐색 복귀 또는 치료가 끝날 때까지 제작을 이어가세요.\n오프라인에서도 시간이 흐릅니다.");
                    tab = "공장"; button = AfterSeoul.Core.Loc.Text("기다리는 동안 제작"); break;
                case GrowthGoalKind.Treat:
                    text = AfterSeoul.Core.Loc.Text("현재 출동할 수 있는 동료가 없습니다. 인원 화면에서 부상자를 치료하세요.\n회복 뒤 다음 파견을 준비할 수 있습니다.");
                    tab = "인원"; button = AfterSeoul.Core.Loc.Text("부상자 치료"); break;
                case GrowthGoalKind.Hire:
                    text = AfterSeoul.Core.Loc.Text("다음 출동을 맡길 인원이 필요합니다. 인원 화면에서 고용과 실종자 상태를 확인하세요.");
                    tab = "인원"; button = AfterSeoul.Core.Loc.Text("인원 확인"); break;
                case GrowthGoalKind.Deliver:
                case GrowthGoalKind.Collect:
                    var pool = Session.Data.GetQuestPool(Employers.QuestPoolId(Session.Data, Session.Save.Player.EmployerNpcId));
                    QuestDef quest = null;
                    if (pool != null) foreach (var q in pool) if (q.Id == goal.QuestId) quest = q;
                    var parts = new List<string>();
                    if (quest != null) foreach (var req in quest.Requires)
                    {
                        int have = !string.IsNullOrEmpty(req.ItemId)
                            ? AfterSeoul.Inventory.Warehouse.CountOf(Session.Save.Warehouse, req.ItemId)
                            : AfterSeoul.Inventory.Warehouse.CountByTag(Session.Save.Warehouse, Session.Data, req.Tag);
                        parts.Add((!string.IsNullOrEmpty(req.ItemId) ? Loc.ItemName(req.ItemId) : Loc.Text(req.Tag)) + $" {have}/{req.Count}");
                    }
                    text = (goal.Kind == GrowthGoalKind.Deliver ? AfterSeoul.Core.Loc.Text("의뢰 물자가 준비됐습니다. 납품해 거래를 이어가세요.") : AfterSeoul.Core.Loc.Text("다음 거래에 필요한 물자를 모으세요. 제작하거나 탐색에서 회수할 수 있습니다.")) +
                        "\n" + string.Join(" · ", parts.ToArray());
                    if (quest != null) text += AfterSeoul.Core.Loc.Text("\n보상 {0} · 신뢰도 +{1} · 경험치 +{2}", Theme.Won(quest.RewardMoney), quest.RewardTrust, quest.RewardExp);
                    button = goal.Kind == GrowthGoalKind.Deliver ? AfterSeoul.Core.Loc.Text("준비된 의뢰 납품") : AfterSeoul.Core.Loc.Text("물자 탐색"); break;
                default:
                    text = AfterSeoul.Core.Loc.Text("오늘의 거래를 마쳤습니다. 다음 지역의 조건을 확인하고 장비와 물자를 준비하세요.");
                    break;
            }
            var hint = Ui.Paragraph("GrowthHint", _guideBody, text, Theme.FontBody, Theme.Text);
            Ui.Size(hint.gameObject, 164f);
            var go = Ui.Button("GrowthNext", _guideBody, button, () => {
                if (goal.Kind == GrowthGoalKind.Deliver) {
                    if (Session.Deliver(goal.QuestId)) { Sfx.Confirm(); Shell.Toast(AfterSeoul.Core.Loc.Text("납품 완료 · 다음 목표가 갱신됐습니다")); }
                    else { Sfx.Error(); Shell.Toast(AfterSeoul.Core.Loc.Text("의뢰가 갱신되었거나 물자가 부족합니다")); }
                    Shell.AfterAction();
                } else Shell.SelectByName(tab);
            }, Theme.Accent, Theme.FontSmall);
            Ui.Size(go.gameObject, 84f);
            if (goal.Kind == GrowthGoalKind.Collect) {
                var craft = Ui.Button("GrowthCraft", _guideBody, AfterSeoul.Core.Loc.Text("공장에서 필요한 물자 제작"), () => Shell.SelectByName("공장"));
                Ui.Size(craft.gameObject, 72f);
            }
            var map = GrowthGuide.NextMap(Session.Save, Session.Data);
            if (map != null) {
                var locked = AfterSeoul.Expedition.MapUnlock.LockReason(Session.Save, map);
                string condition = locked ?? AfterSeoul.Core.Loc.Text("해금 완료 · 탐색에서 출동 가능");
                if (map.Unlock != null && map.Unlock.Type == "npcTrust")
                    condition = condition.Replace(map.Unlock.NpcId, Loc.TraderName(map.Unlock.NpcId));
                var next = Ui.Paragraph("GrowthRegion", _guideBody, AfterSeoul.Core.Loc.Text("다음 지역 · {0}\n{1}", Loc.MapName(map.Id), condition), Theme.FontSmall, locked == null ? Theme.Safe : Theme.TextDim);
                Ui.Size(next.gameObject, 82f);
            }
            return true;
        }
        private void BuildGuide()
        {
            Ui.Clear(_guideBody);
            if (BuildOrientationGuide()) return;
            if (BuildGrowthGuide()) return;

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

            var go = Ui.Button("Go", _guideBody, AfterSeoul.Core.Loc.Text("{0}(으)로 가기", Loc.Text(tab)),
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
                    ? AfterSeoul.Core.Loc.Text("계약 중 — {0}일 남음", (int)Support.Remaining(Session.Save, now).TotalDays)
                    : AfterSeoul.Core.Loc.Text("계약 없음 — 핵심 콘텐츠는 전부 무료로 즐길 수 있습니다"),
                Theme.FontSmall, TextAnchor.MiddleLeft, active ? Theme.Safe : Theme.TextDim);
            Ui.Size(head.gameObject, 44f);

            foreach (var benefit in Support.Benefits)
            {
                var line = Ui.Label("B_" + benefit, _supportBody, "· " + Loc.Text(benefit), Theme.FontSmall,
                    TextAnchor.MiddleLeft, active ? Theme.Text : Theme.TextFaint);
                Ui.Size(line.gameObject, 36f);
            }

            var never = Ui.Label("Never", _supportBody,
                AfterSeoul.Core.Loc.Text("돈으로 살 수 없는 것: ") + string.Join(", ", System.Array.ConvertAll(Support.NeverSold, x => Loc.Text(x))),
                Theme.FontSmall, TextAnchor.UpperLeft, Theme.Warn);
            never.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Size(never.gameObject, 64f);

            int adsLeft = RewardedAd.RemainingToday(Session.Save, now);
            var ads = Ui.Label("Ads", _supportBody,
                AfterSeoul.Core.Loc.Text("오늘 볼 수 있는 보상 광고 {0}/{1}회 — 강제 광고는 없습니다", adsLeft, RewardedAd.MaxPerDay),
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
                var buy = Ui.Button("Buy", row, active ? AfterSeoul.Core.Loc.Text("계약 연장") : AfterSeoul.Core.Loc.Text("지원계약"), BuySupport,
                    Theme.AccentDim, Theme.FontSmall);
                Ui.Size(buy.gameObject, flexWidth: 1f);
            }

            var watch = Ui.Button("Ad", row,
                (active ? AfterSeoul.Core.Loc.Text("혜택 받기") : AfterSeoul.Core.Loc.Text("광고 보기")) + $" ({adsLeft})", OpenAdMenu,
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
            var modal = Ui.Modal("AdMenu", Root, AfterSeoul.Core.Loc.Text("보상 광고"), CloseAdMenu, out body);
            _adModal = modal;

            bool contracted = Support.IsActive(Session.Save, Session.Clock.UtcNow);

            var note = Ui.Paragraph("Note", body,
                (contracted
                    ? AfterSeoul.Core.Loc.Text("계약 중이라 광고를 보지 않고 받습니다. 하루 {0}회까지입니다.", RewardedAd.MaxPerDay)
                    : AfterSeoul.Core.Loc.Text("광고를 끝까지 보면 아래 중 하나를 받습니다. 강제 광고는 없고, 하루 {0}회까지입니다.", RewardedAd.MaxPerDay)),
                Theme.FontSmall, Theme.TextDim);
            Ui.Size(note.gameObject, 76f);

            AddRewardButton(body, RewardedAd.Reward.RerollHiringMarket,
                AfterSeoul.Core.Loc.Text("고용 시장 다시 열기"), AfterSeoul.Core.Loc.Text("오늘 온 후보가 마음에 안 들 때"));
            AddRewardButton(body, RewardedAd.Reward.SpeedUpCraft,
                AfterSeoul.Core.Loc.Text("제작 30분 단축"), AfterSeoul.Core.Loc.Text("가장 먼저 끝나는 것 하나"));
            AddRewardButton(body, RewardedAd.Reward.SpeedUpRecovery,
                AfterSeoul.Core.Loc.Text("회복 2시간 단축"), AfterSeoul.Core.Loc.Text("치료 중인 사람 하나"));
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
                        ? AfterSeoul.Core.Loc.Text("PC 본편과 연결하면 창고의 보급품을 하이드아웃 우편함으로 보낼 수 있습니다.\n")
                          + AfterSeoul.Core.Loc.Text("연결하지 않아도 이 게임은 전부 즐길 수 있습니다.")
                        // 아직 안 되는 것을 "할 수 있습니다"로 적어두고 버튼만 없애면, 누르려다
                        // 못 누른 사람은 고장으로 읽는다. 안 되는 이유를 그 자리에 적는다.
                        : Session.MailLink.UnavailableReason,
                    Theme.FontSmall, Theme.TextDim);
                Ui.Size(info.gameObject, 76f);

                // 통로가 없으면 버튼을 만들지 않는다. 눌러도 거절당하는 버튼은 없느니만 못하고,
                // 무엇보다 <b>연결되면 발송이 물건을 지운다</b> — 받을 쪽이 아직 없기 때문이다.
                if (!ready) return;

                var notice = Ui.Paragraph("AccountNotice", _linkBody,
                    AfterSeoul.Core.Loc.Text("본편 우편함과 같은 계정으로 로그인하세요. 처음 연결한 계정에 이 저장 데이터가 연결됩니다."), Theme.FontSmall, Theme.TextDim);
                Ui.Size(notice.gameObject, 64f);
                var btn = Ui.Button("Link", _linkBody, AfterSeoul.Core.Loc.Text("같은 계정으로 로그인"), OnLink, Theme.Line, Theme.FontSmall);
                Ui.Size(btn.gameObject, 80f);
                return;
            }

            var who = Ui.Label("On", _linkBody,
                AfterSeoul.Core.Loc.Text("연결됨 — {0}", mail.LinkedProfileLabel), Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Safe);
            Ui.Size(who.gameObject, 44f);

            if (mail.Outbox.Count == 0)
            {
                var empty = Ui.Label("None", _linkBody, AfterSeoul.Core.Loc.Text("보낸 것이 없습니다. 창고에서 보낼 수 있습니다."),
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
                    var more = Ui.Label("More", _linkBody, AfterSeoul.Core.Loc.Text("외 {0}건", hidden), Theme.FontSmall,
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
                            ? AfterSeoul.Core.Loc.Text("본편이 안 가져간 화물이 {0}건입니다 — 본편에서 수령해야 더 보낼 수 있습니다.", pending)
                            : AfterSeoul.Core.Loc.Text("본편이 안 가져간 화물 {0}/{1}건. 다 차면 발송이 막힙니다.", pending, cap),
                        Theme.FontSmall, pending >= cap ? Theme.Danger : Theme.Warn);
                    Ui.Size(warn.gameObject, 62f);
                }
            }

            var sync = Ui.Button("Sync", _linkBody, AfterSeoul.Core.Loc.Text("배송함 동기화"), OnMailSync, Theme.Line, Theme.FontSmall);
            Ui.Size(sync.gameObject, 72f);
            var unlink = Ui.Button("Unlink", _linkBody, AfterSeoul.Core.Loc.Text("로그아웃"), OnUnlink, Theme.Line, Theme.FontSmall);
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
                shipment.Claimed ? AfterSeoul.Core.Loc.Text("수령됨") : (string.IsNullOrEmpty(shipment.AccountId) ? AfterSeoul.Core.Loc.Text("개발용 기록 · 전송 안 됨") : shipment.Uploaded ? AfterSeoul.Core.Loc.Text("본편 수령 대기") : AfterSeoul.Core.Loc.Text("전송 대기")),
                Theme.FontSmall, TextAnchor.MiddleRight,
                shipment.Claimed ? Theme.Safe : Theme.Info);
            Ui.Size(state.gameObject, width: 170f, flexWidth: 0f);
        }

        /// <summary>"3일" / "5시간" / "방금". 분 단위까지 적으면 목록이 시끄러워진다.</summary>
        private static string WaitedText(System.TimeSpan waited)
        {
            if (waited.TotalDays >= 1) return AfterSeoul.Core.Loc.Text("{0}일", (int)waited.TotalDays);
            if (waited.TotalHours >= 1) return AfterSeoul.Core.Loc.Text("{0}시간", (int)waited.TotalHours);
            if (waited.TotalMinutes >= 1) return AfterSeoul.Core.Loc.Text("{0}분", (int)waited.TotalMinutes);
            return AfterSeoul.Core.Loc.Text("방금");
        }

        private void OnLink()
        {
            // 같은 계정 로그인과 서버 준비 확인이 모두 성공해야 발송을 연다.
            Session.LinkToMainline(code: null, done: (ok, message) =>
            {
                Shell.Toast(
                    ok ? AfterSeoul.Core.Loc.Text("연결했습니다. 창고에서 보급품을 보낼 수 있습니다.") : message,
                    ok ? 3.5f : 5f);

                if (ok) Sfx.Complete();
                Shell.AfterAction();
                if (ok) OnMailSync();
            });
        }

        private void OnMailSync()
        {
            var link = Session.MailLink as AfterSeoul.Mail.IAccountMailLink;
            link?.Sync((ok, message) => { Shell.Toast(message, 4f); Shell.AfterAction(); });
        }

        private void OnUnlink()
        {
            Session.UnlinkFromMainline();
            Shell.Toast(AfterSeoul.Core.Loc.Text("로그아웃했습니다. 연결 계정과 보낸 기록은 유지됩니다."), 3f);
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

            AddLine(_statusBody, AfterSeoul.Core.Loc.Text("창고 {0} / {1} 칸", save.Warehouse.Stacks.Count, save.Warehouse.TotalCapacity),
                save.Warehouse.Stacks.Count >= save.Warehouse.TotalCapacity ? Theme.Danger : Theme.Text);
            AddLine(_statusBody, AfterSeoul.Core.Loc.Text("제작 진행 중 {0}건", pendingCraft), Theme.Text);

            if (save.Scavs.Count == 0)
                AddLine(_statusBody, AfterSeoul.Core.Loc.Text("고용한 스캐브 없음"), Theme.TextFaint);
            else
                AddLine(_statusBody, AfterSeoul.Core.Loc.Text("스캐브 대기 {0} · 탐색중 {1} · 부상 {2}", idle, away, hurt),
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
