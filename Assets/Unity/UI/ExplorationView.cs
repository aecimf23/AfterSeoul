using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>One portrait play surface: preparation, encounter, combat and return.</summary>
    public sealed class ExplorationView : MonoBehaviour
    {
        private GameSession _session;
        private AppShell _shell;
        private RectTransform _root, _content, _modal;
        private Text _vitals, _status, _enemyInfo, _message;
        private RectTransform _actions;
        private ExplorationScene _scene;
        private bool _focused = true, _background, _savingFailed, _returning;
        private float _saveClock;
        private string _phaseKey;
        private readonly Dictionary<string, int> _packed = new Dictionary<string, int>();
        private ExplorationState Run => _session.Save.Exploration;
        private bool EnemyVisible => Run?.Enemy != null && Run.Enemy.Hp > 0 &&
            (Run.Phase == ExplorationPhase.Combat || (Run.Phase == ExplorationPhase.Encounter && Run.Detected));

        internal static ExplorationView Open(AppShell shell, GameSession session, Transform parent)
        {
            var root = Ui.Rect("DirectExploration", parent);
            var backdrop = root.gameObject.AddComponent<Image>();
            backdrop.color = Theme.Bg; backdrop.raycastTarget = true;
            var view = root.gameObject.AddComponent<ExplorationView>();
            view._root = root; view._session = session; view._shell = shell;
            if (!view.HasRun) view.PackRecommended();
            view.Render();
            if (!view.HasRun) view.ShowArrivalDialogue();
            return view;
        }

        private void ShowArrivalDialogue()
        {
            var save = _session.Save;
            if (FirstExplorationQuest.IsPending(save) &&
                (save.FirstExplorationQuest?.Accepted != true || save.FirstExplorationQuest.ReadyToReport)) {
                ShowFirstQuest(); return;
            }
            if (!save.ExplorationStarterPrepared) { ShowStarterGift(); return; }
            foreach (var map in RegionalExplorationQuest.Maps)
                if (RegionalExplorationQuest.Progress(save, map)?.ReadyToReport == true) { ShowRegionalQuest(map, true); return; }
        }

        private void ShowRegionalIntroduction(string map)
        {
            string speaker = RegionalExplorationQuest.Introducer(_session.Save, map);
            OpenModal(Loc.TraderName(speaker), body => {
                GameArt.Portrait("IntroducerPortrait", body, speaker, 240);
                Text(body, "RegionalIntroduction", Loc.Text(RegionalExplorationQuest.Introduction(map)), 260, Theme.Text, 32);
                Button(body, "MeetRegionalNpc", Loc.Text("{0}에게 연락하기", Loc.TraderName(RegionalExplorationQuest.Npc(map))), () => {
                    CloseModal(); ShowRegionalQuest(map, false);
                }, accent: true);
            });
            CompactModal();
        }

        private void ShowRegionalQuest(string map, bool report)
        {
            string npc = RegionalExplorationQuest.Npc(map);
            OpenModal(Loc.TraderName(npc), body => {
                GameArt.Portrait("RegionalNpcPortrait", body, npc, 230);
                Text(body, "RegionalDialogue", Loc.Text(report ? RegionalExplorationQuest.ReportLine(map) : RegionalExplorationQuest.Offer(map)), 310, Theme.Text, 31);
                Text(body, "RegionalObjective", report ? Loc.Text("의뢰 보상 · 15,000원 / 신뢰 +1") : RegionalExplorationQuest.Objective(map), 115, Theme.Info, 27);
                Button(body, report ? "ReportRegionalQuest" : "AcceptRegionalQuest", report ? Loc.Text("조사 결과 보고하기") : Loc.Text("의뢰를 맡고 준비하기"), () => {
                    if (!Command(s => report ? RegionalExplorationQuest.Report(s, map) : RegionalExplorationQuest.Accept(s, map), false)) return;
                    CloseModal(); Render();
                    if (report) Sfx.Complete();
                }, accent: true);
            });
            CompactModal();
        }

        private void ShowFirstQuest()
        {
            bool report = _session.Save.FirstExplorationQuest?.ReadyToReport == true;
            OpenModal(Loc.TraderName(_session.Save.Player.EmployerNpcId), body => {
                Text(body, "FirstQuestTitle", FirstExplorationQuest.Title(_session.Save), 85, Theme.Accent, 36);
                Text(body, "FirstQuestDialogue", report ? FirstExplorationQuest.ReportLine(_session.Save) : FirstExplorationQuest.Offer(_session.Save), 285, Theme.Text, 32);
                Text(body, "FirstQuestObjective", report ? FirstExplorationQuest.NextAction(_session.Save) : FirstExplorationQuest.Objective(_session.Save), 120, Theme.Info, 28);
                Text(body, "FirstQuestReward", Loc.Text("첫 의뢰 보상 · 25,000원 / 신뢰 +2"), 70, Theme.TextDim, 28);
                Button(body, report ? "ReportFirstQuest" : "AcceptFirstQuest", report ? Loc.Text("조사 결과 보고하기") : Loc.Text("의뢰를 맡겠습니다"), () => {
                    if (!Command(s => report ? FirstExplorationQuest.Report(s) : FirstExplorationQuest.Accept(s), false)) return;
                    CloseModal(); Render();
                    if (report) { Sfx.Complete(); _shell.Toast(Loc.Text("첫 의뢰 완료 · 25,000원 / 신뢰 +2")); }
                    else if (!_session.Save.ExplorationStarterPrepared) ShowStarterGift();
                }, accent: true);
            }, false);
            CompactModal();
        }

        private void ShowStarterGift()
        {
            OpenModal(Loc.TraderName(_session.Save.Player.EmployerNpcId), body => {
                GameArt.Portrait("StarterNpc", body, _session.Save.Player.EmployerNpcId, 230);
                Text(body, "StarterGiftLine", ExplorationDialogue.Gift(_session.Save.Player.EmployerNpcId), 210, Theme.Text, 32);
                Button(body, "ReceiveStarterPistol", Loc.Text("권총 받기"), () => {
                    if (!Command(s => ExplorationSystem.PrepareStarter(s, _session.Data), false)) return;
                    PackRecommended();
                    CloseModal();
                    Render();
                }, accent: true);
            }, false);
            CompactModal();
        }

        private bool HasRun => Run != null && (Run.Result == null || !Run.Result.Acknowledged);

        private static Text Text(Transform parent, string name, string value, float height, Color color, int size = 30)
        {
            var label = Ui.Label(name, parent, value, size, TextAnchor.MiddleLeft, color);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 22; label.resizeTextMaxSize = size;
            Ui.Size(label.gameObject, height);
            return label;
        }

        private static Button Button(Transform parent, string name, string value, Action action, bool enabled = true, bool accent = false, float height = 88)
        {
            var b = Ui.Button(name, parent, value, action, accent ? Theme.AccentDim : Theme.PanelAlt, 30);
            Ui.Size(b.gameObject, height, flexWidth: 1);
            b.interactable = enabled;
            return b;
        }

        private bool Command(Func<GameSave, bool> command, bool redraw = true)
        {
            if (_savingFailed || _returning) return false;
            var audioBefore = ExplorationAudioSnapshot.Capture(_session.Save);
            try {
                if (!_session.ExecuteSavedAction(command)) {
                    _shell.Toast(Loc.Text("지금은 사용할 수 없습니다. 장비와 남은 물자를 확인하세요."));
                    return false;
                }
                _saveClock = 0;
                PlayExplorationAudio(audioBefore);
                if (redraw) Render();
                return true;
            }
            catch (Exception e) { SaveError(e); return false; }
        }

        private void SaveError(Exception e)
        {
            Debug.LogWarning("Exploration save: " + e.Message);
            _savingFailed = true;
            OpenModal(Loc.Text("저장하지 못했습니다"), body => {
                Text(body, "SaveError", Loc.Text("진행을 잠시 멈췄습니다. 저장을 다시 시도해 주세요."), 120, Theme.Warn);
                Button(body, "RetryExplorationSave", Loc.Text("다시 저장"), () => {
                    try { _session.Commit(); _savingFailed = false; CloseModal(); Render(); if (!HasRun) ShowArrivalDialogue(); }
                    catch (Exception retry) { Debug.LogWarning(retry.Message); }
                });
            }, false);
        }

        private void Render()
        {
            if (_content != null) { _content.gameObject.SetActive(false); Release(_content.gameObject); }
            _content = Ui.Rect("PlaySurface", _root);
            Ui.Stretch(_content, 28, 28, 22, 24);
            _scene = null; _vitals = _status = _enemyInfo = _message = null;
            _phaseKey = HasRun ? Run.Phase.ToString() + ":" + Run.NodeIndex : "Prepare";
            if (!HasRun) BuildPreparation();
            else if (Run.Phase == ExplorationPhase.Result) BuildResult();
            else if (Run.Phase == ExplorationPhase.EncounterResult) BuildEncounterResult();
            else if (Run.Phase == ExplorationPhase.LootChoice) BuildLootChoice();
            else BuildRun();
        }

        private void Header(string title, Action back)
        {
            var header = Ui.Rect("Header", _content); Ui.Top(header, 86); Ui.Row(header, 12);
            var label = Text(header, "Title", title, 86, Theme.Text, 38); Ui.Size(label.gameObject, flexWidth: 1);
            var help = Button(header, "ExplorationHelp", Loc.Text("도움말"), ShowHelp, height: 76);
            Ui.Size(help.gameObject, width: 160, flexWidth: 0);
            var close = Button(header, "ExplorationBack", HasRun ? Loc.Text("잠시 쉬기") : Loc.Text("기지"), back, height: 76);
            Ui.Size(close.gameObject, width: 170, flexWidth: 0);
        }

        private void BuildPreparation()
        {
            Header(Loc.Text("직접 탐색 준비"), Close);
            var host = Ui.Rect("Preparation", _content); Ui.Stretch(host, 0, 0, 105, 0);
            var vitals = Text(host, "CharacterLevel", Loc.Text("HP {0:0} · 수분 {1:0} · 에너지 {2:0}", _session.Save.Player.Hp, _session.Save.Player.Hydration, _session.Save.Player.Energy), 58, Theme.Info);
            Ui.Top(vitals.rectTransform, 58);
            var actions = Ui.Rect("PreparationActions", host); Ui.Top(actions, 88); actions.anchoredPosition = new Vector2(0, -66); Ui.Row(actions, 12);
            Button(actions, "Loadout", Loc.Text("장비 · 가져갈 물자"), ShowLoadout, accent: true);
            Button(actions, "Rest", Loc.Text("치료 · 식사"), () => Command(s => ExplorationSystem.Rest(s)));
            var hint = Text(host, "MapLabel", Loc.Text("지역을 누르면 상세 정보가 열립니다"), 62, Theme.TextDim, 27);
            Ui.Top(hint.rectTransform, 62); hint.rectTransform.anchoredPosition = new Vector2(0, -162);
            var maps = new List<MapDef>();
            foreach (var map in ExplorationSystem.OrderedMaps(_session.Data)) {
                if (ExplorationSystem.RouteLockReason(_session.Save, map.Id) != null) continue;
                bool occupied = _session.Save.Expeditions.Exists(e => !e.Resolved && e.MapId == map.Id);
                if (!occupied) maps.Add(map);
            }
            var overview = SeoulMapSelection.Draw(host, maps, "Explore_", ShowMapDetail);
            Ui.Stretch(overview, 0, 0, 236, 0);
            MaybeGuide(0, Loc.Text("첫 탐색을 준비해 봅시다"), ExplorationDialogue.Ready(_session.Save.Player.EmployerNpcId));
        }

        private void ShowLoadout()
        {
            OpenModal(Loc.Text("장비 · 물자 준비"), body => {
                new PlayerLoadoutPanel(body, _session, PickEquipment);
                Button(body, "Pack", Loc.Text("가져갈 물자 · {0}개", PackedCount()), PickSupplies, accent: true);
                if (_session.Save.ExplorationOverflow.Count > 0)
                    Button(body, "ClaimOverflow", Loc.Text("보관 중인 귀환 물자 받기"), () => { if (Command(s => { ExplorationSystem.ClaimOverflow(s, _session.Data); return true; })) { CloseModal(); ShowLoadout(); } });
                foreach (var questMap in RegionalExplorationQuest.Maps) {
                    string target = questMap;
                    if (RegionalExplorationQuest.Progress(_session.Save, target)?.ReadyToReport == true)
                        Button(body, "Report_" + target, Loc.Text("{0}에게 정찰 보고", Loc.TraderName(RegionalExplorationQuest.Npc(target))), () => ShowRegionalQuest(target, true));
                }
            });
        }

        private void ShowMapDetail(string id)
        {
            if (ExplorationSystem.RouteLockReason(_session.Save, id) != null) return;
            OpenModal(Loc.MapName(id), body => {
                var artwork = GameArt.MapThumbnail(body, id); Ui.Size(artwork.gameObject, 240);
                Text(body, "MapSummary", Loc.Text("6~10개 사건 · 입구에서 경로를 골라 출발\n이동 시 수분·에너지 소모 · 중간 탈출 가능"), 125, Theme.Text, 29);
                if (RegionalExplorationQuest.Npc(id) != null)
                    Text(body, "MapContact", Loc.Text("연락할 사람 · {0}", Loc.TraderName(RegionalExplorationQuest.Npc(id))), 75, Theme.Info);
                if (id == "YONGSAN_MARKET" && FirstExplorationQuest.IsPending(_session.Save))
                    Text(body, "MapObjective", FirstExplorationQuest.Objective(_session.Save), 115, Theme.Info, 28);
                else if (RegionalExplorationQuest.Progress(_session.Save, id)?.Accepted == true)
                    Text(body, "MapObjective", RegionalExplorationQuest.Objective(id), 115, Theme.Info, 28);
                string reason = ExplorationSystem.StartBlockReason(_session.Save, _session.Data, id);
                if (reason != null) Text(body, "DepartureReason", Loc.Text(reason), 90, Theme.Warn);
                Button(body, "EnterSelectedMap", RegionalExplorationQuest.NeedsIntroduction(_session.Save, id) ? Loc.Text("소개를 받고 탐색 준비") : Loc.Text("이 지역 탐색 시작"), () => { CloseModal(); BeginExploration(id); }, reason == null, true);
            });
            var panel = (RectTransform)_modal.Find("Panel");
            panel.anchorMin = new Vector2(0, .18f); panel.anchorMax = new Vector2(1, .82f);
            panel.offsetMin = new Vector2(36, 0); panel.offsetMax = new Vector2(-36, 0);
        }
        private static string SlotLabel(string slot)
        {
            switch (slot) {
                case "Weapon": return Loc.Text("총기"); case "Melee": return Loc.Text("근접 무기");
                case "Headwear": return Loc.Text("머리"); case "BodyArmor": return Loc.Text("방탄복");
                case "Earpiece": return Loc.Text("헤드셋"); case "TacticalRig": return Loc.Text("전술 조끼");
                default: return Loc.Text("가방");
            }
        }

        private void PickEquipment(string slot)
        {
            OpenModal(SlotLabel(slot), body => {
                Button(body, "BackToLoadout", Loc.Text("내 장비 전체 보기"), ShowLoadout, height: 76);
                PlayerLoadoutPanel.Choices(body, _session, slot,
                    id => { if (Command(s => PlayerEquipment.TryEquip(s, _session.Data, id), false)) { CloseModal(); ShowLoadout(); } },
                    () => { if (Command(s => PlayerEquipment.TryUnequip(s, _session.Data, slot), false)) { CloseModal(); ShowLoadout(); } });
                Text(body, "BuyLabel", Loc.Text("장비 구매"), 64, Theme.Info);
                foreach (var offer in Shop.OffersFor(_session.Save, _session.Data)) {
                    if (PlayerEquipment.SlotFor(_session.Data.GetItem(offer.ItemId)) != slot) continue;
                    string id = offer.ItemId;
                    Button(body, "Buy_" + id, ItemPresentation.Name(_session.Data, id) + " · " + Theme.Won(offer.Price), () => {
                        if (Command(s => Shop.TryBuy(s, _session.Data, id) && PlayerEquipment.TryEquip(s, _session.Data, id), false)) { CloseModal(); ShowLoadout(); }
                    }, Shop.BuyBlockReason(_session.Save, _session.Data, id) == null);
                }
            });
        }

        private int PackedCount() { int count = 0; foreach (var x in _packed) count += x.Value; return count; }
        private void PackRecommended()
        {
            _packed.Clear();
            foreach (string id in new[] { "MED05", "FOOD01", "FOOD05" }) {
                int available = Warehouse.CountOf(_session.Save.Warehouse, id);
                if (available > 0) _packed[id] = Math.Min(available, 2);
            }
            var profile = CombatProfiles.For(PlayerEquipment.Equipped(_session.Save, "Weapon"));
            int left = 50;
            foreach (var stack in _session.Save.Warehouse.Stacks) {
                if (profile == null || left <= 0 || ExplorationSystem.AmmoCaliber(stack.ItemId) != profile.Caliber) continue;
                int n = Math.Min(left, stack.Count); left -= n;
                _packed[stack.ItemId] = (_packed.TryGetValue(stack.ItemId, out var before) ? before : 0) + n;
            }
        }
        private void PickSupplies()
        {
            OpenModal(Loc.Text("가져갈 물자"), body => {
                Text(body, "PackHint", Loc.Text("터치하면 물자 1개, 탄약은 10발씩 담습니다. 출발할 때 창고에서 가져갑니다."), 88, Theme.TextDim);
                Button(body, "PackRecommended", Loc.Text("권장 물자 한 번에 담기"), () => {
                    PackRecommended();
                    PickSupplies();
                }, accent: true);
                var seen = new HashSet<string>();
                foreach (var stack in _session.Save.Warehouse.Stacks) {
                    string id = stack.ItemId;
                    if (!seen.Add(id) || !ExplorationSystem.IsSupply(_session.Data, id)) continue;
                    int owned = Warehouse.CountOf(_session.Save.Warehouse, id);
                    int selected = _packed.TryGetValue(id, out var count) ? count : 0;
                    Button(body, "Pack_" + id, ItemPresentation.Name(_session.Data, id) + "  " + selected + " / " + owned, () => {
                        _packed[id] = selected < owned ? Math.Min(owned, selected + (_session.Data.GetItem(id)?.Category == "Ammo" ? 10 : 1)) : 0;
                        PickSupplies();
                    });
                }
                Button(body, "PackedDone", Loc.Text("준비 완료"), () => { CloseModal(); Render(); }, accent: true);
            });
        }

        private void BeginExploration(string map)
        {
            if (RegionalExplorationQuest.NeedsIntroduction(_session.Save, map)) { ShowRegionalIntroduction(map); return; }
            var supplies = new List<ItemStack>();
            foreach (var x in _packed) if (x.Value > 0) supplies.Add(new ItemStack(x.Key, x.Value));
            if (Command(s => ExplorationSystem.Start(s, _session.Data, map, supplies))) _packed.Clear();
        }

        private void BuildRun()
        {
            Header(Loc.MapName(Run.MapId), Pause);
            _vitals = Ui.Label("Vitals", _content, "", 30, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Top(_vitals.rectTransform, 78); _vitals.rectTransform.anchoredPosition = new Vector2(0, -94);
            var stage = Ui.Rect("Stage", _content);
            stage.anchorMin = new Vector2(0, .42f); stage.anchorMax = new Vector2(1, .88f); stage.offsetMin = stage.offsetMax = Vector2.zero;
            _scene = new ExplorationScene(stage);
            _scene.SetLocation(Run.Location, Run.Weather.ToString(), Run.MapId);
            _scene.Animate(0, EnemyVisible, Run.Enemy?.Action.ToString() ?? "", 1, Run.Enemy?.Kind);
            var place = Ui.Label("Place", stage, Loc.Text(Run.Location), 35, TextAnchor.UpperLeft, Theme.Text);
            Ui.Top(place.rectTransform, 65, 20);
            if (FirstExplorationQuest.IsPending(_session.Save)) {
                var goal = Ui.Label("LiveQuestGoal", stage, FirstExplorationQuest.NextAction(_session.Save), 26, TextAnchor.UpperLeft, Theme.Info);
                Ui.Top(goal.rectTransform, 65, 20); goal.rectTransform.anchoredPosition = new Vector2(0, -80);
            }
            if (!FirstExplorationQuest.IsPending(_session.Save) && RegionalExplorationQuest.Progress(_session.Save, Run.MapId)?.Accepted == true &&
                RegionalExplorationQuest.Progress(_session.Save, Run.MapId)?.Completed != true) {
                var goal = Ui.Label("RegionalLiveGoal", stage, Loc.Text("정찰 이동 {0}/2 · 물품 {1} · 생존 귀환", Math.Min(2, Run.NodeIndex), Run.Loot.Count > 0 ? "✓" : "—"), 26, TextAnchor.UpperLeft, Theme.Info);
                Ui.Top(goal.rectTransform, 65, 20); goal.rectTransform.anchoredPosition = new Vector2(0, -80);
            }
            _enemyInfo = Ui.Label("EnemyStatus", stage, "", 30, TextAnchor.LowerCenter, Theme.Text);
            Ui.Bottom(_enemyInfo.rectTransform, 70, 20);
            var bottom = Ui.Rect("Situation", _content);
            bottom.anchorMin = Vector2.zero; bottom.anchorMax = new Vector2(1, .40f); bottom.offsetMin = bottom.offsetMax = Vector2.zero;
            Ui.Column(bottom, 12);
            _status = Text(bottom, "WeatherStatus", WeatherText(), 48, Theme.Info, 26);
            _message = Text(bottom, "SituationText", "", 108, Theme.Text, 34);
            _actions = Ui.Rect("Choices", bottom); Ui.Column(_actions, 12); Ui.Size(_actions.gameObject, flexHeight: 1);
            BuildActions(); UpdateLabels();
            if (Run.Phase == ExplorationPhase.Encounter)
                MaybeGuide(1, Loc.Text("주변을 살펴보세요"), Run.Enemy == null
                    ? LootContainers.Hint(Run.ContainerKind) + "\n" + Loc.Text("주변을 경계하며 하나만 챙깁니다. 필요한 물건을 고르세요.")
                    : Loc.Text("발소리를 따라 습격하거나, 숨어서 위험을 피할 수 있어요. 매장과 빈집에서는 물자를 찾아보세요."));
            else if (Run.Phase == ExplorationPhase.Combat)
                MaybeGuide(2, Loc.Text("적이 총을 들면 엄폐하세요"), Loc.Text("적이 조준하면 사격 전에 엄폐하세요. 재장전하거나 부상당했을 때 공격할 기회입니다. 아이템 메뉴를 열면 전투가 잠시 멈추지만 치료를 시작하면 다시 진행됩니다."));
            else if (Run.Phase == ExplorationPhase.Routes)
                MaybeGuide(3, Loc.Text("더 들어갈까요, 돌아갈까요?"), Loc.Text("이동할 때 수분과 에너지를 씁니다. 탈출구가 보이면 물건을 챙겨 안전하게 귀환할 수 있어요. 사망하거나 긴급 귀환하면 이번 전리품을 잃습니다."));
        }

        private void BuildActions()
        {
            if (Run.Phase == ExplorationPhase.Encounter) {
                bool human = Run.EncounterKind == "PMC" || Run.EncounterKind == "Scav";
                Button(_actions, "EncounterPrimary", human ? Loc.Text("발소리 쪽으로 습격") : Loc.Text("열어서 물건 고르기"), () => Command(s => ExplorationSystem.Choose(s, _session.Data, human ? EncounterChoice.Fight : EncounterChoice.Search)), accent: true);
                Button(_actions, "EncounterSecondary", human ? Loc.Text("숨어서 기다린다") : Loc.Text("눈에 띄는 물건부터 챙기기"), () => Command(s => ExplorationSystem.Choose(s, _session.Data, human ? EncounterChoice.Avoid : EncounterChoice.Leave)));
            }
            else if (Run.Phase == ExplorationPhase.Routes) {
                if (Run.NodeIndex < Run.NodeCount - 1) {
                    for (int i = 0; i < 2; i++) {
                        int route = i;
                        string box = Run.RouteContainers != null && Run.RouteContainers.Length > i ? Run.RouteContainers[i] : null;
                        string label = Loc.Text(Run.Routes[i]) + (box == null ? "" : "\n" + Loc.Text("{0} 단서", LootContainers.Name(box)));
                        Button(_actions, "Route" + i, label, () => Command(s => ExplorationSystem.Move(s, _session.Data, route)), _session.Save.Player.Energy > 0 && Run.UseRemaining <= 0, height: 100);
                    }
                }
                if (ExplorationSystem.CanExtract(Run))
                    Button(_actions, "Extract", Loc.Text("물건을 챙겨 탈출"), () => Command(s => ExplorationSystem.Extract(s, _session.Data)), accent: true);
                else Button(_actions, "FieldSupplies", Loc.Text("가방 · 물자 사용"), FieldSupplies, height: 72);
            }
            else if (Run.Phase == ExplorationPhase.Combat) {
                string weapon = PlayerEquipment.Equipped(_session.Save, "Weapon");
                var modes = CombatProfiles.For(weapon)?.Modes ?? new FireMode[0];
                var attacks = Ui.Rect("FireModes", _actions); Ui.Row(attacks, 10); Ui.Size(attacks.gameObject, 90);
                foreach (var mode in modes) {
                    var chosen = mode;
                    Button(attacks, "Fire_" + mode, mode == FireMode.Single ? Loc.Text("단발") : mode == FireMode.Burst ? Loc.Text("점사") : Loc.Text("연사"), () => {
                        if (Command(s => ExplorationSystem.Attack(s, _session.Data, chosen), false)) { Tween.Punch(_scene.Root, .02f, .12f); CheckPhase(); }
                    }, !string.IsNullOrEmpty(weapon), true);
                }
                var defense = Ui.Rect("Defense", _actions); Ui.Row(defense, 10); Ui.Size(defense.gameObject, 92);
                Button(defense, "Cover", Loc.Text("엄폐 / 회피"), () => Command(s => ExplorationSystem.Cover(s), false), accent: true);
                Button(defense, "CombatSupplies", Loc.Text("아이템"), FieldSupplies);
                if (!string.IsNullOrEmpty(PlayerEquipment.Equipped(_session.Save, "Melee")))
                    Button(_actions, "Melee", Loc.Text("근접 공격"), () => { Command(s => ExplorationSystem.Melee(s, _session.Data), false); CheckPhase(); }, height: 76);
            }
        }

        private string WeatherText()
        {
            return Run.Weather == ExplorationWeather.Rain ? Loc.Text("비 · 서로 발소리를 듣기 어렵습니다") : Run.Weather == ExplorationWeather.Fog ? Loc.Text("안개 · 양쪽 모두 명중률 감소") : Loc.Text("맑음 · 시야가 좋습니다");
        }

        private void UpdateLabels()
        {
            if (Run == null || _vitals == null) return;
            var p = _session.Save.Player;
            _vitals.text = Loc.Text("HP {0:0}   ·   수분 {1:0}   ·   에너지 {2:0}", p.Hp, p.Hydration, p.Energy);
            if (Run.Phase == ExplorationPhase.Combat) {
                string tell = EnemyTell();
                _enemyInfo.text = Loc.Text(Run.Enemy.Name) + "  ·  HP " + Math.Max(0, Run.Enemy.Hp).ToString("0");
                _message.text = tell;
                _message.color = Run.Enemy.Action == EnemyAction.Aiming ? Theme.Warn : Theme.Text;
                _status.text = Run.UseRemaining > 0 ? Loc.Text("아이템 사용 중 · {0:0.0}초", Run.UseRemaining) : Run.CoverRemaining > 0 ? Loc.Text("엄폐 중 · {0:0.0}초", Run.CoverRemaining) : WeatherText() + Loc.Text("  ·  탄약 {0}", ExplorationSystem.AmmoRemaining(_session.Save));
                foreach (var b in _actions.GetComponentsInChildren<Button>()) {
                    if (b.name.StartsWith("Fire_")) b.interactable = Run.AttackCooldown <= 0 && Run.UseRemaining <= 0 && ExplorationSystem.AmmoRemaining(_session.Save) >= (b.name.EndsWith("Auto") ? 5 : b.name.EndsWith("Burst") ? 3 : 1);
                    if (b.name == "Melee") b.interactable = Run.AttackCooldown <= 0 && Run.UseRemaining <= 0;
                    if (b.name == "Cover") {
                        b.interactable = Run.CoverCooldown <= 0 && Run.UseRemaining <= 0;
                        Ui.SetButtonLabel(b, Run.CoverCooldown > 0 ? Loc.Text("엄폐 대기 {0:0.0}초", Run.CoverCooldown) : Loc.Text("엄폐 / 회피"));
                    }
                }
            }
            else if (Run.Phase == ExplorationPhase.Encounter) {
                bool human = Run.EncounterKind == "Scav" || Run.EncounterKind == "PMC";
                _message.text = human ? (Run.Detected ? Loc.Text("상대가 이쪽을 발견했습니다!") : Loc.Text("가까운 곳에서 발소리가 들립니다. 어떻게 할까요?")) : Loc.Text("{0} 발견!", LootContainers.Name(Run.ContainerKind));
                _enemyInfo.text = human ? (Run.Detected ? Loc.Text(Run.Enemy.Name) : "") : LootContainers.Hint(Run.ContainerKind);
            }
            else {
                _message.text = Run.AwaitingEntryChoice ? Loc.Text("지역 입구에 도착했습니다. 어디로 진입할까요?") : ExplorationSystem.CanExtract(Run) ? Loc.Text("탈출구가 보입니다. 지금까지 얻은 물건을 지킬 수 있어요.") : p.Energy <= 0 ? Loc.Text("에너지가 부족합니다. 음식을 먹거나 긴급 귀환하세요.") : Loc.Text("주변이 조용해졌습니다. 다음은 어디로 갈까요?");
                _enemyInfo.text = Loc.Text("탐색 가방 · {0}종 확보", Run.Loot.Count);
                if (Run.Phase == ExplorationPhase.Routes) {
                    bool usingItem = Run.UseRemaining > 0;
                    _status.text = usingItem ? Loc.Text("아이템 사용 중 · {0:0.0}초", Run.UseRemaining) : WeatherText();
                    foreach (var button in _actions.GetComponentsInChildren<Button>()) {
                        if (button.name == "Route0" || button.name == "Route1")
                            button.interactable = p.Energy > 0 && !usingItem && !Run.Paused;
                        else if (button.name == "Extract") button.interactable = !usingItem && !Run.Paused;
                        else if (button.name == "FieldSupplies") button.interactable = !usingItem;
                    }
                }
            }
        }

        private string EnemyTell()
        {
            switch (Run.Enemy.Action) {
                case EnemyAction.Aiming: return Loc.Text("적이 총을 겨눕니다! 엄폐하세요 · {0:0.0}초", Run.Enemy.Remaining);
                case EnemyAction.Firing: return Loc.Text("적이 사격합니다!");
                case EnemyAction.Cover: return Loc.Text("적이 몸을 숨겼습니다. 회복할 기회입니다.");
                case EnemyAction.Reloading: return Loc.Text("적이 재장전 중입니다. 공격할 기회!");
                case EnemyAction.Injured: return Loc.Text("적이 부상으로 비틀거립니다!");
                default: return Loc.Text("적이 주변을 경계하고 있습니다.");
            }
        }

        private void FieldSupplies()
        {
            OpenModal(Loc.Text("탐색 가방"), body => {
                Text(body, "UseHint", Loc.Text("사용할 물자를 선택하세요. 치료하는 동안에도 적은 움직입니다."), 95, Theme.TextDim);
                var amounts = new Dictionary<string, int>();
                foreach (var stack in Run.Supplies) amounts[stack.ItemId] = (amounts.TryGetValue(stack.ItemId, out var n) ? n : 0) + stack.Count;
                foreach (var stack in Run.Loot) amounts[stack.ItemId] = (amounts.TryGetValue(stack.ItemId, out var n) ? n : 0) + stack.Count;
                foreach (var x in amounts) {
                    string id = x.Key;
                    if (CombatProfiles.ConsumableFor(id) == null) continue;
                    Button(body, "Use_" + id, ItemPresentation.Name(_session.Data, id) + " × " + x.Value, () => { if (Command(s => ExplorationSystem.Use(s, _session.Data, id), false)) { CloseModal(); CheckPhase(); } });
                }
                Text(body, "LootTitle", Loc.Text("탈출하면 가져갈 물건"), 64, Theme.Info);
                ItemRows(body, Run.Loot);
            });
        }

        private void BuildLootChoice()
        {
            Header(Loc.Text("{0} 발견!", LootContainers.Name(Run.ContainerKind)), Pause);
            var host = Ui.Rect("LootChoiceHost", _content); Ui.Stretch(host, 12, 12, 110, 24);
            var col = Ui.ScrollList("LootChoices", host, out var scroll, 16);
            LootContainerIllustration.Draw(col, Run.ContainerKind);
            Text(col, "ContainerHint", LootContainers.Hint(Run.ContainerKind), 95, Theme.Info, 32);
            Text(col, "LootChoiceHint", Loc.Text("주변을 경계하며 하나만 챙깁니다. 필요한 물건을 고르세요."), 100, Theme.TextDim, 28);
            for (int i = 0; i < Run.LootOptions.Count; i++) {
                int index = i; var item = Run.LootOptions[i];
                string detail = _session.Data.GetItem(item.ItemId)?.Category == "Ammo" ? " · " + ExplorationSystem.AmmoCaliber(item.ItemId) : "";
                var choice = Button(col, "LootChoice_" + i, ItemPresentation.Name(_session.Data, item.ItemId) + " × " + item.Count + detail + "\n" + Loc.Text("이 물건 챙기기"), () => {
                    if (Command(s => ExplorationSystem.ChooseLoot(s, index))) Sfx.OpenLoot();
                }, accent: true, height: 130);
                var icon = Ui.Icon("ItemIcon", choice.transform, _session.Data.GetItem(item.ItemId), 116);
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
                icon.rectTransform.sizeDelta = new Vector2(116, 116);
                icon.rectTransform.anchoredPosition = new Vector2(70, 0);
                var label = choice.GetComponentInChildren<Text>();
                Ui.Stretch(label.rectTransform, 145, 18, 4, 4); label.alignment = TextAnchor.MiddleLeft;
            }
        }

        private void BuildEncounterResult()
        {
            Header(Loc.MapName(Run.MapId), Pause);
            var host = Ui.Rect("EncounterResultHost", _content); Ui.Stretch(host, 18, 18, 120, 150);
            var col = Ui.ScrollList("EncounterResultScroll", host, out var scroll, 18);
            bool victory = Run.Enemy != null;
            Text(col, "EncounterResultTitle", victory ? Loc.Text("전투 승리!") : Loc.Text("수색 완료"), 120, Theme.Safe, 52);
            Text(col, "EncounterResultDetail", victory ? Loc.Text("{0}을(를) 쓰러뜨렸습니다.", Loc.Text(Run.Enemy.Name)) : LootContainers.Name(Run.ContainerKind) + " · " + Loc.Text(Run.Location), 90, Theme.Text, 34);
            Text(col, "RemainingHp", Loc.Text("남은 HP · {0:0}", _session.Save.Player.Hp), 65, Theme.Info);
            if (FirstExplorationQuest.IsPending(_session.Save))
                Text(col, "QuestProgress", FirstExplorationQuest.NextAction(_session.Save), 80, Theme.Safe, 28);
            Text(col, "EncounterLootTitle", Loc.Text("이번에 얻은 전리품"), 80, Theme.Text, 36);
            if (Run.EncounterLoot.Count == 0)
                Text(col, "NoEncounterLoot", Loc.Text("이번에는 얻은 물건이 없습니다."), 90, Theme.TextDim);
            else ItemRows(col, Run.EncounterLoot);
            if (Run.EncounterLoot.Count > 0)
                Text(col, "LootCarryHint", Loc.Text("전리품을 탐색 가방에 넣었습니다. 안전하게 탈출해야 기지로 가져갈 수 있어요."), 120, Theme.TextDim, 28);
            var next = Ui.Button("ContinueEncounter", _content, Loc.Text("확인 · 다음 길 선택"), () => Command(s => ExplorationSystem.ContinueEncounter(s)), Theme.AccentDim, 34);
            Ui.Bottom((RectTransform)next.transform, 106);
        }

        private void BuildResult()
        {
            var result = Run.Result;
            bool success = result.Outcome == ExplorationOutcome.Success;
            var host = Ui.Rect("ResultHost", _content); Ui.Stretch(host, 18, 18, 65, 130);
            var col = Ui.ScrollList("ResultScroll", host, out var scroll, 18);
            if (success && RegionalExplorationQuest.Progress(_session.Save, result.MapId)?.ReadyToReport == true)
                Text(col, "RegionalReportReady", Loc.Text("{0}에게 돌아가 정찰 결과를 보고하세요.", Loc.TraderName(RegionalExplorationQuest.Npc(result.MapId))), 90, Theme.Info, 28);
            Text(col, "ResultTitle", success ? Loc.Text("생존하여 귀환에 성공했습니다!") : result.Outcome == ExplorationOutcome.Death ? Loc.Text("당신은 사망했습니다.") : Loc.Text("탐색을 중단하고 구조되었습니다."), 155, success ? Theme.Safe : Theme.Danger, 46);
            Text(col, "ResultLevel", Loc.Text("캐릭터 Lv.{0}  ·  {1}", result.CharacterLevel, Loc.MapName(result.MapId)), 70, Theme.TextDim);
            if (_session.Save.FirstExplorationQuest?.ReadyToReport == true)
                Text(col, "QuestReady", FirstExplorationQuest.NextAction(_session.Save), 75, Theme.Safe, 30);
            if (result.Outcome == ExplorationOutcome.Death) {
                Text(col, "Killer", Loc.Text("나를 쓰러뜨린 상대\n{0} · {1}\n사용 무기: {2}", Loc.Text(result.KillerName), Loc.Text(result.KillerKind), string.IsNullOrEmpty(result.KillerWeaponId) ? Loc.Text(result.Cause) : ItemPresentation.Name(_session.Data, result.KillerWeaponId)), 185, Theme.Warn);
            }
            Text(col, "ResultItemsTitle", success ? Loc.Text("확보한 물건") : Loc.Text("잃어버린 전리품"), 70, Theme.Info, 36);
            ItemRows(col, success ? result.Items : result.LostLoot);
            if (!success) Text(col, "GearSafe", Loc.Text("장착한 장비와 사용하지 않은 출발 물자는 보존됩니다."), 90, Theme.TextDim);
            if (_session.Save.ExplorationOverflow.Count > 0)
                Text(col, "Overflow", Loc.Text("창고에 들어가지 못한 물자는 안전하게 보관 중입니다. 준비 화면에서 수령하세요."), 110, Theme.Warn);
            var returnButton = Ui.Button("ReturnToBase", _content, Loc.Text("기지로 돌아가기"), ReturnToBase, Theme.AccentDim, 34);
            Ui.Bottom((RectTransform)returnButton.transform, 106);
        }

        private void ItemRows(Transform body, List<ItemStack> items)
        {
            if (items.Count == 0) { Text(body, "EmptyItems", Loc.Text("물건이 없습니다."), 78, Theme.TextDim); return; }
            foreach (var stack in items) {
                var row = Ui.Rect("Item_" + stack.ItemId, body); Ui.Row(row, 18); Ui.Size(row.gameObject, 104);
                Ui.Icon("Icon", row, _session.Data.GetItem(stack.ItemId), 96);
                var name = Text(row, "Name", ItemPresentation.Name(_session.Data, stack.ItemId) + " × " + stack.Count, 104, Theme.Text);
                Ui.Size(name.gameObject, flexWidth: 1);
            }
        }

        private void ReturnToBase()
        {
            bool rescued = Run.Result.Outcome != ExplorationOutcome.Success;
            if (!Command(s => ExplorationSystem.Acknowledge(s), false)) return;
            _returning = true;
            var black = Ui.Panel("ReturnFade", _root, Color.black); black.raycastTarget = true;
            var group = black.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0;
            Tween.Play(group, "return", .45f, t => group.alpha = t, Tween.Ease.Linear, done: () => {
                _content.gameObject.SetActive(false);
                _shell.AfterAction();
                _root.GetComponent<Image>().color = Color.clear;
                Tween.Play(group, "return", .55f, t => group.alpha = 1 - t, Tween.Ease.Linear, done: () => {
                    Release(black.gameObject);
                    OpenModal(Loc.TraderName(_session.Save.Player.EmployerNpcId), body => {
                        GameArt.Portrait("RescuerPortrait", body, _session.Save.Player.EmployerNpcId, 330);
                        Text(body, "RescueLine", NpcLine(rescued), 185, Theme.Text, 34);
                        Button(body, "RescueContinue", Loc.Text("알겠어요"), Close, accent: true);
                    }, false);
                    CompactModal();
                });
            });
        }

        private string NpcLine(bool rescued)
        {
            if (!rescued) return Loc.Text("무사히 돌아왔군. 가져온 물건부터 정리하고 푹 쉬어.");
            switch (_session.Save.Player.EmployerNpcId) {
                case "DR_CHOI": return Loc.Text("내가 데려와서 응급처치를 했어요. 다음에는 몸 상태를 보고, 늦기 전에 돌아오세요.");
                case "YONGSAN_KIM": return Loc.Text("연락이 끊겨서 직접 찾아갔잖아. 물건보다 네 목숨이 먼저야. 다음엔 조심해.");
                default: return Loc.Text("간신히 구해 왔다. 장비는 챙겨 뒀으니 쉬어라. 다음에는 무리하지 말고 탈출구부터 확인해.");
            }
        }

        private void MaybeGuide(int step, string title, string line)
        {
            if (!_session.Save.ExplorationStarterPrepared) return;
            if (FirstExplorationQuest.IsPending(_session.Save) && _session.Save.FirstExplorationQuest?.Accepted != true) return;
            if (_modal != null || (_session.Save.ExplorationTutorialSeen & (1 << step)) != 0) return;
            OpenModal(title, body => {
                Text(body, "GuideNpc", Loc.TraderName(_session.Save.Player.EmployerNpcId), 60, Theme.Accent);
                Text(body, "GuideText", line, 240, Theme.Text, 34);
                Button(body, "ExplorationGuideContinue", Loc.Text("직접 해보기"), () => {
                    if (Command(s => { s.ExplorationTutorialSeen |= 1 << step; return true; }, false)) CloseModal();
                }, accent: true);
            }, false);
            CompactModal();
        }

        private void ShowHelp()
        {
            OpenModal(Loc.Text("탐색 즐기는 방법"), body => {
                Text(body, "Help1", Loc.Text("1. 무기를 착용하고 물·음식·치료제를 챙기세요. 한 지역은 6~10개 구간입니다."), 135, Theme.Text);
                Text(body, "Help2", Loc.Text("2. 적이 조준할 때 엄폐하고 재장전할 때 공격하세요. 날씨 효과는 적에게도 똑같이 적용됩니다."), 145, Theme.Text);
                Text(body, "Help3", Loc.Text("3. 이동은 수분과 에너지를 소모합니다. 탈출구가 보이면 전리품을 챙겨 귀환하세요."), 135, Theme.Text);
                Text(body, "Help4", Loc.Text("4. 사망·긴급 귀환 시 이번 전리품을 잃습니다. 캐릭터 레벨은 현재 표시만 제공하며 능력치에는 영향을 주지 않습니다."), 160, Theme.TextDim);
            });
        }

        private void Pause()
        {
            OpenModal(Loc.Text("잠시 숨 고르기"), body => {
                Text(body, "Paused", Loc.Text("탐색을 잠시 멈췄습니다. 앱을 닫아도 이 지점에서 이어집니다."), 120, Theme.Text);
                Button(body, "ResumeExploration", Loc.Text("계속 탐색"), CloseModal, accent: true);
                Button(body, "EmergencyReturn", Loc.Text("긴급 귀환 · 이번 전리품 포기"), () => {
                    OpenModal(Loc.Text("긴급 귀환"), confirm => {
                        Text(confirm, "Warning", Loc.Text("이번에 얻은 전리품을 포기하고 구조를 요청합니다. 장착 장비는 보존됩니다."), 170, Theme.Warn);
                        Button(confirm, "ConfirmEmergency", Loc.Text("전리품을 포기하고 귀환"), () => { if (Command(s => ExplorationSystem.EmergencyReturn(s, _session.Data))) CloseModal(); });
                    });
                });
            });
        }

        private void OpenModal(string title, Action<RectTransform> build, bool closable = true)
        {
            CloseModal();
            _modal = Ui.Modal("ExplorationModal", _root, title, closable ? (Action)CloseModal : null, out var body);
            var panelImage = _modal.Find("Panel").GetComponent<Image>();
            panelImage.sprite = null; panelImage.type = Image.Type.Simple; panelImage.color = Theme.Panel;
            if (!closable) {
                foreach (var b in _modal.GetComponentsInChildren<Button>()) if (b.name == "Close") b.gameObject.SetActive(false);
            }
            build(body);
        }
        private void CompactModal()
        {
            var panel = (RectTransform)_modal.Find("Panel");
            panel.anchorMin = new Vector2(0, .24f); panel.anchorMax = new Vector2(1, .76f);
            panel.offsetMin = new Vector2(36, 0); panel.offsetMax = new Vector2(-36, 0);
        }
        private static void Release(UnityEngine.Object value) { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
        private void CloseModal()
        {
            if (_modal == null) return;
            _modal.gameObject.SetActive(false); Release(_modal.gameObject); _modal = null;
        }
        private void Close() { gameObject.SetActive(false); _shell.ExplorationClosed(); Release(gameObject); }

        private void CheckPhase()
        {
            string key = HasRun ? Run.Phase.ToString() + ":" + Run.NodeIndex : "Prepare";
            if (_phaseKey != key) Render(); else UpdateLabels();
        }

        private void Update()
        {
            if (_session == null || !HasRun || _returning || _modal != null || !_focused || _background || _savingFailed) return;
            if (Run.Phase == ExplorationPhase.Result) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            var audioBefore = ExplorationAudioSnapshot.Capture(_session.Save);
            ExplorationSystem.Tick(_session.Save, _session.Data, dt);
            PlayExplorationAudio(audioBefore);
            CheckPhase();
            if (_scene != null) _scene.Animate(dt, EnemyVisible, Run.Enemy == null ? "" : Run.Enemy.Action.ToString(), Run.Enemy == null ? 1 : (float)(Run.Enemy.Hp / Math.Max(1, Run.Enemy.MaxHp)), Run.Enemy?.Kind);
            _saveClock += dt;
            if (_saveClock >= 1 || Run.Phase == ExplorationPhase.Result) SaveProgress();
        }
        private void SaveProgress()
        {
            if (_session == null || _savingFailed) return;
            try { _session.Commit(); _saveClock = 0; } catch (Exception e) { SaveError(e); }
        }
        private void PlayExplorationAudio(ExplorationAudioSnapshot before)
        {
            if (!Application.isPlaying || !gameObject.activeInHierarchy) return;
            var cues = ExplorationAudioCues.Between(before, ExplorationAudioSnapshot.Capture(_session.Save));
            if (cues.Count > 0) StartCoroutine(PlayCues(cues));
        }
        private System.Collections.IEnumerator PlayCues(List<string> cues)
        {
            foreach (var cue in cues) {
                if (!_focused || _background || _savingFailed || _returning || _modal != null) yield break;
                Sfx.ExplorationCue(cue);
                yield return new WaitForSecondsRealtime(cue.StartsWith("footstep") ? .22f : .085f);
            }
        }
        private void OnApplicationPause(bool paused) { _background = paused; if (paused) SaveProgress(); }
        private void OnApplicationFocus(bool focused) { _focused = focused; if (!focused) SaveProgress(); }
    }
}
