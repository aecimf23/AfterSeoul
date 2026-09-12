using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI.Screens
{
    /// <summary>
    /// 탐색. GDD §31 — 팀을 고르고 지역으로 보낸다.
    ///
    /// <para>구성이 <b>팀 먼저, 지역 나중</b>인 이유: 어느 지역에 보낼지는 누가 비어 있느냐에
    /// 달려 있다. 지역을 먼저 고르게 하면 "여긴 누굴 보내지"를 화면 두 개를 오가며 풀어야 한다.
    /// 한 화면에서 위(팀)를 정하고 아래(지역)를 누르면 끝난다.</para>
    ///
    /// <para>선택 상태는 화면 객체가 들고 있고 세이브에 넣지 않는다. 보내기 전의 선택은
    /// 게임 상태가 아니라 화면 상태다 — 저장하는 순간 앱을 껐다 켤 때 되살릴 의무가 생긴다.</para>
    /// </summary>
    public sealed class ExpeditionScreen : ScreenBase
    {
        public override string TabName => "탐색";
        public override IconSet.TabGlyph Glyph => IconSet.TabGlyph.Expedition;
        public override string Title => "탐색 — 서울";

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

        protected override void Build()
        {
            var host = Ui.Rect("Host", Root);
            Ui.Stretch(host, Theme.Gutter, Theme.Gutter, 16f, 16f);
            _list = Ui.ScrollList("Scroll", host, out var scroll, 14f);
        }

        public override void Refresh()
        {
            if (_list == null) return;
            PruneSelection();
            Ui.Clear(_list);

            var maps = SortedMaps();
            if (maps.Count == 0)
            {
                var err = Ui.Paragraph("Err", _list,
                    "지역 데이터를 읽지 못했습니다.\nStreamingAssets/Data/expeditions.json 을 확인하세요.",
                    Theme.FontBody, Theme.Danger);
                Ui.Size(err.gameObject, 140f);
                return;
            }

            BuildActiveExpeditions();
            BuildRescues();       // 시한이 있다. 지역 목록 아래에 묻으면 놓친다.
            BuildTeamPicker();

            foreach (var map in maps)
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
            var card = Ui.Card(_list, "무전 포착", out body);
            Ui.Size(card.gameObject, 90f + rescuable.Count * 220f);

            foreach (var lost in rescuable) BuildRescueRow(body, lost);
        }

        private void BuildRescueRow(RectTransform parent, ScavState lost)
        {
            var map = Session.Data.GetMap(lost.LostAtMapId);
            var left = RescueSystem.RemainingWindow(lost, Session.Clock.UtcNow);

            var head = Ui.Label("R_" + lost.Uid, parent,
                $"{lost.Name} — {Loc.MapName(lost.LostAtMapId)}에서 신호",
                Theme.FontBody, TextAnchor.MiddleLeft, Theme.Warn);
            Ui.Size(head.gameObject, 48f);

            int need = RescueSystem.RequiredSurvival(map);
            int have = SelectedSurvival();

            var detail = Ui.Label("RD_" + lost.Uid, parent,
                $"신호 {(int)left.TotalHours}시간 {left.Minutes}분 남음   ·   "
                + $"데려오려면 생존 {need} 필요 (지금 팀 {have})",
                Theme.FontSmall, TextAnchor.MiddleLeft,
                have >= need ? Theme.TextDim : Theme.Warn);
            Ui.Size(detail.gameObject, 42f);

            string uid = lost.Uid;
            var team = new List<string>(_selected);

            string block = team.Count == 0
                ? "먼저 아래에서 갈 사람을 고르세요"
                : ExpeditionSystem.DepartBlockReason(Session.Save, Session.Data, lost.LostAtMapId, team);

            var btn = Ui.Button("RB_" + lost.Uid, parent,
                block == null
                    ? (have >= need ? "데리러 간다" : "데리러 간다 — 생존이 모자랍니다")
                    : "보낼 수 없음",
                () => OnRescue(uid), block == null ? Theme.Danger : Theme.Line, Theme.FontSmall);
            btn.interactable = block == null;
            Ui.Size(btn.gameObject, 84f);

            if (block != null)
            {
                var why = Ui.Label("RW_" + lost.Uid, parent, block, Theme.FontSmall,
                    TextAnchor.MiddleLeft, Theme.TextFaint);
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
                Shell.Toast("보내지 못했습니다 — 인원과 자금을 확인하세요", 3f);
                return;
            }

            _selected.Clear();
            Shell.Toast("구조대가 출발했습니다", 3f);
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

        private List<MapDef> SortedMaps()
        {
            var list = new List<MapDef>();

            // JsonDataRegistry 는 목록을 공개한다. 인터페이스에 열거 API 를 더하면
            // 테스트 스텁까지 같이 고쳐야 해서, 화면 하나 때문에 늘리지 않는다.
            var json = Session.Data as JsonDataRegistry;
            if (json != null)
                foreach (var m in json.Maps) list.Add(m);

            list.Sort((a, b) =>
            {
                int c = a.Tier.CompareTo(b.Tier);
                if (c != 0) return c;
                c = a.DurationMinutes.CompareTo(b.DurationMinutes);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
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
            Ui.Card(_list, "파견 중", out body);

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
                (rescue ? "[구조] " : "") + $"{Loc.MapName(exp.MapId)}   ({TeamNames(exp)})",
                Theme.FontSmall, TextAnchor.MiddleLeft, rescue ? Theme.Warn : Theme.Text);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var time = Ui.Label("Time", head, "", Theme.FontSmall, TextAnchor.MiddleRight, Theme.Info);
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
                        ? $"{(int)left.TotalHours}시간 {left.Minutes}분 남음"
                        : $"{left.Minutes}분 {left.Seconds}초 남음";
                    continue;
                }

                if (row.MarkedDone) continue;
                row.MarkedDone = true;

                row.Time.text = "복귀 — 곧 정산";
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
                    if (s.Uid == uid) { names.Add(s.Name); break; }
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
                    ? "팀 편성"
                    : $"팀 편성   ·   {_selected.Count}명   ·   시급 합계 {Theme.Won(wageSum)}",
                out body);

            if (Session.Save.Scavs.Count == 0)
            {
                var none = Ui.Paragraph("NoScav", body,
                    "고용한 스캐브가 없습니다. 인원 화면에서 먼저 고용하세요.",
                    Theme.FontSmall, Theme.TextDim);
                Ui.Size(none.gameObject, 50f);

                var go = Ui.Button("GoHire", body, "인원 화면으로", () => Shell.SelectByName("인원"));
                Ui.Size(go.gameObject, 80f);
                return;
            }

            if (idle.Count == 0)
            {
                var none = Ui.Paragraph("NoIdle", body,
                    "대기 중인 스캐브가 없습니다. 전원 파견 중이거나 회복 중입니다.",
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

            var name = Ui.Label("Name", row, scav.Name, Theme.FontBody,
                TextAnchor.MiddleLeft, on ? Theme.Text : Theme.TextDim);
            Ui.Size(name.gameObject, width: 230f, flexWidth: 0f);

            // 무기가 없으면 능력치 대신 그걸 먼저 말한다. 능력치가 아무리 좋아도 못 나간다.
            var gear = AfterSeoul.Scav.Equipment.EffectsOf(scav, Session.Data);
            var stats = Ui.Label("Stats", row,
                gear.HasWeapon
                    ? $"탐 {scav.Search}   전 {scav.Combat}   생 {scav.Survival}"
                    : "무기 없음 — 인원 화면에서 지급",
                Theme.FontSmall, TextAnchor.MiddleRight,
                gear.HasWeapon ? Theme.TextDim : Theme.Warn);
            Ui.Size(stats.gameObject, flexWidth: 1f);
        }

        private void Toggle(string uid)
        {
            if (!_selected.Remove(uid)) _selected.Add(uid);
            Refresh();
        }

        // ── 지역 ─────────────────────────────────────────────────

        private void BuildMapCard(MapDef map)
        {
            var card = Ui.Rect("Map_" + map.Id, _list);
            var img = card.gameObject.AddComponent<Image>();
            img.color = Theme.Panel;
            img.raycastTarget = false;
            Ui.Column(card, 8f, new RectOffset(20, 20, 16, 18));

            // 제목 줄: 지역명 + 위험도
            var head = Ui.Rect("Head", card);
            Ui.Size(head.gameObject, 54f);
            Ui.Row(head, 8f);

            var name = Ui.Label("Name", head, Loc.MapName(map.Id), Theme.FontHeading);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var risk = Ui.Label("Risk", head, Theme.RiskStars(map.RiskLevel), Theme.FontSmall,
                TextAnchor.MiddleRight, Theme.RiskColor(map.RiskLevel));
            Ui.Size(risk.gameObject, width: 220f, flexWidth: 0f);

            // 정보 줄. 파견비는 팀에 따라 달라지므로 여기엔 1인 기준값만 적고,
            // 실제 청구액은 아래 버튼에 띄운다.
            var info = Ui.Label("Info", card,
                $"예상 {FormatDuration(map.DurationMinutes)}   ·   1인 기준 {Theme.Won(ExpeditionSystem.BaselineCost(map))}   ·   교전 {map.CombatChance * 100f:0}%",
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
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

                var loot = Ui.Paragraph("Loot", card, "주요 발견  " + string.Join(" · ", names.ToArray()),
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
                block == null ? $"파견   {team.Count}명   {Theme.Won(cost)}" : "파견 불가",
                () => OnDepart(mapId),
                block == null ? Theme.Accent : Theme.Line);
            btn.interactable = block == null;
            Ui.Size(btn.gameObject, 92f);

            if (block != null)
            {
                var why = Ui.Label("Why", card, block, Theme.FontSmall,
                    TextAnchor.MiddleLeft, unlocked ? Theme.TextDim : Theme.Warn);
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
                string reason = ExpeditionSystem.DepartBlockReason(
                    Session.Save, Session.Data, mapId, team);
                Shell.Toast(reason ?? "파견하지 못했습니다", 3f);
                Shell.AfterAction();
                return;
            }

            _selected.Clear();

            // 알림 권한은 여기서 묻는다. 첫 화면에서 물으면 무엇에 대한 허락인지 모르지만,
            // 방금 사람을 내보낸 참이면 "돌아오면 알려줄까"가 자연스럽다.
            AndroidNotifications.RequestPermissionIfNeeded();

            var left = exp.ReturnsAt - Session.Clock.UtcNow;
            int minutes = (int)System.Math.Ceiling(left.TotalMinutes);
            Shell.Toast($"{Loc.MapName(mapId)} 출발 — 약 {minutes}분 뒤 복귀", 3.5f);
            Shell.AfterAction();
        }

        private static string FormatDuration(int minutes)
        {
            if (minutes < 60) return minutes + "분";
            int h = minutes / 60, m = minutes % 60;
            return m == 0 ? h + "시간" : $"{h}시간 {m}분";
        }
    }
}
