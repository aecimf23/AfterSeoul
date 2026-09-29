using System;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>Fixed upper workshop scene; production actions stay within thumb reach.</summary>
    internal sealed partial class ProductionFactoryView
    {
        readonly GameSession _session;
        readonly AppShell _shell;
        readonly Action _parts, _equipment, _production;
        readonly RectTransform _root, _floor, _equipmentHost, _equipmentList, _inlineList;
        readonly ScrollRect _productionScroll;
        readonly Text _playerName, _facilitySummary;
        readonly Image _weaponArt, _workerArt;
        readonly Text _weaponName, _crew, _progressText, _rate, _wage, _feedback, _support, _switchHint;
        readonly Button _previous, _next, _hire, _tap, _deliver;
        readonly Button[] _sections = new Button[3];
        readonly ProgressBar _bar;
        readonly ProductionConveyorView _conveyor;
        readonly Image[] _scavArt = new Image[3];
        readonly Text[] _scavNames = new Text[3];
        readonly string[] _scavIds = new string[3];
        readonly int[] _scavFrames = new int[3];
        readonly Text _autoStatus;
        NpcSpeech _dialogSpeech;
        long _seenProduced = -1;
        int _seenSamples;
        RectTransform _dialog;
        int _equipmentPage=1;
        bool _isEquipment;
        float _strike, _poll, _animation;
        string _lastWeapon, _inlineState;
        public bool HasOpenDialogue => _dialog != null;

        public ProductionFactoryView(RectTransform parent, GameSession session, AppShell shell, Action parts, Action equipment, Action production)
        {
            _session = session; _shell = shell; _parts = parts; _equipment = equipment; _production = production;
            _session.AdvanceProductionConveyor(0);
            _root = Ui.Rect("ProductionFactory", parent);
            _floor = Ui.Rect("ProductionFloor", _root);
            var floorContent = Ui.ScrollList("ProductionScroll", _floor, out _productionScroll, 12);
            var scene = Ui.Surface("FactoryScene", floorContent, Theme.PanelAlt, Theme.AccentDim);
            Ui.Size(scene.gameObject, 480);
            var sceneTitle = Ui.Label("FactorySign", scene, Loc.Text("용산킴의 공장 · 총기 제작 알바"), 30, TextAnchor.MiddleCenter, Theme.Accent);
            Region(sceneTitle.rectTransform, .03f, .89f, .97f, .99f);
            _workerArt = Art("ProductionWorker", scene, GameArt.Worker(0));
            Region(_workerArt.rectTransform, .015f, .53f, .25f, .88f);
            _playerName = Ui.Label("PlayerWorkstation", scene, Loc.Text("나 · 직접 조립"), 21, TextAnchor.MiddleCenter, Theme.Accent);
            Region(_playerName.rectTransform, .015f, .48f, .25f, .53f);
            _facilitySummary=Ui.Label("FacilitySummary",scene,"",25,TextAnchor.MiddleLeft,Theme.Info);
            Region(_facilitySummary.rectTransform,.65f,.53f,.98f,.88f);
            for (int i=0; i<3; i++) {
                float left = .255f + i * .245f;
                _scavArt[i] = Art("ProductionScavArt" + i, scene, GameArt.EmptyBench());
                Region(_scavArt[i].rectTransform, left, .53f, left + .235f, .88f);
                _scavNames[i] = Ui.Label("ProductionScavName" + i, scene, "", 21, TextAnchor.MiddleCenter, Theme.TextDim);
                _scavNames[i].resizeTextForBestFit = true;
                _scavNames[i].resizeTextMinSize = 14; _scavNames[i].resizeTextMaxSize = 21;
                Region(_scavNames[i].rectTransform, left, .48f, left + .235f, .53f);
            }
            _crew = Ui.Label("FactoryCrew", scene, "", 25, TextAnchor.MiddleCenter, Theme.TextDim);
            Region(_crew.rectTransform, .04f, .415f, .96f, .48f);
            _conveyor = new ProductionConveyorView(scene);
            Region(_conveyor.Root, .025f, .205f, .975f, .415f);
            _weaponArt = Art("ProductionWeaponArt", scene, GameArt.Weapon("WPN04"));
            Region(_weaponArt.rectTransform, .04f, .005f, .96f, .205f);

            var controls = Ui.Rect("ProductionControls", floorContent);
            Ui.Size(controls.gameObject, 470);
            _weaponName = Ui.Label("ProductionWeaponName", controls, "", 36, TextAnchor.MiddleCenter, Theme.Text);
            Region(_weaponName.rectTransform, .16f, .88f, .84f, 1);
            _previous = Ui.Button("ProductionPrevious", controls, "<", () => Select(-1), Theme.PanelAlt, 32);
            Region((RectTransform)_previous.transform, 0, .88f, .14f, 1);
            _next = Ui.Button("ProductionNext", controls, ">", () => Select(1), Theme.PanelAlt, 32);
            Region((RectTransform)_next.transform, .86f, .88f, 1, 1);
            _switchHint = Ui.Label("ProductionSwitchHint", controls, Loc.Text("총기 변경 시 현재 작업 초기화"), 20, TextAnchor.MiddleCenter, Theme.TextFaint);
            Region(_switchHint.rectTransform, .16f, .845f, .84f, .89f);
            _wage = Ui.Label("ProductionWage", controls, "", 27, TextAnchor.MiddleCenter, Theme.Accent);
            Region(_wage.rectTransform, 0, .77f, 1, .845f);
            _progressText = Ui.Label("ProductionProgress", controls, "", 26, TextAnchor.MiddleCenter);
            Region(_progressText.rectTransform, 0, .69f, 1, .78f);
            _bar = Ui.Bar(controls, 14, Theme.Safe);
            Region(_bar.Root, .02f, .65f, .98f, .68f);
            _rate = Ui.Label("ProductionRate", controls, "", 24, TextAnchor.MiddleCenter, Theme.TextDim);
            Region(_rate.rectTransform, 0, .595f, 1, .65f);
            _autoStatus = Ui.Label("ProductionAutoStatus", controls, "", 22, TextAnchor.MiddleCenter, Theme.Safe);
            Region(_autoStatus.rectTransform, 0, .54f, 1, .595f);
            _tap = Ui.Button("ProductionTap", controls, Loc.Text("타이밍 맞춰 조립"), null, Theme.Accent, 36);
            _tap.gameObject.AddComponent<PressButton>().Pressed = Tap;
            Region((RectTransform)_tap.transform, .02f, .33f, .98f, .54f);
            _feedback = Ui.Label("ProductionFeedback", controls, Loc.Text("재료는 용산킴이 제공합니다. 완성 즉시 자동 납품."), 25, TextAnchor.MiddleCenter, Theme.Safe);
            Region(_feedback.rectTransform, 0, .21f, 1, .32f);
            _support = Ui.Label("ProductionSupport", controls, "", 24, TextAnchor.MiddleCenter, Theme.TextDim);
            Region(_support.rectTransform, 0, .13f, 1, .21f);
            _hire = Ui.Button("ProductionHire", controls, Loc.Text("첫 동료 고르기"), _shell.OpenRecruitment, Theme.Safe, 26);
            Region((RectTransform)_hire.transform, 0, 0, .49f, .12f);
            _deliver = Ui.Button("ProductionDeliver", controls, Loc.Text("용산킴에게 납품"), Deliver, Theme.Safe, 26);
            Region((RectTransform)_deliver.transform, 0, 0, .49f, .12f);
            var gear = Ui.Button("ProductionToEquipment", controls, Loc.Text("제작 장비") + " ↓", ScrollToUpgrades, Theme.PanelAlt, 26);
            Region((RectTransform)gear.transform, .51f, 0, 1, .12f);
            var help=Ui.Button("FactoryHelp",controls,Loc.Text("공장 도움말"),()=>OpenGuide(0),Theme.PanelAlt,26);
            Region((RectTransform)help.transform,0,0,.49f,.12f);
            var guidance=Ui.Rect("FactoryGuidance",floorContent);Ui.Row(guidance,10);Ui.Size(guidance.gameObject,80,flexHeight:0);
            _automationHelp=Ui.Button("FactoryAutomationHelp",guidance,Loc.Text("자동 생산 시작하기"),()=>OpenGuide(2),Theme.PanelAlt,26);
            Ui.Size(_automationHelp.gameObject,80,flexWidth:1);

            _inlineList = Ui.Rect("ProductionUpgrades", floorContent);
            _inlineList.pivot = new Vector2(.5f, 1);
            Ui.Column(_inlineList, 12);

            _equipmentHost = Ui.Rect("ProductionEquipment", _root);
            var sections = Ui.Rect("EquipmentSections", _equipmentHost); Ui.Top(sections, 74); Ui.Row(sections, 8);
            var names = new[] { "EquipmentCommissions", "EquipmentTools", "EquipmentCrew" };
            var labels = new[] { "의뢰", "제작 장비", "인원 배치" };
            for (int i=0; i<3; i++) {
                int page = i;
                _sections[i] = Ui.Button(names[i], sections, Loc.Text(labels[i]), () => { _equipmentPage=page; DrawEquipment(); }, Theme.PanelAlt, 25);
                Ui.Size(_sections[i].gameObject, 72, flexWidth:1);
            }
            Ui.Size(Ui.Button("FactoryParts", sections, Loc.Text("부품 작업대"), _parts, Theme.PanelAlt, 24).gameObject, 72, flexWidth:1);
            var equipmentContent = Ui.Rect("EquipmentContent", _equipmentHost); Ui.Stretch(equipmentContent, 0, 0, 90, 0);
            _equipmentList = Ui.ScrollList("EquipmentScroll", equipmentContent, out var scroll, 14f);
            Show(false);
        }

        static void Region(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        static Image Art(string name, Transform parent, Sprite sprite)
        {
            var image = Ui.Rect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.preserveAspect = true;
            image.raycastTarget = false; image.color = Color.white;
            return image;
        }
        public void Show(bool equipment)
        {
            _isEquipment = equipment; _root.gameObject.SetActive(true);
            _floor.gameObject.SetActive(!equipment); _equipmentHost.gameObject.SetActive(equipment);
            if (!equipment) { _productionScroll.StopMovement(); _productionScroll.verticalNormalizedPosition=1; }
        }
        public void Hide() => _root.gameObject.SetActive(false);
        public void Refresh()
        {
            if (_isEquipment) DrawEquipment();
            else DrawProduction();
        }
        string GunName(string id) => Loc.ItemName(id);

        void DrawProduction()
        {
            var save = _session.Save;
            var state = save.Factory.Production;
            var gun = ProductionWork.Current(save, _session.Data);
            if (gun == null) return;
            bool trial = state.Contract != null && state.Contract.WeaponId == gun.WeaponId;
            bool trialReady = ProductionWork.TrialReady(save);
            if (_lastWeapon != gun.WeaponId) {
                _lastWeapon = gun.WeaponId;
                _weaponArt.sprite = GameArt.Weapon(gun.WeaponId);
                _feedback.text = trial ? Loc.Text("겉마감은 거칠어도 괜찮습니다. 목표 수량을 완성하세요.") : Loc.Text("재료는 용산킴이 제공합니다. 완성 즉시 자동 납품.");
            }
            var guns = ProductionWork.Guns(_session.Data);
            int index = 0;
            for (int i = 0; i < guns.Count; i++) if (guns[i].WeaponId == gun.WeaponId) index = i;
            int available = state.UnlockedCount + (state.Contract != null ? 1 : 0);
            _weaponName.text = GunName(gun.WeaponId) + "   " + (trial ? Loc.Text("시험 제작") : (index + 1) + "/" + state.UnlockedCount);
            _previous.interactable = index > 0; _next.interactable = index + 1 < available;
            _switchHint.gameObject.SetActive(available > 1);
            _wage.text = trial ? Loc.Text("시제품 납품 보수 · {0:N0}원", state.Contract.Required * gun.Wage) : Loc.Text("자동 납품 · 1정당 {0:N0}원", gun.Wage);
            _crew.text = Loc.Text("나 + 스캐브 {0}/3명 · 부품 공급 Lv.{1}", state.AssignedScavUids.Count, state.SpeedLevel);
            DrawStations();
            _rate.text = Loc.Text("공급 {0:0.##}초마다 · 최고 {1}등급 · 자동 {2:0.###}/초", ProductionWork.FeedInterval(save, _session.Data), ProductionWork.MaterialTier(save, _session.Data), ProductionWork.AutoWorkPerSecond(save, _session.Data));
            bool ready = StarterSupport.Ready(save);
            _hire.gameObject.SetActive(false);
            _deliver.gameObject.SetActive(trialReady);
            _deliver.transform.SetAsLastSibling();
            Ui.SetButtonLabel(_automationHelp,ProductionWork.AutoWorkPerSecond(save,_session.Data)>0 ? Loc.Text("자동 생산 중 · 관리 방법") : Loc.Text("자동 생산 시작하기"));
            _tap.interactable = !(trial && trialReady);
            Ui.SetButtonLabel(_tap, trial && trialReady ? Loc.Text("시제품 완성 · 납품 대기") : Loc.Text("타이밍 맞춰 조립"));
            _support.text = state.Contract != null ? Loc.Text("시제품 {0}/{1}정 · 납품하면 정식 제작 승인", state.Contract.Crafted, state.Contract.Required)
                : Loc.Text("누적 제작 {0:N0}정 · 받은 작업비 {1:N0}원", state.TotalProduced, state.TotalWages);
            _facilitySummary.text=Loc.Text("현재 설비\n공급 Lv.{0}\n{1:0.##}초마다 부품\n재료 최고 {2}등급\n자동 {3:0.###}/초",state.SpeedLevel,ProductionWork.FeedInterval(save,_session.Data),ProductionWork.MaterialTier(save,_session.Data),ProductionWork.AutoWorkPerSecond(save,_session.Data));
            DrawProgress();
            _conveyor.Draw(_session.Conveyor);
            DrawInlineUpgrades();
        }

        void ScrollToUpgrades()
        {
            Canvas.ForceUpdateCanvases();
            _productionScroll.StopMovement();
            float distance = Mathf.Max(0, _productionScroll.content.rect.height - _productionScroll.viewport.rect.height);
            _productionScroll.verticalNormalizedPosition = distance > 0 ? 1 - Mathf.Clamp01(-_inlineList.anchoredPosition.y / distance) : 1;
        }

        void DrawInlineUpgrades()
        {
            var save=_session.Save; var state=save.Factory.Production;
            string key=$"{save.Player.Money}/{state.SpeedLevel}/{state.AssemblyJigLevel}/{state.PowerToolsLevel}/{state.ExtraBenches}/{state.UnlockedCount}/{state.Contract?.WeaponId}/{state.Contract?.Crafted}/{ProductionWork.AutoWorkPerSecond(save,_session.Data)}";
            // Manual hits usually change only progress; keep purchase buttons and scrolling stable.
            if (_inlineState==key) return;
            _inlineState=key;
            Ui.Clear(_inlineList);
            DrawTools(_inlineList, "Inline",true);
            DrawCommission(_inlineList, "Inline", compact:true);
        }

        void DrawProgress()
        {
            var gun = ProductionWork.Current(_session.Save, _session.Data);
            if (gun == null) return;
            var state = _session.Save.Factory.Production;
            if (state.Contract != null && state.Contract.WeaponId == gun.WeaponId && ProductionWork.TrialReady(_session.Save)) {
                _bar.Set(1); _progressText.text = Loc.Text("목표 수량 완성 · 용산킴에게 납품하세요.");
                _autoStatus.text = Loc.Text("자동 작업 대기 · 시제품 납품 필요"); return;
            }
            // Interpolate the next settlement visually; only the core awards wages.
            double elapsed = Math.Max(0, (_session.Clock.UtcNow - state.LastWorkedAt).TotalSeconds);
            double rate = ProductionWork.AutoWorkPerSecond(_session.Save, _session.Data);
            double work = state.WorkDone + Math.Min(elapsed, 5) * rate;
            work = Math.Min(work, gun.WorkRequired);
            _bar.Set((float)(work / gun.WorkRequired));
            _progressText.text = Loc.Text("제작 진행 {0:0.00} / {1:0.##}", work, gun.WorkRequired);
            if (rate > 0) {
                double seconds = Math.Max(0, Math.Ceiling((gun.WorkRequired - work) / rate));
                string time = ((int)seconds / 60).ToString("00") + ":" + ((int)seconds % 60).ToString("00");
                _autoStatus.text = Loc.Text("자동 조립 중 · +{0:0.###}/초 · 다음 완성 {1}", rate, time);
            } else _autoStatus.text = Loc.Text("스캐브를 배치하면 함께 자동으로 조립합니다.");
        }
        void Tap()
        {
            if (!_tap.interactable || _isEquipment || !_root.gameObject.activeSelf || _dialog != null) return;
            var before = _session.Save.Factory.Production;
            long producedBefore = before.TotalProduced;
            int samplesBefore = before.Contract?.Crafted ?? 0;
            var strike = _session.StrikeProduction();
            if (strike.Kind == ProductionStrikeKind.Cooldown || strike.Kind == ProductionStrikeKind.Blocked) return;
            _shell.AfterAction();
            if (strike.Kind == ProductionStrikeKind.Miss) {
                Sfx.WorkshopMiss();
                _feedback.text = Loc.Text("빗나갔습니다 · 연타하지 말고 다음 부품을 기다리세요.");
                _feedback.color = Theme.Warn; return;
            }
            _strike = .28f;
            _workerArt.sprite = GameArt.Worker(2);
            Sfx.WorkshopHit();
            _feedback.color = Theme.Safe;
            _feedback.text = Loc.Text("{0}등급 부품 조립 성공 · 작업 +{1:0.#}", strike.Tier, strike.WorkAdded);
            var result = strike.Production;
            if (result.CompletedCount > 0) {
                var trial = _session.Save.Factory.Production.Contract;
                _feedback.text = trial != null && trial.WeaponId == _session.Save.Factory.Production.SelectedWeaponId
                    ? Loc.Text("시제품 완성 {0}/{1}정 · 보수는 납품 시 지급", trial.Crafted, trial.Required)
                    : Loc.Text("{0:N0}정 납품 완료 · +{1:N0}원", result.CompletedCount, result.Wages);
                Sfx.WorkshopComplete();
            }
            else if (_session.Save.Factory.Production.TotalProduced > producedBefore || (_session.Save.Factory.Production.Contract?.Crafted ?? 0) > samplesBefore)
                Sfx.WorkshopComplete();
            RememberProduction();
        }
        void Select(int delta)
        {
            var guns = ProductionWork.Guns(_session.Data);
            for (int i = 0; i < guns.Count; i++) if (guns[i].WeaponId == _session.Save.Factory.Production.SelectedWeaponId) {
                int target = i + delta;
                var state = _session.Save.Factory.Production;
                if (target >= 0 && target < state.UnlockedCount + (state.Contract != null ? 1 : 0)) {
                    _session.SelectProductionGun(guns[target].WeaponId);
                    _shell.AfterAction();
                }
                return;
            }
        }
        public void Tick(float delta)
        {
            _dialogSpeech?.Tick(delta);
            if (_isEquipment || _dialog != null) return;
            _session.AdvanceProductionConveyor(delta);
            _conveyor.Draw(_session.Conveyor);
            _poll += delta;
            _animation += delta;
            if (_strike > 0) _strike -= delta;
            int playerFrame = _strike > .16f ? 2 : _strike > 0 ? 3 : 0;
            if (_strike <= 0 && _session.Conveyor.CooldownRemaining <= 0)
                foreach (var part in _session.Conveyor.Parts)
                    if (part.Position >= .34 && part.Position < _session.Conveyor.HitEnd) { playerFrame=1; break; }
            _workerArt.sprite = GameArt.Worker(playerFrame);
            bool working = _tap.interactable && ProductionWork.AutoWorkPerSecond(_session.Save, _session.Data) > 0;
            for (int i=0; i<3; i++) {
                if (string.IsNullOrEmpty(_scavIds[i])) continue;
                float phase = (_animation + i * .46f) % 1.8f;
                int frame = !working ? 0 : phase < .45f ? 0 : phase < .85f ? 1 : phase < 1.03f ? 2 : phase < 1.3f ? 3 : 0;
                if (frame == 2 && _scavFrames[i] != 2) Sfx.WorkshopHit(automatic:true);
                _scavFrames[i] = frame;
                _scavArt[i].sprite = GameArt.Worker(frame, i);
            }
            var state = _session.Save.Factory.Production;
            int samples = state.Contract?.Crafted ?? 0;
            if (_seenProduced >= 0 && (state.TotalProduced > _seenProduced || samples > _seenSamples)) Sfx.WorkshopComplete();
            RememberProduction();
            if (_poll >= .1f) {
                _poll = 0; DrawProgress();
                if (_tap.interactable) Ui.SetButtonLabel(_tap, _session.Conveyor.CooldownRemaining > .2
                    ? Loc.Text("다음 입력까지 {0:0.0}초", _session.Conveyor.CooldownRemaining) : Loc.Text("타이밍 맞춰 조립"));
            }
        }

        void RememberProduction()
        {
            _seenProduced = _session.Save.Factory.Production.TotalProduced;
            _seenSamples = _session.Save.Factory.Production.Contract?.Crafted ?? 0;
        }
        void DrawStations()
        {
            var save = _session.Save;
            var assigned = save.Factory.Production.AssignedScavUids;
            int count = 1;
            for (int i=0; i<Math.Min(3,assigned.Count); i++)
                if (save.Scavs.Exists(s => s.Uid == assigned[i] && s.Status == ScavStatus.Working)) count++;
            float width = Mathf.Min(.30f, .62f / count);
            float start = .02f;
            PositionStation(_workerArt, _playerName, start, width);
            int visibleIndex = 1;
            for (int i=0; i<3; i++) {
                string uid = i < assigned.Count ? assigned[i] : null;
                var scav = save.Scavs.Find(s => s.Uid == uid && s.Status == ScavStatus.Working);
                _scavIds[i] = scav?.Uid;
                _scavNames[i].text = scav != null ? Loc.Text(scav.Name) : "";
                _scavArt[i].sprite = scav != null ? GameArt.Worker(_scavFrames[i], i) : GameArt.EmptyBench();
                _scavArt[i].gameObject.SetActive(scav != null);
                _scavNames[i].gameObject.SetActive(scav != null);
                if (scav != null) PositionStation(_scavArt[i], _scavNames[i], start + width * visibleIndex++, width);
            }
            if (_seenProduced < 0) RememberProduction();
        }

        static void PositionStation(Image art, Text label, float left, float width)
        {
            Region(art.rectTransform, left+.005f, .53f, left+width-.005f, .89f);
            Region(label.rectTransform, left+.005f, .475f, left+width-.005f, .535f);
        }

        Text Paragraph(Transform parent, string name, string text, float height = 76)
        {
            var label = Ui.Paragraph(name, parent, text, 27, Theme.TextDim);
            Ui.Size(label.gameObject, height);
            return label;
        }
        void DrawEquipment()
        {
            Ui.Clear(_equipmentList);
            for (int i=0; i<_sections.Length; i++) _sections[i].targetGraphic.color = i == _equipmentPage ? Theme.AccentDim : Theme.PanelAlt;
            if (_equipmentPage == 0) DrawCommission();
            else if (_equipmentPage == 1) DrawTools();
            else DrawCrew();
        }

        void DrawTools(Transform list = null, string prefix = "",bool compact=false)
        {
            list = list ?? _equipmentList;
            var save = _session.Save; var data = _session.Data; var state = save.Factory.Production;
            Ui.Card(list, Loc.Text("부품 공급 설비"), out var speed);
            long cost = ProductionWork.SpeedUpgradeCost(save, data);
            if(!compact) {
                Paragraph(speed, "SpeedInfo", Loc.Text("공급 Lv.{0} · {1:0.##}초마다 부품이 도착합니다. 강화하면 더 자주 나옵니다.", state.SpeedLevel, ProductionWork.FeedInterval(save, data)), 96);
                Paragraph(speed,"SpeedAutoEffect",Loc.Text("배치된 스캐브의 자동 제작 속도도 함께 증가합니다. 인원이 없으면 자동 생산은 시작되지 않습니다."),88);
                GuideButton(speed,prefix+"HelpSpeed","이 설비는 언제 강화하나요?",()=>OpenGuide(4));
            }
            if(cost>0) {
                var next=PreviewSave(save);next.Factory.Production.SpeedLevel++;
                Paragraph(speed,"SpeedNext",Loc.Text("다음 Lv.{0} · 공급 {1:0.##}초 → {2:0.##}초\n필요 {3:N0}원 · 보유 {4:N0}원",next.Factory.Production.SpeedLevel,ProductionWork.FeedInterval(save,data),ProductionWork.FeedInterval(next,data),cost,save.Player.Money),96);
            }
            var upgrade = Ui.Button(prefix + "ProductionUpgrade", speed,
                cost > 0 ? Loc.Text("부품 공급 강화 · {0:N0}원", cost) : Loc.Text("최대 레벨"), () => {
                    if (_session.UpgradeProduction()) Sfx.Confirm(); _shell.AfterAction();
                }, Theme.AccentDim, 28);
            Ui.Size(upgrade.gameObject, 88);
            upgrade.interactable = cost > 0 && save.Player.Money >= cost;
            DrawTool(ProductionEquipment.AssemblyJig, Loc.Text("재료 선별 장치"), Loc.Text("강화하면 상위 등급 부품이 등장합니다. 높은 등급일수록 조립 작업량이 큽니다."), list, prefix,compact);
            DrawTool(ProductionEquipment.PowerTools, Loc.Text("동력 공구"), Loc.Text("배치한 스캐브의 자동 제작 속도가 증가합니다."), list, prefix,compact);
            DrawTool(ProductionEquipment.ExtraBench, Loc.Text("추가 작업대"), Loc.Text("작업대마다 스캐브 1명을 더 배치할 수 있습니다. 최대 3명입니다."), list, prefix,compact);
        }
        void DrawTool(ProductionEquipment kind, string name, string effect, Transform list, string prefix,bool compact=false)
        {
            var save = _session.Save; var data = _session.Data;
            int level = ProductionWork.EquipmentLevel(save, kind);
            long cost = ProductionWork.EquipmentCost(save, data, kind);
            Ui.Card(list, name + " · Lv." + level, out var body);
            if(!compact) {
                Paragraph(body, "ToolEffect", effect, 72);
                GuideButton(body,prefix+"Help"+kind,"이 설비는 어떻게 쓰나요?",()=>OpenGuide(kind==ProductionEquipment.AssemblyJig?5:kind==ProductionEquipment.PowerTools?6:7));
            }
            string current = kind == ProductionEquipment.ExtraBench ? Loc.Text("현재 공장 배치 한도 {0}명", ProductionWork.CrewCapacity(save))
                : kind == ProductionEquipment.AssemblyJig ? Loc.Text("최고 {0}등급 · 작업 +{1:0.#} · 공급 비중 {2:0}%", ProductionWork.MaterialTier(save, data), ProductionWork.MaterialWork(data, ProductionWork.MaterialTier(save, data)), ProductionWork.MaterialChance(save, data, ProductionWork.MaterialTier(save, data)) * 100)
                : Loc.Text("현재 자동 작업량 {0:0.###}/초", ProductionWork.AutoWorkPerSecond(save, data));
            if(!compact || cost<=0) Paragraph(body, "ToolCurrent", current, 48);
            if(cost>0) {
                var next=PreviewSave(save);
                next.Player.Money=long.MaxValue;
                ProductionWork.TryUpgradeEquipment(next,data,kind);
                string effectNext=kind==ProductionEquipment.ExtraBench ? Loc.Text("배치 한도 {0} → {1}명",ProductionWork.CrewCapacity(save),ProductionWork.CrewCapacity(next))
                    : kind==ProductionEquipment.AssemblyJig ? Loc.Text("최고 {0} → {1}등급 · 상위 등급 비중 {2:0} → {3:0}%",ProductionWork.MaterialTier(save,data),ProductionWork.MaterialTier(next,data),(1-ProductionWork.MaterialChance(save,data,1))*100,(1-ProductionWork.MaterialChance(next,data,1))*100)
                    : Loc.Text("자동 작업량 {0:0.###} → {1:0.###}/초",ProductionWork.AutoWorkPerSecond(save,data),ProductionWork.AutoWorkPerSecond(next,data));
                Paragraph(body,"ToolNext",Loc.Text("다음 Lv.{0} · ",level+1)+effectNext+"\n"+Loc.Text("필요 {0:N0}원 · 보유 {1:N0}원",cost,save.Player.Money),110);
                if(kind==ProductionEquipment.PowerTools && ProductionWork.AutoWorkPerSecond(save,data)==0)
                    Paragraph(body,"ToolCrewRequired",Loc.Text("현재 배치 인원 없음 · 스캐브를 배치해야 자동 제작 효과가 적용됩니다."),80);
            }
            var button = Ui.Button(prefix + "Equipment_" + kind, body, cost <= 0 ? Loc.Text("최대 레벨")
                : Loc.Text(level == 0 ? "장비 제작 · {0:N0}원" : "장비 강화 · {0:N0}원", cost), () => {
                    if (_session.UpgradeProductionEquipment(kind)) Sfx.Confirm(); _shell.AfterAction();
                }, Theme.AccentDim, 28);
            Ui.Size(button.gameObject, 84); button.interactable = cost > 0 && save.Player.Money >= cost;
        }

        static GameSave PreviewSave(GameSave save)
        {
            var codec=new NewtonsoftJsonCodec();
            return codec.Deserialize<GameSave>(codec.Serialize(save));
        }

        void DrawCommission(Transform list = null, string prefix = "", bool compact = false)
        {
            list = list ?? _equipmentList;
            var save = _session.Save; var data = _session.Data; var state = save.Factory.Production;
            var next = ProductionWork.NextGun(save, data);
            Ui.Card(list, Loc.Text("용산킴 · 제작 승인 의뢰"), out var unlock);
            if (state.Contract != null) {
                var contract = state.Contract;
                Paragraph(unlock, "CommissionActive", Loc.Text("{0} 시제품 · {1}/{2}정", GunName(contract.WeaponId), contract.Crafted, contract.Required), 68);
                if (!compact) {
                    Paragraph(unlock, "CommissionTerms", Loc.Text("시제품을 모두 만든 뒤 직접 납품하면 보수와 정식 제작 권한을 받습니다."), 92);
                    var art = Art("TrialGunArt", unlock, GameArt.Weapon(contract.WeaponId)); Ui.Size(art.gameObject, 180);
                }
                bool ready = ProductionWork.TrialReady(save);
                var action = Ui.Button(prefix + "CommissionContinue", unlock, Loc.Text(ready ? "시제품 납품 · 정식 해금" : "시제품 제작 계속하기"), () => {
                    if (ready) Deliver();
                    else { _session.SelectProductionGun(contract.WeaponId); _production(); _productionScroll.verticalNormalizedPosition=1; }
                }, Theme.AccentDim, 28); Ui.Size(action.gameObject, 90);
            } else if (next != null) {
                if (!compact) { var art = Art("NextGunArt", unlock, GameArt.Weapon(next.WeaponId)); Ui.Size(art.gameObject, 150); }
                Paragraph(unlock, "NextGunInfo", Loc.Text("{0} · 시제품 {1}정", GunName(next.WeaponId), next.SampleCount), 54);
                Paragraph(unlock, "CommissionThreshold", Loc.Text("보유금 조건 {0:N0}원 · 접수 시 차감 없음", next.UnlockCost), 72);
                if (!compact) Paragraph(unlock, "CommissionReward", Loc.Text("시제품 납품 보수 · {0:N0}원", next.Wage * next.SampleCount), 50);
                var button = Ui.Button(prefix + "ProductionUnlock", unlock, Loc.Text("용산킴의 의뢰 확인"), OpenCommission, Theme.AccentDim, 28);
                Ui.Size(button.gameObject, 88); button.interactable = save.Player.Money >= next.UnlockCost;
                if (!compact) Paragraph(unlock, "SelectHint", Loc.Text("해금한 총기는 생산 공장의 화살표로 선택합니다.") + "\n" + Loc.Text("총기를 바꾸면 진행 중인 작업이 초기화됩니다."), 96);
            } else Paragraph(unlock, "AllUnlocked", Loc.Text("모든 총기를 해금했습니다."), 54);
        }

        void DrawCrew()
        {
            var save = _session.Save; var state = save.Factory.Production;
            Ui.Card(_equipmentList, Loc.Text("공장 인원 배치"), out var crew);
            int idle=0, deployed=0;
            foreach (var scav in save.Scavs) { if (scav.Status == ScavStatus.Idle) idle++; if (scav.Status == ScavStatus.OnExpedition) deployed++; }
            Paragraph(crew, "CrewDistribution", Loc.Text("공장 {0}/{1}명 · 파견 {2}명 · 대기 {3}명", state.AssignedScavUids.Count, ProductionWork.CrewCapacity(save), deployed, idle), 66);
            Paragraph(crew, "CrewInfo", Loc.Text("대기 스캐브를 배치하면 접속하지 않아도 제작합니다. 파견하려면 먼저 배치를 해제하세요."), 108);
            if (save.Scavs.Count == 0) {
                Paragraph(crew, "NoCrew", Loc.Text("지금은 혼자 작업합니다. 첫 총기를 납품하고 동료를 고용하세요."), 90);
                Ui.Size(Ui.Button("FactoryHire", crew, Loc.Text("고용할 동료 보기"), _shell.OpenRecruitment, Theme.PanelAlt).gameObject, 82);
            }
            foreach (var scav in save.Scavs) {
                string uid = scav.Uid;
                bool assigned = state.AssignedScavUids.Contains(uid);
                var row = Ui.Rect("FactoryScav_" + uid, crew); Ui.Column(row, 8);
                Paragraph(row, "ScavName", Loc.Text(scav.Name) + " · " + Loc.Text(assigned ? "공장 근무 중" : scav.Status == ScavStatus.Idle ? "대기" : scav.Status == ScavStatus.OnExpedition ? "탐색중" : "배치 불가"), 48);
                var button = Ui.Button((assigned ? "Unassign_" : "Assign_") + uid, row,
                    Loc.Text(assigned ? "배치 해제" : "공장에 배치"), () => {
                        if (assigned) _session.UnassignProductionScav(uid); else _session.AssignProductionScav(uid);
                        _shell.AfterAction();
                    }, assigned ? Theme.PanelAlt : Theme.AccentDim, 28);
                Ui.Size(button.gameObject, 80); button.interactable = assigned || ProductionWork.CanAssign(save, uid);
                if (!assigned && scav.Status == ScavStatus.Idle && !button.interactable)
                    Paragraph(row, "CapacityHint", Loc.Text("추가 작업대를 제작하면 배치 한도가 늘어납니다."), 68);
            }
            Ui.Size(Ui.Button("CrewToExpedition", crew, Loc.Text("파견 편성하기"), _shell.OpenDispatch, Theme.PanelAlt).gameObject, 84);
        }

        void CloseDialog()
        {
            _dialogSpeech = null;
            if (_dialog == null) return;
            var root = _dialog.gameObject; _dialog = null; root.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
        }
        void OpenCommission()
        {
            var next = ProductionWork.NextGun(_session.Save, _session.Data);
            if (next == null || _session.Save.Factory.Production.Contract != null) return;
            CloseDialog();
            _dialog = Ui.Modal("ProductionCommission", _shell.transform.GetChild(0), Loc.Text("용산킴 · 제작 승인 의뢰"), CloseDialog, out var body);
            var portrait = Art("KimPortrait", body, GameArt.Portrait("YONGSAN_KIM")); Ui.Size(portrait.gameObject, 230);
            string offer = ProductionDialogue.Offer(GunName(next.WeaponId), next.SampleCount, next.Wage * next.SampleCount);
            _dialogSpeech = new NpcSpeech(Paragraph(body, "KimOffer", offer, 340), "YONGSAN_KIM", offer);
            var art = Art("CommissionGun", body, GameArt.Weapon(next.WeaponId)); Ui.Size(art.gameObject, 180);
            Paragraph(body, "CommissionCash", Loc.Text("보유금 조건 {0:N0}원 · 접수 시 차감 없음", next.UnlockCost), 70);
            var accept = Ui.Button("CommissionAccept", body, Loc.Text("의뢰 수락 · 시험 제작 시작"), () => {
                if (!_session.UnlockProductionGun()) { CloseDialog(); _shell.AfterAction(); return; }
                CloseDialog(); _production(); _shell.AfterAction();
                Sfx.NpcBlip("YONGSAN_KIM");
                _shell.Toast(ProductionDialogue.Accepted(GunName(next.WeaponId)), 4);
            }, Theme.Accent, 30);
            Ui.Size(accept.gameObject, 96); accept.interactable = _session.Save.Player.Money >= next.UnlockCost;
        }
        void Deliver()
        {
            var contract = _session.Save.Factory.Production.Contract;
            if (contract == null) return;
            string weaponId = contract.WeaponId;
            string name = GunName(contract.WeaponId);
            if (!_session.DeliverProductionCommission(out long paid)) return;
            _lastWeapon = null;
            _shell.AfterAction(); Sfx.Complete(); CloseDialog();
            _dialog = Ui.Modal("ProductionApproval", _shell.transform.GetChild(0), Loc.Text("정식 제작 승인"), CloseDialog, out var body);
            var portrait = Art("KimPortrait", body, GameArt.Portrait("YONGSAN_KIM")); Ui.Size(portrait.gameObject, 230);
            string completed = ProductionDialogue.Completed(name);
            _dialogSpeech = new NpcSpeech(Paragraph(body, "KimApproval", completed, 260), "YONGSAN_KIM", completed);
            Paragraph(body, "CommissionPaid", Loc.Text("보수 +{0:N0}원 · {1} 정식 제작 해금", paid, name), 90);
            Ui.Size(Ui.Button("CommissionApproved", body, Loc.Text("정식 생산 시작"), () => {
                _session.SelectProductionGun(weaponId); CloseDialog(); _production(); _shell.AfterAction();
            }, Theme.Accent, 30).gameObject, 96);
        }
    }
}

