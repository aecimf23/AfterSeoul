using System;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    public sealed partial class ExplorationView
    {
        private void ManageLoot()
        {
            OpenModal(Loc.Text("전리품 가방 정리"), body => {
                PlayerLoadoutPanel.Explain(body, "BagCapacity", Loc.Text("보관 {0}/{1}종 · 같은 물건은 한 종류로 합칩니다", Run.Loot.Count, Math.Max(8, Run.LootCapacity)), Theme.Info);
                if (Run.PendingLoot.Count > 0) {
                    var pending = Run.PendingLoot[0];
                    Ui.Icon("PendingLootIcon", body, _session.Data.GetItem(pending.ItemId), 120);
                    PlayerLoadoutPanel.Explain(body, "PendingLootName", Loc.Text("발견한 물건 · {0} × {1}", ItemPresentation.Name(_session.Data, pending.ItemId), pending.Count), Theme.Accent);
                    string slot = PlayerEquipment.SlotFor(_session.Data.GetItem(pending.ItemId));
                    PlayerLoadoutPanel.Explain(body, "PendingLootEffect", RaidItemDescription.Describe(_session.Data, pending.ItemId, slot == null ? null : PlayerEquipment.Equipped(_session.Save, slot)));
                    if (!ExplorationSystem.CanCarry(Run, pending.ItemId)) PlayerLoadoutPanel.Explain(body, "PendingSpaceRequired", Loc.Text("한 종류를 비우면 새 물건을 챙길 수 있습니다."), Theme.Warn);
                    Button(body, "TakePendingLoot", Loc.Text("이 물건 챙기기"), () => ResolveLoot(true), ExplorationSystem.CanCarry(Run, pending.ItemId), true);
                    Button(body, "LeavePendingLoot", Loc.Text("새 물건을 두고 가기"), () => ResolveLoot(false));
                }
                PlayerLoadoutPanel.Explain(body, "DiscardHint", Loc.Text("공간이 필요하면 아래 물건을 선택해 버리세요. 버린 물건은 되찾을 수 없습니다."));
                foreach (var stack in Run.Loot) {
                    string id = stack.ItemId; int count = stack.Count;
                    var discard = Button(body, "Discard_" + id, ItemPresentation.Name(_session.Data, id) + " × " + count + Loc.Text(" · 버리기"), () => {
                        OpenModal(Loc.Text("물건 버리기"), confirm => {
                            PlayerLoadoutPanel.Explain(confirm, "DiscardWarning", Loc.Text("{0} {1}개를 모두 버릴까요? 퀘스트에 필요한 물건도 잃습니다.", ItemPresentation.Name(_session.Data, id), count), Theme.Warn);
                            Button(confirm, "ConfirmDiscard", Loc.Text("버리고 공간 확보"), () => { if (Command(s => ExplorationSystem.DiscardLoot(s, id), false)) { Render(); ManageLoot(); } });
                            Button(confirm, "CancelDiscard", Loc.Text("보관하기"), ManageLoot, accent: true);
                        });
                    });
                    var icon = Ui.Icon("BagItemIcon", discard.transform, _session.Data.GetItem(id), 66);
                    icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0,.5f);
                    icon.rectTransform.sizeDelta = new Vector2(66,66); icon.rectTransform.anchoredPosition = new Vector2(46,0);
                    Ui.Stretch(discard.GetComponentInChildren<Text>().rectTransform, 90, 12, 4, 4);
                }
                Button(body, "LootManagementDone", Loc.Text("정리 완료"), () => { CloseModal(); Render(); }, Run.PendingLoot.Count == 0, true);
                if (Run.Phase == ExplorationPhase.Routes && ExplorationSystem.CanExtract(Run))
                    Button(body, "ExtractFromBag", Loc.Text("현재 전리품으로 안전하게 탈출"), () => { if (Command(s => ExplorationSystem.Extract(s, _session.Data))) CloseModal(); }, accent: true);
            });
        }

        private void ResolveLoot(bool take)
        {
            if (!Command(s => ExplorationSystem.ResolvePendingLoot(s, take), false)) return;
            CloseModal(); Render();
            if (Run.PendingLoot.Count > 0) ManageLoot();
        }
    }
}
