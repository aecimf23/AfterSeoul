using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI.Screens
{
    /// <summary>
    /// 원작 지도 선택 구조: 지역 선택, 상세 정보, 인원 편성과 출발.
    ///
    /// <para>선택 상태는 화면 객체가 들고 있고 세이브에 넣지 않는다. 보내기 전의 선택은
    /// 게임 상태가 아니라 화면 상태다 — 저장하는 순간 앱을 껐다 켤 때 되살릴 의무가 생긴다.</para>
    /// </summary>
    public sealed class ExpeditionScreen : ScreenBase
    {
        public override string TabName => "탐색";
        public override IconSet.TabGlyph Glyph => IconSet.TabGlyph.Expedition;
        public override string Title => AfterSeoul.Core.Loc.Text("탐색 — 서울");

        private RectTransform _list;
        private ScrollRect _scroll;
        private string _mapId, _rescueId;
        private bool _preparing;

        /// <summary>지금 편성된 팀.</summary>
        private readonly HashSet<string> _selected = new HashSet<string>();

        /// <summary>나가 있는 팀의 살아 있는 줄들. 매 프레임 막대를 민다.</summary>
        private readonly List<RunRow> _runRows = new List<RunRow>();
        private readonly List<MapReturnRow> _mapReturns = new List<MapReturnRow>();
        private sealed class MapReturnRow { public string MapId; public Text Label; }

        private sealed class RunRow
        {
            public ExpeditionState Exp;
            public ProgressBar Bar;
            public Text Time;
            public Text Progress;
            public RectTransform Surface;

            /// <summary>복귀 표시를 이미 했는가. 매 프레임 같은 글자를 다시 넣지 않으려고 둔다.</summary>
            public bool MarkedDone;
        }

        protected override void Build()
        {
            var host = Ui.Rect("Host", Root);
            Ui.Stretch(host, Theme.Gutter, Theme.Gutter, 16f, 16f);
            _list = Ui.ScrollList("Scroll", host, out _scroll, 14f);
        }

        public override void Refresh()
        {
            if (_list == null) return;
            PruneSelection();
            _runRows.Clear();
            _mapReturns.Clear();
            Ui.Clear(_list);

            var maps = SortedMaps();
            if (maps.Count == 0)
            {
                var err = Ui.Paragraph("Err", _list,
                    AfterSeoul.Core.Loc.Text("지역 데이터를 읽지 못했습니다.\nStreamingAssets/Data/expeditions.json 을 확인하세요."),
                    Theme.FontBody, Theme.Danger);
                Ui.Size(err.gameObject, 140f);
                return;
            }

            var selected = maps.Find(m => m.Id == _mapId);
            if (_mapId != null && (selected == null || !MapUnlock.IsUnlocked(Session.Save, selected))) {
                _mapId=null; _rescueId=null; _preparing=false; _selected.Clear();
                Shell.Toast(Loc.Text("지역 정보가 변경되어 지도로 돌아왔습니다."));
            }
            if (_mapId == null) { BuildOverview(maps); return; }
            var back=Ui.Button("ExpeditionBack",_list,Loc.Text(_preparing ? "상세 정보로" : "지역 목록으로"),()=> {
                if (_preparing) _preparing=false;
                else { _mapId=null; _rescueId=null; _selected.Clear(); }
                Navigate();
            },Theme.PanelAlt,28); Ui.Size(back.gameObject,78);
            if (!_preparing) {
                BuildActiveExpeditions(_mapId);
                BuildMapCard(selected);
                Ui.Size(Ui.Button("PrepareExpedition",_list,Loc.Text("파견 준비"),()=> {
                    _preparing=true; Navigate();
                },Theme.Accent,32).gameObject,96);
            } else {
                Ui.Card(_list,Loc.MapName(selected.Id),out var summary);
                var note=Ui.Paragraph("TeamBrief",summary,Loc.Text("배치할 인원과 장비를 확인하세요."),28,Theme.TextDim);
                Ui.Size(note.gameObject,60);
                BuildTeamPicker();
                if (_rescueId != null) {
                    var lost=RescueSystem.Rescuable(Session.Save,Session.Clock.UtcNow).Find(s=>s.Uid==_rescueId);
                    if (lost != null) { Ui.Card(_list,Loc.Text("구조 대상 · {0}",Loc.Text(lost.Name)),out var rescue); BuildRescueRow(rescue,lost); }
                    else { _mapId=null; _rescueId=null; _preparing=false; Navigate(); }
                } else if (IsOrientation(selected)) BuildOrientation();
                else { Ui.Card(_list,Loc.Text("파견 준비"),out var dispatch); BuildDeparture(selected,dispatch); }
            }
        }

        void Navigate() { Refresh(); _scroll.StopMovement(); _scroll.verticalNormalizedPosition=1; }
        bool IsOrientation(MapDef map) => _rescueId == null && map.Id==Orientation.MapId && Session.Save.Orientation?.Stage==OrientationStage.Pending;
        void SelectMap(string id,string rescue=null) { _mapId=id; _rescueId=rescue; _preparing=false; _selected.Clear(); Navigate(); }

        void BuildOverview(List<MapDef> maps)
        {
            var intro=Ui.Paragraph("ExpeditionSteps",_list,Loc.Text("1 · 지역 선택   →   2 · 상세 정보   →   3 · 파견"),27,Theme.Info);
            Ui.Size(intro.gameObject,52);
            var hint=Ui.Paragraph("RegionHint",_list,Loc.Text("지역을 선택하면 상세 정보와 파견 준비를 확인할 수 있습니다."),27,Theme.TextDim);
            Ui.Size(hint.gameObject,70);
            var mapRoot=Ui.Surface("RegionMap",_list,Theme.PanelAlt,Theme.AccentDim); Ui.Size(mapRoot.gameObject,760);
            var graphic=Ui.Rect("SeoulMap",mapRoot).gameObject.AddComponent<FieldMap>(); Ui.Stretch(graphic.rectTransform); graphic.raycastTarget=false;
            int visible=0;
            foreach(var map in maps) {
                if (!MapUnlock.IsUnlocked(Session.Save,map)) continue;
                visible++;
                string id=map.Id; var position=RegionPosition(id);
                var button=Ui.Button("SelectMap_"+id,mapRoot,Loc.MapName(id)+"\n"+(IsOrientation(map) ? Loc.Text("초도 보급 · 3분 · 무료") : FormatDuration(map.DurationMinutes)),()=>SelectMap(id),Theme.Panel,26);
                var rt=(RectTransform)button.transform;
                rt.anchorMin=new Vector2(position.x-.13f,position.y); rt.anchorMax=new Vector2(position.x+.13f,position.y);
                rt.pivot=new Vector2(.5f,.5f); rt.sizeDelta=new Vector2(0,86); rt.anchoredPosition=Vector2.zero;
                var returns=Ui.Label("MapReturn_"+id,mapRoot,"",21,TextAnchor.UpperCenter,Theme.Safe);
                returns.rectTransform.anchorMin=rt.anchorMin; returns.rectTransform.anchorMax=rt.anchorMax;
                returns.rectTransform.sizeDelta=new Vector2(0,62); returns.rectTransform.anchoredPosition=new Vector2(0,-76);
                _mapReturns.Add(new MapReturnRow {MapId=id,Label=returns});
            }
            if (visible==0) { var none=Ui.Paragraph("NoRegions",_list,Loc.Text("현재 선택할 수 있는 지역이 없습니다."),28,Theme.TextDim); Ui.Size(none.gameObject,90); }
            BuildRescues();
            TickMapReturns();
        }

        static Vector2 RegionPosition(string id)
        {
            // Original maps.json geography, spaced for mobile touch targets.
            switch(id) {
                case "UIJEONGBU": return new Vector2(.73f,.9f);
                case "NAMSAN_WOODS": return new Vector2(.38f,.72f);
                case "GURO_FACTORY": return new Vector2(.15f,.51f);
                case "MYEONGDONG": return new Vector2(.55f,.51f);
                case "GANGNAM_STREETS": return new Vector2(.82f,.37f);
                case "HAN_RIVER": return new Vector2(.82f,.15f);
                case "YONGSAN_MARKET": return new Vector2(.38f,.19f);
                default: return new Vector2(.5f,.5f);
            }
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
            var rescuable = RescueSystem.Rescuable(Session.Save,Session.Clock.UtcNow);
            rescuable.RemoveAll(s=>!MapUnlock.IsUnlocked(Session.Save,Session.Data.GetMap(s.LostAtMapId)));
            if (rescuable.Count == 0) return;

            RectTransform body;
            var card = Ui.Card(_list, AfterSeoul.Core.Loc.Text("무전 포착"), out body);
            foreach (var lost in rescuable) {
                if (!MapUnlock.IsUnlocked(Session.Save,Session.Data.GetMap(lost.LostAtMapId))) continue;
                string uid=lost.Uid, mapId=lost.LostAtMapId;
                var left=RescueSystem.RemainingWindow(lost,Session.Clock.UtcNow);
                var button=Ui.Button("SelectRescue_"+uid,body,
                    Loc.Text("{0} — {1}에서 신호",Loc.Text(lost.Name),Loc.MapName(mapId))+"\n"+Loc.Text("신호 {0}시간 {1}분 남음   ·   ",(int)left.TotalHours,left.Minutes)+Loc.Text("무전 위치 확인"),
                    ()=>SelectMap(mapId,uid),Theme.PanelAlt,26);
                Ui.Size(button.gameObject,112);
            }
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
                ? AfterSeoul.Core.Loc.Text("먼저 파견할 인원을 선택하세요.")
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
            _mapId=null; _rescueId=null; _preparing=false;
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

        private List<MapDef> SortedMaps()
        {
            var list = new List<MapDef>();

            foreach (var map in Session.Data.AllMaps) list.Add(map);

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
        private void BuildActiveExpeditions(string mapId)
        {
            _runRows.Clear();

            var save = Session.Save;
            var running = new List<ExpeditionState>();
            foreach (var e in save.Expeditions)
                if (!e.Resolved && e.MapId==mapId) running.Add(e);
            running.Sort((a,b)=>a.ReturnsAt.CompareTo(b.ReturnsAt));

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
            Ui.Size(surface.gameObject, 178f);
            Ui.Column(surface, 6f, new RectOffset(16, 16, 10, 12));

            var head = Ui.Rect("Head", surface);
            Ui.Size(head.gameObject, 62f);
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

            var returnAt=Ui.Label("ReturnAt",surface,Loc.Text("복귀 예정 {0}",ReturnTime(exp.ReturnsAt)),26,TextAnchor.MiddleLeft,Theme.TextDim);
            Ui.Size(returnAt.gameObject,36);
            var progress=Ui.Label("Progress",surface,"",23,TextAnchor.MiddleLeft,Theme.Info); Ui.Size(progress.gameObject,28);

            var bar = Ui.Bar(surface, 8f, rescue ? Theme.Warn : Theme.Info);

            _runRows.Add(new RunRow { Exp = exp, Bar = bar, Time = time, Progress=progress, Surface = surface });
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
                row.Progress.text=Loc.Text("진행 {0:0}%",value*100);

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

        string ReturnTime(System.DateTimeOffset at)
        {
            var local=at.ToOffset(GameTime.GameZoneOffset);
            return local.ToString(local.Date==Session.Clock.UtcNow.ToOffset(GameTime.GameZoneOffset).Date ? "HH:mm" : "MM/dd HH:mm")+" KST";
        }

        void TickMapReturns()
        {
            foreach(var row in _mapReturns) {
                int count=0; ExpeditionState first=null;
                foreach(var exp in Session.Save.Expeditions) {
                    if (exp.Resolved || exp.MapId!=row.MapId) continue;
                    count++; if (first==null || exp.ReturnsAt<first.ReturnsAt) first=exp;
                }
                row.Label.gameObject.SetActive(first!=null);
                if (first==null) { row.Label.text=""; continue; }
                var left=first.ReturnsAt-Session.Clock.UtcNow;
                string remaining=left.TotalSeconds<=0 ? Loc.Text("곧 복귀") : Loc.Text("{0}분 {1}초 남음",(int)left.TotalMinutes,left.Seconds);
                row.Label.text=Loc.Text("파견 {0}팀 · 복귀 {1}",count,ReturnTime(first.ReturnsAt))+"\n"+remaining;
            }
        }
        public override void Tick(float deltaTime) { TickRuns(); TickMapReturns(); }

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
                    AfterSeoul.Core.Loc.Text("공장 근무·파견·회복 중인 인원은 선택할 수 없습니다."),
                    Theme.FontSmall, Theme.TextDim);
                Ui.Size(none.gameObject, 50f);
                Ui.Size(Ui.Button("ManageFactoryCrew",body,Loc.Text("공장 배치 관리"),()=>Shell.SelectByName("공장"),Theme.PanelAlt,28).gameObject,82);
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
                    _mapId=null; _rescueId=null; _preparing=false;
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
        }

        private void BuildMapCard(MapDef map)
        {
            var card = Ui.Rect("Map_" + map.Id, _list);
            var img = card.gameObject.AddComponent<Image>();
            img.color = Theme.Panel;
            img.raycastTarget = false;
            Ui.Column(card, 8f, new RectOffset(20, 20, 16, 18));

            var picture=GameArt.Place("RegionIllustration",card,GameArt.City()); Ui.Size(picture.gameObject,250);

            // 제목 줄: 지역명 + 위험도
            var head = Ui.Rect("Head", card);
            Ui.Size(head.gameObject, 54f);
            Ui.Row(head, 8f);

            var name = Ui.Label("Name", head, Loc.MapName(map.Id), Theme.FontHeading);
            Ui.Size(name.gameObject, flexWidth: 1f);

            bool orientation=IsOrientation(map);
            var risk = Ui.Label("Risk", head, orientation ? Loc.Text("초도 보급 · 3분 · 무료") : Theme.RiskStars(map.RiskLevel), Theme.FontSmall,
                TextAnchor.MiddleRight, Theme.RiskColor(map.RiskLevel));
            risk.horizontalOverflow = HorizontalWrapMode.Wrap;
            risk.verticalOverflow = VerticalWrapMode.Truncate;
            risk.resizeTextMinSize = 18;
            risk.resizeTextMaxSize = risk.fontSize;
            risk.resizeTextForBestFit = true;
            Ui.Size(risk.gameObject, width: 220f, flexWidth: 0f);

            string description=Loc.Get("MAP_"+map.Id+"_DESC");
            var desc=Ui.Paragraph("RegionDescription",card,description,28,Theme.TextDim); Ui.Size(desc.gameObject,130);
            if (_rescueId != null) {
                var lost=Session.Save.Scavs.Find(s=>s.Uid==_rescueId);
                var target=Ui.Paragraph("RescueTarget",card,Loc.Text("구조 대상 · {0}",Loc.Text(lost?.Name ?? "")),28,Theme.Warn); Ui.Size(target.gameObject,60);
            }

            // 정보 줄. 파견비는 팀에 따라 달라지므로 여기엔 1인 기준값만 적고,
            // 실제 청구액은 아래 버튼에 띄운다.
            var info = Ui.Label("Info", card,
                AfterSeoul.Core.Loc.Text("예상 {0}   ·   1인 기준 {1}   ·   교전 {2:0}%", FormatDuration(orientation ? Orientation.DurationMinutes : map.DurationMinutes), Theme.Won(orientation ? 0 : ExpeditionSystem.BaselineCost(map)), orientation ? 0 : map.CombatChance * 100f),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            info.verticalOverflow = VerticalWrapMode.Truncate;
            info.resizeTextMinSize = 18;
            info.resizeTextMaxSize = info.fontSize;
            info.resizeTextForBestFit = true;
            Ui.Size(info.gameObject, 42f);

            // 발견 가능 품목: 가중치 높은 순 상위 5개
            var table = Session.Data.GetLootTable(map.LootTableId);
            if (orientation) {
                var brief=Ui.Paragraph("OrientationRegionBrief",card,Loc.Text("명동 외곽의 확인된 보급 경로입니다. 1명이 3분 동안 다녀옵니다.\n비용 없음 · 부상과 실종 없음 · 장비 보존\n꾸러미를 기지에 납품하면 50,000원을 받습니다."),28,Theme.Safe); Ui.Size(brief.gameObject,190);
            }
            else if (table != null && table.Entries.Length > 0)
            {
                var entries = new List<LootEntry>(table.Entries);
                entries.Sort((a, b) => b.Weight.CompareTo(a.Weight));

                var names = new List<string>();
                for (int i = 0; i < entries.Count && i < 5; i++)
                    names.Add(Loc.ItemName(entries[i].ItemId));

                var loot = Ui.Paragraph("Loot", card, AfterSeoul.Core.Loc.Text("주요 발견  ") + string.Join(" · ", names.ToArray()),
                    Theme.FontSmall, Theme.TextFaint);
                Ui.Size(loot.gameObject, 100f);
            }
        }

        private void BuildDeparture(MapDef map,Transform card)
        {
            // 파견 버튼. 막힌 사유는 ExpeditionSystem 이 내리는 판정을 그대로 쓴다 —
            // 화면이 조건을 따로 쓰면 "눌리는데 아무 일도 안 일어나는" 버튼이 생긴다.
            var team = new List<string>(_selected);
            string block = ExpeditionSystem.DepartBlockReason(Session.Save, Session.Data, map.Id, team);
            bool unlocked = MapUnlock.IsUnlocked(Session.Save, map);
            long cost = ExpeditionSystem.CostFor(Session.Save, Session.Data, map, team);
            var quote=Ui.Paragraph("DispatchQuote",card,
                Loc.Text("소요 시간 {0} · 위험도 {1}",FormatDuration(map.DurationMinutes),map.RiskLevel)+"\n"+
                Loc.Text("예상 비용 {0} · 교전 확률 {1:0}%",Theme.Won(cost),map.CombatChance*100),28,Theme.TextDim);
            Ui.Size(quote.gameObject,100);

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
            _mapId=null; _rescueId=null; _preparing=false;

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
