using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Exploration;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI.Screens
{
    /// <summary>Opened regions appear on the map; tapping one opens departure details.</summary>
    public sealed class ExpeditionScreen : ScreenBase
    {
        public override string TabName => "탐색";
        public override IconSet.TabGlyph Glyph => IconSet.TabGlyph.Expedition;
        public override string Title => AfterSeoul.Core.Loc.Text("탐색 — 서울");

        private RectTransform _list;

        /// <summary>지금 편성된 팀.</summary>
        private readonly HashSet<string> _selected = new HashSet<string>();

        /// <summary>나가 있는 팀의 살아 있는 줄들. 매 프레임 막대를 민다.</summary>
        private readonly List<RunRow> _runRows = new List<RunRow>();

        private sealed class RunRow
        {
            public ExpeditionState Exp;
            public ProgressBar Bar;
            public Text Time;
            public RectTransform Surface;

            /// <summary>복귀 표시를 이미 했는가. 매 프레임 같은 글자를 다시 넣지 않으려고 둔다.</summary>
            public bool MarkedDone;
        }

        private RectTransform _mapHost, _modal;
        private Text _teamLabel;
        private string _detailMap, _modalView;

        protected override void Build()
        {
            var host = Ui.Rect("Host", Root);
            Ui.Stretch(host, Theme.Gutter, Theme.Gutter, 16f, 16f);
            var controls = Ui.Rect("MapControls", host);
            controls.anchorMin = new Vector2(0, 1); controls.anchorMax = Vector2.one;
            controls.pivot = new Vector2(.5f, 1); controls.sizeDelta = new Vector2(0, 88);
            Ui.Row(controls, 12);
            var team = Ui.Button("ChooseTeam", controls, Loc.Text("팀 편성"), () => { _detailMap = null; ShowTeam(); });
            Ui.Size(team.gameObject, flexWidth: 1);
            _teamLabel = team.GetComponentInChildren<Text>();
            var runs = Ui.Button("Operations", controls, Loc.Text("파견 현황 · 구조"), ShowOperations, Theme.Panel);
            Ui.Size(runs.gameObject, flexWidth: 1);
            _mapHost = Ui.Rect("MapHost", host);
            Ui.Stretch(_mapHost, 0, 0, 110, 0);
        }

        public override void Refresh()
        {
            if (_mapHost == null) return;
            PruneSelection(); Ui.Clear(_mapHost);
            _teamLabel.text = Loc.Text("팀 편성 · {0}명", _selected.Count);
            var maps = new List<MapDef>();
            foreach (var map in ExplorationSystem.OrderedMaps(Session.Data))
                if (MapUnlock.IsUnlocked(Session.Save, map)) maps.Add(map);
            var overview = SeoulMapSelection.Draw(_mapHost, maps, "MapSelect_", ShowMap);
            Ui.Stretch(overview);
            if (_modal != null) {
                if (_detailMap != null && !MapUnlock.IsUnlocked(Session.Save, Session.Data.GetMap(_detailMap))) { CloseDetail(); _detailMap = null; }
                else if (_modalView == "team") ShowTeam();
                else if (_modalView == "operations") ShowOperations();
                else if (_detailMap != null) ShowMap(_detailMap);
            }
        }

        private void CloseDetail()
        {
            if (_modal != null) { _modal.gameObject.SetActive(false); if (Application.isPlaying) Object.Destroy(_modal.gameObject); else Object.DestroyImmediate(_modal.gameObject); }
            _modal = null; _list = null; _runRows.Clear();
        }

        private void OpenDetail(string title)
        {
            CloseDetail();
            _modal = Ui.Modal("ExpeditionDetail", Root, title, CloseDetail, out _list);
        }

        private void ShowTeam()
        {
            OpenDetail(Loc.Text("파견 팀 편성")); _modalView = "team";
            BuildTeamPicker(); BuildOrientation();
            var done = Ui.Button("TeamReady", _list, Loc.Text("편성 완료"), () => {
                if (_detailMap != null) ShowMap(_detailMap); else CloseDetail();
            });
            Ui.Size(done.gameObject, 88);
        }

        private void ShowOperations()
        {
            _detailMap = null;
            OpenDetail(Loc.Text("파견 현황 · 구조")); _modalView = "operations";
            BuildActiveExpeditions(); BuildRescues();
            if (_list.childCount == 0) Ui.Size(Ui.Paragraph("NoOperations", _list, Loc.Text("현재 파견 중인 팀이 없습니다.")).gameObject, 80);
        }

        private void ShowMap(string id)
        {
            var map = Session.Data.GetMap(id);
            if (map == null || !MapUnlock.IsUnlocked(Session.Save, map)) return;
            _detailMap = id;
            OpenDetail(Loc.MapName(id)); _modalView = "map";
            var team = Ui.Button("EditMapTeam", _list, Loc.Text("팀 편성 · {0}명", _selected.Count), ShowTeam, Theme.Panel);
            Ui.Size(team.gameObject, 88);
            BuildMapCard(map);
        }

        // ── 실종자 구조 (GDD §15) ────────────────────────────────

        /// <summary>
        /// 무전이 잡힌 실종자.
        ///
        /// <para><b>시한이 있어서 위에 둔다.</b> 지역 카드 아래에 묻어두면 놓치고, 놓치면 그 사람은
        /// 영영 안 돌아온다 — 되돌릴 수 없는 것을 조용히 지나가게 두면 안 된다.</para>
        /// </summary>
        private void BuildRescues()
        {
            var rescuable = Session.RescuableScavs();
            if (rescuable.Count == 0) return;

            RectTransform body;
            var card = Ui.Card(_list, AfterSeoul.Core.Loc.Text("무전 포착"), out body);
            Ui.Size(card.gameObject, 90f + rescuable.Count * 220f);

            foreach (var lost in rescuable) BuildRescueRow(body, lost);
        }

        private void BuildRescueRow(RectTransform parent, ScavState lost)
        {
            var map = Session.Data.GetMap(lost.LostAtMapId);
            var left = RescueSystem.RemainingWindow(lost, Session.Clock.UtcNow);

            var head = Ui.Label("R_" + lost.Uid, parent,
                AfterSeoul.Core.Loc.Text("{0} — {1}에서 신호", AfterSeoul.Core.Loc.Text(lost.Name), Loc.MapName(lost.LostAtMapId)),
                Theme.FontBody, TextAnchor.MiddleLeft, Theme.Warn);
            Ui.Size(head.gameObject, 48f);

            int need = RescueSystem.RequiredSurvival(map);
            int have = SelectedSurvival();

            var detail = Ui.Label("RD_" + lost.Uid, parent,
                AfterSeoul.Core.Loc.Text("신호 {0}시간 {1}분 남음   ·   ", (int)left.TotalHours, left.Minutes)
                + AfterSeoul.Core.Loc.Text("데려오려면 생존 {0} 필요 (지금 팀 {1})", need, have),
                Theme.FontSmall, TextAnchor.MiddleLeft,
                have >= need ? Theme.TextDim : Theme.Warn);
            detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            detail.verticalOverflow = VerticalWrapMode.Truncate;
            detail.resizeTextMinSize = 18;
            detail.resizeTextMaxSize = detail.fontSize;
            detail.resizeTextForBestFit = true;
            Ui.Size(detail.gameObject, 42f);

            string uid = lost.Uid;
            var team = new List<string>(_selected);

            string block = team.Count == 0
                ? AfterSeoul.Core.Loc.Text("팀 편성에서 갈 사람을 고르세요")
                : ExpeditionSystem.DepartBlockReason(Session.Save, Session.Data, lost.LostAtMapId, team);

            var btn = Ui.Button("RB_" + lost.Uid, parent,
                block == null
                    ? (have >= need ? AfterSeoul.Core.Loc.Text("데리러 간다") : AfterSeoul.Core.Loc.Text("데리러 간다 — 생존이 모자랍니다"))
                    : AfterSeoul.Core.Loc.Text("보낼 수 없음"),
                () => OnRescue(uid), block == null ? Theme.Danger : Theme.Line, Theme.FontSmall);
            btn.interactable = block == null;
            Ui.Size(btn.gameObject, 84f);

            if (block != null)
            {
                var why = Ui.Label("RW_" + lost.Uid, parent, block, Theme.FontSmall,
                    TextAnchor.MiddleLeft, Theme.TextFaint);
                why.horizontalOverflow = HorizontalWrapMode.Wrap;
                why.verticalOverflow = VerticalWrapMode.Truncate;
                why.resizeTextMinSize = 18;
                why.resizeTextMaxSize = why.fontSize;
                why.resizeTextForBestFit = true;
                Ui.Size(why.gameObject, 38f);
            }
        }

        private int SelectedSurvival()
        {
            var team = new List<string>(_selected);

            int total = 0;
            foreach (var uid in team)
                foreach (var s in Session.Save.Scavs)
                    if (s.Uid == uid) { total += s.Survival; break; }

            total += Scav.Equipment.TeamEffectsOf(Session.Save, Session.Data, team).SurvivalBonus;
            return total;
        }

        private void OnRescue(string missingUid)
        {
            var team = new List<string>(_selected);
            if (Session.DepartRescue(missingUid, team) == null)
            {
                Shell.Toast(AfterSeoul.Core.Loc.Text("보내지 못했습니다 — 인원과 자금을 확인하세요"), 3f);
                return;
            }

            _selected.Clear();
            CloseDetail();
            Shell.Toast(AfterSeoul.Core.Loc.Text("구조대가 출발했습니다"), 3f);
            Shell.AfterAction();
        }

        /// <summary>
        /// 대기 상태가 아니게 된 스캐브를 선택에서 뺀다.
        ///
        /// <para>방금 보낸 팀이 선택에 남아 있으면 다음 지역 버튼이 전부 회색이 되는데,
        /// 사유가 "이미 나가 있어서"라 화면만 봐서는 이유를 알 수가 없다.</para>
        /// </summary>
        private void PruneSelection()
        {
            if (_selected.Count == 0) return;

            var idle = new HashSet<string>();
            foreach (var s in Session.Save.Scavs)
                if (s.Status == ScavStatus.Idle) idle.Add(s.Uid);

            _selected.IntersectWith(idle);
        }

        // ── 파견 중 ──────────────────────────────────────────────

        /// <summary>
        /// 나가 있는 팀들. 이 카드가 이 화면에서 제일 자주 쳐다보는 곳이다 —
        /// 파견을 보내고 나면 할 일이 "언제 오나"를 확인하는 것뿐이기 때문이다.
        ///
        /// <para>그래서 숫자만 두지 않고 막대를 깐다. 두 팀이 서로 다른 지역에 나가 있을 때
        /// 어느 쪽이 먼저 오는지가 읽지 않아도 보인다.</para>
        /// </summary>
        private void BuildActiveExpeditions()
        {
            _runRows.Clear();

            var save = Session.Save;
            var running = new List<ExpeditionState>();
            foreach (var e in save.Expeditions)
                if (!e.Resolved) running.Add(e);

            if (running.Count == 0) return;

            RectTransform body;
            Ui.Card(_list, AfterSeoul.Core.Loc.Text("파견 중"), out body);

            for (int i = 0; i < running.Count; i++) BuildRunRow(body, running[i], i);
            TickRuns();
        }

        private void BuildRunRow(RectTransform parent, ExpeditionState exp, int index)
        {
            bool rescue = !string.IsNullOrEmpty(exp.RescueScavUid);

            var surface = Ui.Surface("Run" + index, parent, Theme.PanelAlt);
            Ui.Size(surface.gameObject, 92f);
            Ui.Column(surface, 6f, new RectOffset(16, 16, 10, 12));

            var head = Ui.Rect("Head", surface);
            Ui.Size(head.gameObject, 44f);
            Ui.Row(head, 8f);

            var name = Ui.Label("Map", head,
                (rescue ? AfterSeoul.Core.Loc.Text("[구조] ") : "") + $"{Loc.MapName(exp.MapId)}   ({TeamNames(exp)})",
                Theme.FontSmall, TextAnchor.MiddleLeft, rescue ? Theme.Warn : Theme.Text);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var time = Ui.Label("Time", head, "", Theme.FontSmall, TextAnchor.MiddleRight, Theme.Info);
            time.horizontalOverflow = HorizontalWrapMode.Wrap;
            time.verticalOverflow = VerticalWrapMode.Truncate;
            time.resizeTextMinSize = 18;
            time.resizeTextMaxSize = time.fontSize;
            time.resizeTextForBestFit = true;
            Ui.Size(time.gameObject, width: 330f, flexWidth: 0f);

            var bar = Ui.Bar(surface, 8f, rescue ? Theme.Warn : Theme.Info);

            _runRows.Add(new RunRow { Exp = exp, Bar = bar, Time = time, Surface = surface });
        }

        /// <summary>매 프레임. 남은 시간과 막대를 민다 — <see cref="Refresh"/> 는 5초에 한 번뿐이다.</summary>
        private void TickRuns()
        {
            if (_runRows.Count == 0) return;

            var now = Session.Clock.UtcNow;

            foreach (var row in _runRows)
            {
                if (row.Bar == null || !row.Bar.Alive || row.Time == null) continue;

                double total = (row.Exp.ReturnsAt - row.Exp.DepartedAt).TotalSeconds;
                var left = row.Exp.ReturnsAt - now;

                float value = total <= 0.0
                    ? 1f
                    : Mathf.Clamp01(1f - (float)(left.TotalSeconds / total));
                row.Bar.Set(value);

                if (left.TotalSeconds > 0)
                {
                    row.Time.text = left.TotalHours >= 1
                        ? AfterSeoul.Core.Loc.Text("{0}시간 {1}분 남음", (int)left.TotalHours, left.Minutes)
                        : AfterSeoul.Core.Loc.Text("{0}분 {1}초 남음", left.Minutes, left.Seconds);
                    continue;
                }

                if (row.MarkedDone) continue;
                row.MarkedDone = true;

                row.Time.text = AfterSeoul.Core.Loc.Text("복귀 — 곧 정산");
                row.Time.color = Theme.Safe;
                row.Bar.SetColor(Theme.Safe);
                row.Bar.SetPulsing(true);
                Ui.SetEdge(row.Surface, Theme.EdgeLive);
            }
        }

        public override void Tick(float deltaTime) => TickRuns();

        private string TeamNames(ExpeditionState exp)
        {
            var names = new List<string>();
            foreach (var uid in exp.ScavUids)
                foreach (var s in Session.Save.Scavs)
                    if (s.Uid == uid) { names.Add(AfterSeoul.Core.Loc.Text(s.Name)); break; }
            return names.Count == 0 ? "?" : string.Join(", ", names.ToArray());
        }

        // ── 팀 편성 ──────────────────────────────────────────────

        private void BuildTeamPicker()
        {
            var idle = new List<ScavState>();
            foreach (var s in Session.Save.Scavs)
                if (s.Status == ScavStatus.Idle) idle.Add(s);

            // 시급 합계를 같이 보여준다. 파견비의 인건비 쪽이 이 값에 비례하므로,
            // 비싼 사람을 넣으면 아래 파견 버튼의 금액이 왜 뛰는지가 여기서 설명된다.
            long wageSum = 0;
            foreach (var s in Session.Save.Scavs)
                if (_selected.Contains(s.Uid)) wageSum += s.WagePerHour;

            RectTransform body;
            Ui.Card(_list,
                _selected.Count == 0
                    ? AfterSeoul.Core.Loc.Text("팀 편성")
                    : AfterSeoul.Core.Loc.Text("팀 편성   ·   {0}명   ·   시급 합계 {1}", _selected.Count, Theme.Won(wageSum)),
                out body);

            if (Session.Save.Scavs.Count == 0)
            {
                var none = Ui.Paragraph("NoScav", body,
                    AfterSeoul.Core.Loc.Text("고용한 스캐브가 없습니다. 인원 화면에서 먼저 고용하세요."),
                    Theme.FontSmall, Theme.TextDim);
                Ui.Size(none.gameObject, 50f);

                var go = Ui.Button("GoHire", body, AfterSeoul.Core.Loc.Text("인원 화면으로"), () => Shell.SelectByName("인원"));
                Ui.Size(go.gameObject, 80f);
                return;
            }

            if (idle.Count == 0)
            {
                var none = Ui.Paragraph("NoIdle", body,
                    AfterSeoul.Core.Loc.Text("대기 중인 스캐브가 없습니다. 전원 파견 중이거나 회복 중입니다."),
                    Theme.FontSmall, Theme.TextDim);
                Ui.Size(none.gameObject, 50f);
                return;
            }

            foreach (var scav in idle)
                BuildPickRow(body, scav);
        }

        private void BuildPickRow(RectTransform parent, ScavState scav)
        {
            bool on = _selected.Contains(scav.Uid);
            string uid = scav.Uid;

            // 라벨을 빈 문자열로 두고 내용을 직접 얹는다. Ui.Button 이 만든 라벨을 지우면
            // Destroy 가 프레임 끝까지 미뤄져서 이번 프레임 레이아웃에는 그대로 남는다.
            var btn = Ui.Button("Pick_" + uid, parent, "", () => Toggle(uid),
                on ? Theme.AccentDim : Theme.PanelAlt);
            Ui.Size(btn.gameObject, 88f);

            var row = Ui.Rect("Content", btn.transform);
            Ui.Stretch(row, 18f, 18f, 0f, 0f);
            Ui.Row(row, 10f);

            var mark = Ui.Label("Mark", row, on ? "■" : "□", Theme.FontHeading,
                TextAnchor.MiddleCenter, on ? Theme.Accent : Theme.TextFaint);
            Ui.Size(mark.gameObject, width: 48f, flexWidth: 0f);

            var name = Ui.Label("Name", row, AfterSeoul.Core.Loc.Text(scav.Name), Theme.FontBody,
                TextAnchor.MiddleLeft, on ? Theme.Text : Theme.TextDim);
            Ui.Size(name.gameObject, width: 230f, flexWidth: 0f);

            // 무기가 없으면 능력치 대신 그걸 먼저 말한다. 능력치가 아무리 좋아도 못 나간다.
            var gear = AfterSeoul.Scav.Equipment.EffectsOf(scav, Session.Data);
            var stats = Ui.Label("Stats", row,
                gear.HasWeapon
                    ? AfterSeoul.Core.Loc.Text("탐 {0}   전 {1}   생 {2}", scav.Search, scav.Combat, scav.Survival)
                    : AfterSeoul.Core.Loc.Text("무기 없음 — 인원 화면에서 지급"),
                Theme.FontSmall, TextAnchor.MiddleRight,
                gear.HasWeapon ? Theme.TextDim : Theme.Warn);
            stats.horizontalOverflow = HorizontalWrapMode.Wrap;
            stats.verticalOverflow = VerticalWrapMode.Truncate;
            stats.resizeTextMinSize = 18;
            stats.resizeTextMaxSize = stats.fontSize;
            stats.resizeTextForBestFit = true;
            Ui.Size(stats.gameObject, flexWidth: 1f);
        }

        private void Toggle(string uid)
        {
            if (!_selected.Remove(uid)) _selected.Add(uid);
            Refresh();
            ShowTeam();
        }

        // ── 지역 ─────────────────────────────────────────────────

        private void BuildOrientation()
        {
            if (Session.Save.Orientation == null || Session.Save.Orientation.Stage != OrientationStage.Pending) return;
            RectTransform body;
            Ui.Card(_list, AfterSeoul.Core.Loc.Text("초도 보급 · 첫 출동"), out body);
            var hint = Ui.Paragraph("Brief", body,
                AfterSeoul.Core.Loc.Text("명동 외곽의 확인된 보급 경로입니다. 1명이 3분 동안 다녀옵니다.\n비용 없음 · 부상과 실종 없음 · 장비 보존\n꾸러미를 기지에 납품하면 50,000원을 받습니다."),
                Theme.FontBody, Theme.Text);
            Ui.Size(hint.gameObject, 190f);
            var team = new List<string>(_selected);
            var reason = Orientation.BlockReason(Session.Save, Session.Data, team);
            var go = Ui.Button("OrientationDepart", body, AfterSeoul.Core.Loc.Text("보급 꾸러미 회수하러 출발"), () => {
                var exp = Session.DepartOrientation(new List<string>(_selected));
                if (exp == null) {
                    Sfx.Error();
                    Shell.Toast(Orientation.BlockReason(Session.Save, Session.Data, new List<string>(_selected)) ?? AfterSeoul.Core.Loc.Text("출발하지 못했습니다"));
                } else {
                    _selected.Clear();
            CloseDetail();
                    AndroidNotifications.RequestPermissionIfNeeded();
                    Sfx.Confirm();
                    Shell.Toast(AfterSeoul.Core.Loc.Text("초도 보급 출발 — 3분 뒤 안전하게 복귀합니다"), 3.5f);
                }
                Shell.AfterAction();
            }, reason == null ? Theme.Accent : Theme.Line);
            go.interactable = reason == null;
            Ui.Size(go.gameObject, 88f);
            var note = Ui.Paragraph("Reason", body,
                reason ?? AfterSeoul.Core.Loc.Text("첫 출동에만 제공되는 지원입니다."), Theme.FontSmall, Theme.TextDim);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            note.verticalOverflow = VerticalWrapMode.Truncate;
            note.resizeTextMinSize = 18;
            note.resizeTextMaxSize = note.fontSize;
            note.resizeTextForBestFit = true;
            Ui.Size(note.gameObject, 48f);
            var regular = Ui.Paragraph("RegularWarning", body,
                AfterSeoul.Core.Loc.Text("일반 파견으로 먼저 출발하면 초도 보급 지원은 종료됩니다. 일반 파견에는 비용과 사고 위험이 있습니다."),
                Theme.FontSmall, Theme.Warn);
            regular.horizontalOverflow = HorizontalWrapMode.Wrap;
            regular.verticalOverflow = VerticalWrapMode.Truncate;
            regular.resizeTextMinSize = 18;
            regular.resizeTextMaxSize = regular.fontSize;
            regular.resizeTextForBestFit = true;
            Ui.Size(regular.gameObject, 80f);
        }

        private void BuildMapCard(MapDef map)
        {
            var card = Ui.Rect("Map_" + map.Id, _list);
            var img = card.gameObject.AddComponent<Image>();
            img.color = Theme.Panel;
            img.raycastTarget = false;
            Ui.Column(card, 8f, new RectOffset(20, 20, 16, 18));

            var mapArtwork = GameArt.MapThumbnail(card, map.Id);
            Ui.Size(mapArtwork.gameObject, 180);

            // 제목 줄: 지역명 + 위험도
            var head = Ui.Rect("Head", card);
            Ui.Size(head.gameObject, 54f);
            Ui.Row(head, 8f);

            var name = Ui.Label("Name", head, Loc.MapName(map.Id), Theme.FontHeading);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var risk = Ui.Label("Risk", head, Theme.RiskStars(map.RiskLevel), Theme.FontSmall,
                TextAnchor.MiddleRight, Theme.RiskColor(map.RiskLevel));
            risk.horizontalOverflow = HorizontalWrapMode.Wrap;
            risk.verticalOverflow = VerticalWrapMode.Truncate;
            risk.resizeTextMinSize = 18;
            risk.resizeTextMaxSize = risk.fontSize;
            risk.resizeTextForBestFit = true;
            Ui.Size(risk.gameObject, width: 220f, flexWidth: 0f);

            // 정보 줄. 파견비는 팀에 따라 달라지므로 여기엔 1인 기준값만 적고,
            // 실제 청구액은 아래 버튼에 띄운다.
            var info = Ui.Label("Info", card,
                AfterSeoul.Core.Loc.Text("예상 {0}   ·   1인 기준 {1}   ·   교전 {2:0}%", FormatDuration(map.DurationMinutes), Theme.Won(ExpeditionSystem.BaselineCost(map)), map.CombatChance * 100f),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            info.verticalOverflow = VerticalWrapMode.Truncate;
            info.resizeTextMinSize = 18;
            info.resizeTextMaxSize = info.fontSize;
            info.resizeTextForBestFit = true;
            Ui.Size(info.gameObject, 42f);

            // 발견 가능 품목: 가중치 높은 순 상위 5개
            var table = Session.Data.GetLootTable(map.LootTableId);
            if (table != null && table.Entries.Length > 0)
            {
                var entries = new List<LootEntry>(table.Entries);
                entries.Sort((a, b) => b.Weight.CompareTo(a.Weight));

                var names = new List<string>();
                for (int i = 0; i < entries.Count && i < 5; i++)
                    names.Add(Loc.ItemName(entries[i].ItemId));

                var loot = Ui.Paragraph("Loot", card, AfterSeoul.Core.Loc.Text("주요 발견  ") + string.Join(" · ", names.ToArray()),
                    Theme.FontSmall, Theme.TextFaint);
                Ui.Size(loot.gameObject, 42f);
            }

            // 파견 버튼. 막힌 사유는 ExpeditionSystem 이 내리는 판정을 그대로 쓴다 —
            // 화면이 조건을 따로 쓰면 "눌리는데 아무 일도 안 일어나는" 버튼이 생긴다.
            var team = new List<string>(_selected);
            string block = ExpeditionSystem.DepartBlockReason(Session.Save, Session.Data, map.Id, team);
            bool unlocked = MapUnlock.IsUnlocked(Session.Save, map);
            long cost = ExpeditionSystem.CostFor(Session.Save, Session.Data, map, team);

            string mapId = map.Id;
            var btn = Ui.Button("Depart_" + map.Id, card,
                block == null ? AfterSeoul.Core.Loc.Text("파견   {0}명   {1}", team.Count, Theme.Won(cost)) : AfterSeoul.Core.Loc.Text("파견 불가"),
                () => OnDepart(mapId),
                block == null ? Theme.Accent : Theme.Line);
            btn.interactable = block == null;
            Ui.Size(btn.gameObject, 92f);

            if (block != null)
            {
                var why = Ui.Label("Why", card, block, Theme.FontSmall,
                    TextAnchor.MiddleLeft, unlocked ? Theme.TextDim : Theme.Warn);
                why.horizontalOverflow = HorizontalWrapMode.Wrap;
                why.verticalOverflow = VerticalWrapMode.Truncate;
                why.resizeTextMinSize = 18;
                why.resizeTextMaxSize = why.fontSize;
                why.resizeTextForBestFit = true;
                Ui.Size(why.gameObject, 40f);
            }
        }

        private void OnDepart(string mapId)
        {
            var team = new List<string>(_selected);

            // Session.Depart 는 먼저 정산을 돌린다. 그 사이 복귀·부상 같은 변화가 생겨
            // 조건이 깨질 수 있으므로 결과를 보고 사유를 다시 묻는다.
            var exp = Session.Depart(mapId, team);

            if (exp == null)
            {
                Sfx.Error();
                string reason = ExpeditionSystem.DepartBlockReason(
                    Session.Save, Session.Data, mapId, team);
                Shell.Toast(reason ?? AfterSeoul.Core.Loc.Text("파견하지 못했습니다"), 3f);
                Shell.AfterAction();
                return;
            }

            _selected.Clear();
            CloseDetail();

            // 알림 권한은 여기서 묻는다. 첫 화면에서 물으면 무엇에 대한 허락인지 모르지만,
            // 방금 사람을 내보낸 참이면 "돌아오면 알려줄까"가 자연스럽다.
            AndroidNotifications.RequestPermissionIfNeeded();
            Sfx.Confirm();

            var left = exp.ReturnsAt - Session.Clock.UtcNow;
            int minutes = (int)System.Math.Ceiling(left.TotalMinutes);
            Shell.Toast(AfterSeoul.Core.Loc.Text("{0} 출발 — 약 {1}분 뒤 복귀", Loc.MapName(mapId), minutes), 3.5f);
            Shell.AfterAction();
        }

        private static string FormatDuration(int minutes)
        {
            if (minutes < 60) return minutes + AfterSeoul.Core.Loc.Text("분");
            int h = minutes / 60, m = minutes % 60;
            return m == 0 ? h + AfterSeoul.Core.Loc.Text("시간") : AfterSeoul.Core.Loc.Text("{0}시간 {1}분", h, m);
        }
    }
}
