using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    public sealed partial class ExplorationView
    {
        private string _preparingMap;

        private void ReturnToPreparation()
        {
            if (_preparingMap != null) ShowMapDetail(_preparingMap);
            else ShowLoadout();
        }

        private void DrawPreparation(string id)
        {
            _preparingMap = id;
            OpenModal(Loc.MapName(id) + Loc.Text(" · 출발 준비"), body => {
                var art = GameArt.MapThumbnail(body, id); Ui.Size(art.gameObject, 160);
                PlayerLoadoutPanel.Explain(body, "MapSummary", Loc.Text("6~10개 사건 · 입구에서 경로 선택 · 중간 탈출 가능"));
                PlayerLoadoutPanel.Explain(body,"RegionSpecialty",Loc.Text("주요 파밍 · ")+Loc.Text(RaidRegions.Theme(id)),Theme.Info);
                var goal=RaidProgression.Goal(_session.Save);
                if(goal!=null) PlayerLoadoutPanel.Explain(body,"MaterialGoal",Loc.Text("재료 목표 · ")+Loc.Text(goal.Name)+"\n"+RaidWorkshopWindow.Costs(_session,goal),Theme.Accent);
                string objective = id == "YONGSAN_MARKET" && FirstExplorationQuest.IsPending(_session.Save)
                    ? FirstExplorationQuest.Objective(_session.Save) : RegionalExplorationQuest.Progress(_session.Save, id)?.Accepted == true ? RegionalExplorationQuest.Objective(id) : "";
                if (objective.Length > 0) PlayerLoadoutPanel.Explain(body, "MapObjective", objective, Theme.Info);
                var player = _session.Save.Player;
                PlayerLoadoutPanel.Explain(body, "PreparationVitals", Loc.Text("HP {0:0} · 수분 {1:0} · 에너지 {2:0}", player.Hp, player.Hydration, player.Energy), Theme.Info);
                PlayerLoadoutPanel.Explain(body, "QuickEquipHint", Loc.Text("장비 칸을 눌러 바로 교체"));
                RectTransform row = null;
                for (int i = 0; i < PlayerEquipment.Slots.Length; i++) {
                    if (i % 2 == 0) { row = Ui.Rect("QuickEquipmentRow", body); Ui.Row(row, 10); Ui.Size(row.gameObject, 94, flexHeight: 0); }
                    string slot = PlayerEquipment.Slots[i];
                    string equipped = PlayerEquipment.Equipped(_session.Save, slot);
                    var b = Button(row, "QuickSlot_" + slot, SlotLabel(slot) + " · " + (equipped == null ? Loc.Text("선택") : ItemPresentation.Name(_session.Data, equipped)), () => PickEquipment(slot), height: 94);
                    Ui.Size(b.gameObject, 94, flexWidth: 1, flexHeight: 0);
                    if (equipped != null) {
                        var icon = Ui.Icon("QuickEquipmentIcon", b.transform, _session.Data.GetItem(equipped), 54);
                        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
                        icon.rectTransform.sizeDelta = new Vector2(54,54); icon.rectTransform.anchoredPosition = new Vector2(38,0);
                        var label = b.GetComponentInChildren<Text>(); Ui.Stretch(label.rectTransform, 76, 10, 4, 4);
                    }
                }
                PlayerLoadoutPanel.Explain(body, "PreparationSupplies", SupplySummary(), Theme.Info);
                var packRow = Ui.Rect("QuickPackRow", body); Ui.Row(packRow, 10); Ui.Size(packRow.gameObject, 88, flexHeight: 0);
                Button(packRow, "QuickPack", Loc.Text("권장 물자 담기"), () => { PackRecommended(); ReturnToPreparation(); }, !RecoveryPlanned, accent: true, height: 88);
                Button(packRow, "EditPack", Loc.Text("직접 선택"), PickSupplies, !RecoveryPlanned, height: 88);
                Button(body,"BuyFieldSupplies",Loc.Text("부족한 기본 물자 구매"),BuyFieldSupplies,height:82);
                if(id=="YONGSAN_MARKET" && (RaidProgression.CanRequestRecovery(_session.Save)||_session.Save.RaidBase.RecoveryReady)) {
                    Button(body,"RecoveryKit",RecoveryPlanned ? Loc.Text("대여 보급 사용 중 · 취소") : Loc.Text("재도전 지원 · 권총과 물자 대여"),()=>{
                        if(Command(s=>{if(s.RaidBase.RecoveryReady){s.RaidBase.RecoveryReady=false;return true;}return RaidProgression.RequestRecovery(s);},false)) {_packed.Clear();ReturnToPreparation();}
                    },height:88);
                    PlayerLoadoutPanel.Explain(body,"RecoveryTerms",Loc.Text("자금 5,000원 미만 지원 · 용산 전자상가 전용\n권총·탄약 50발·치료 2·음식 2·음료 3 대여. 개인 물자는 가져가지 않으며 대여품은 귀환 시 반납합니다."));
                }
                PlayerLoadoutPanel.Explain(body, "PackOwnership", Loc.Text("창고에서만 챙깁니다 · 출발할 때 차감"));
                AddBaseRecovery(body);
                string warning = PreparationWarning();
                if (warning.Length > 0) PlayerLoadoutPanel.Explain(body, "SupplyWarning", warning, Theme.Warn);
                string reason = ExplorationSystem.StartBlockReason(_session.Save, _session.Data, id);
                if (reason != null) PlayerLoadoutPanel.Explain(body, "DepartureReason", Loc.Text(reason), Theme.Warn);
            });
            var panel = (RectTransform)_modal.Find("Panel");
            string block = ExplorationSystem.StartBlockReason(_session.Save, _session.Data, id);
            var enter = Button(panel, "EnterSelectedMap", RegionalExplorationQuest.NeedsIntroduction(_session.Save, id) ? Loc.Text("소개를 받고 탐색 준비") : Loc.Text("준비 완료 · 출발"), () => { CloseModal(); BeginExploration(id); }, block == null, true, 100);
            Ui.Size(enter.gameObject, 100, flexHeight: 0);
        }

        private int PackedMatching(Func<string, bool> matches)
        {
            int total = 0;
            foreach (var x in _packed) if (matches(x.Key)) total += Math.Min(x.Value, Warehouse.CountOf(_session.Save.Warehouse, x.Key));
            return total;
        }

        private void RecommendAlternative(Func<ConsumableProfile, bool> matches)
        {
            foreach (var x in _packed) {
                var effect = CombatProfiles.ConsumableFor(x.Key);
                if (x.Value > 0 && effect != null && matches(effect)) return;
            }
            foreach (var stack in _session.Save.Warehouse.Stacks) {
                var effect = CombatProfiles.ConsumableFor(stack.ItemId);
                if (stack.Count <= 0 || effect == null || !matches(effect)) continue;
                _packed[stack.ItemId] = Math.Min(2, Warehouse.CountOf(_session.Save.Warehouse, stack.ItemId));
                return;
            }
        }

        private string SupplySummary()
        {
            if(RecoveryPlanned) return Loc.Text("대여 P17 · 탄약 50발 · 치료 2 · 음식 2 · 음료 3\n장착한 개인 총기 대신 대여 권총을 사용합니다.");
            var gun = CombatProfiles.For(PlayerEquipment.Equipped(_session.Save, "Weapon"));
            int ammo = gun == null ? 0 : PackedMatching(id => ExplorationSystem.AmmoCaliber(id) == gun.Caliber);
            return Loc.Text("탄약 {0}발 ({1}) · 치료 {2} · 음식 {3} · 물 {4}\n전리품 공간 {5}종 · 가져가는 보급품은 별도 보관", ammo, gun?.Caliber ?? "총기 없음",
                PackedMatching(id => CombatProfiles.ConsumableFor(id)?.Hp > 0),
                PackedMatching(id => CombatProfiles.ConsumableFor(id)?.Energy >= 30),
                PackedMatching(id => CombatProfiles.ConsumableFor(id)?.Hydration >= 30), RaidEquipment.LootCapacity(_session.Save, _session.Data));
        }

        private string PreparationWarning()
        {
            if(RecoveryPlanned) return "";
            var missing = new List<string>();
            var gun = CombatProfiles.For(PlayerEquipment.Equipped(_session.Save, "Weapon"));
            if (gun != null && PackedMatching(id => ExplorationSystem.AmmoCaliber(id) == gun.Caliber) == 0) missing.Add("호환 탄약");
            if (PackedMatching(id => CombatProfiles.ConsumableFor(id)?.Hp > 0) == 0) missing.Add("치료제");
            if (PackedMatching(id => CombatProfiles.ConsumableFor(id)?.Energy >= 30) == 0) missing.Add("음식");
            if (PackedMatching(id => CombatProfiles.ConsumableFor(id)?.Hydration >= 30) == 0) missing.Add("물");
            string warning = missing.Count == 0 ? "" : Loc.Text("부족한 준비물: {0}. 그대로 출발할 수도 있습니다.", string.Join(" · ", missing));
            if (_session.Save.Player.Hp < 50 || _session.Save.Player.Energy < 30 || _session.Save.Player.Hydration < 30)
                warning += "\n" + Loc.Text("몸 상태가 좋지 않습니다. 출발 전에 회복하세요.");
            return warning.Trim();
        }

        private bool RecoveryPlanned => _session.Save.RaidBase.RecoveryReady && RaidProgression.CanRequestRecovery(_session.Save);
        private void BuyFieldSupplies()
        {
            OpenModal(Loc.Text("기본 보급품 구매"),body=>{
                PlayerLoadoutPanel.Explain(body,"SupplyBudget",Loc.Text("보유 자금 · {0}\n구매한 물자는 출발 가방에도 담습니다. 창고를 넘친 수량은 귀환 물자 보관함에서 받을 수 있습니다.",Theme.Won(_session.Save.Player.Money)),Theme.Info);
                var offers=new List<string>{"MED05","FOOD01","FOOD02"};
                var gun=CombatProfiles.For(PlayerEquipment.Equipped(_session.Save,"Weapon"));
                string ammo=null; long best=long.MaxValue;
                foreach(var item in _session.Data.AllItems) if(item.Category=="Ammo" && gun!=null && ExplorationSystem.AmmoCaliber(item.Id)==gun.Caliber) {
                    long price=RaidProgression.SupplyPrice(_session.Save,_session.Data,item.Id,30);
                    if(price<best){best=price;ammo=item.Id;}
                }
                if(ammo!=null) offers.Add(ammo);
                foreach(var id in offers) {
                    string item=id; int count=_session.Data.GetItem(item).Category=="Ammo" ? 30 : 1;
                    long price=RaidProgression.SupplyPrice(_session.Save,_session.Data,item,count);
                    Button(body,"BuySupply_"+item,ItemPresentation.Name(_session.Data,item)+" × "+count+" · "+Theme.Won(price),()=>{
                        if(Command(s=>RaidProgression.BuySupplies(s,_session.Data,item,count),false)) {
                            _packed[item]=Math.Min(Warehouse.CountOf(_session.Save.Warehouse,item),(_packed.TryGetValue(item,out var old)?old:0)+count);
                            BuyFieldSupplies();
                        }
                    },_session.Save.Player.Money>=price,accent:true);
                    PlayerLoadoutPanel.Explain(body,"SupplyEffect",RaidItemDescription.Describe(_session.Data,item));
                }
                Button(body,"ReturnAfterSupplyPurchase",Loc.Text("출발 준비로 돌아가기"),ReturnToPreparation);
            });
        }

        private void AddBaseRecovery(RectTransform body)
        {
            var p = _session.Save.Player;
            if (p.Hp >= 100 && p.Hydration >= 100 && p.Energy >= 100) return;
            var seen = new HashSet<string>();
            foreach (var x in _session.Save.Warehouse.Stacks) {
                string id = x.ItemId; var effect = CombatProfiles.ConsumableFor(id);
                if (effect == null || x.Count <= 0 || !seen.Add(id)) continue;
                bool helps = effect.Hp > 0 && p.Hp < 100 || effect.Hydration > 0 && p.Hydration < 100 || effect.Energy > 0 && p.Energy < 100;
                if (!helps) continue;
                Button(body, "Recover_" + id, Loc.Text("지금 사용 · {0}", ItemPresentation.Name(_session.Data, id)) + "\n" + RaidItemDescription.Describe(_session.Data, id), () => {
                    if (Command(s => ExplorationSystem.Use(s, _session.Data, id), false)) {
                        if (_packed.ContainsKey(id)) _packed[id] = Math.Min(_packed[id], Warehouse.CountOf(_session.Save.Warehouse, id));
                        ReturnToPreparation();
                    }
                }, height: 102);
            }
        }
    }
}
