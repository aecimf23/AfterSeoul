using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    internal sealed class RaidWorkshopWindow
    {
        private readonly AppShell shell;
        private readonly GameSession session;
        private readonly Transform parent;
        private RectTransform root;
        private bool barter;
        internal RaidWorkshopWindow(AppShell shell,GameSession session,Transform parent) { this.shell=shell; this.session=session; this.parent=parent; Draw(); }
        private void Close() { if(root==null) return; root.gameObject.SetActive(false); if(Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject); else UnityEngine.Object.DestroyImmediate(root.gameObject); root=null; }
        private void Draw(string receipt=null)
        {
            Close(); root=Ui.Modal("RaidWorkshop",parent,Loc.Text("기지 시설 · 장비 교환"),Close,out var body);
            var tabs=Ui.Rect("WorkshopTabs",body); Ui.Row(tabs,10); Ui.Size(tabs.gameObject,80,flexHeight:0);
            AddButton(tabs,"FacilityTab","기지 시설",()=>{barter=false;Draw();});
            AddButton(tabs,"BarterTab","장비 교환",()=>{barter=true;Draw();});
            if(receipt!=null) PlayerLoadoutPanel.Explain(body,"ProjectReceipt",receipt,Theme.Safe);
            PlayerLoadoutPanel.Explain(body,"ProjectMoney",Loc.Text("보유 자금 · {0}",Theme.Won(session.Save.Player.Money)),Theme.Info);
            if(!barter) foreach(string id in new[]{"clinic","supplies","workbench"}) {
                var project=RaidProgression.Next(session.Save,id);
                if(project!=null) Project(body,project,false);
                else PlayerLoadoutPanel.Explain(body,"Complete_"+id,Loc.Text(id=="clinic"?"응급 처치대 · 완성":id=="supplies"?"보급 정리대 · 완성":"장비 작업대 · 완성"),Theme.Safe);
            }
            else foreach(var project in RaidProgression.Barters) {
                if(ExplorationSystem.RouteLockReason(session.Save,project.Map)!=null) continue;
                Project(body,project,true);
            }
        }
        private void Project(RectTransform body,RaidProject p,bool exchange)
        {
            Ui.Card(body,Loc.Text(p.Name)+(exchange?"":Loc.Text(" · 다음 {0}단계",p.Tier)),out var card);
            if(exchange) { Ui.Icon("BarterReward",card,session.Data.GetItem(p.Reward),100); PlayerLoadoutPanel.Explain(card,"Reward",ItemPresentation.Name(session.Data,p.Reward),Theme.Info); }
            else PlayerLoadoutPanel.Explain(card,"FacilityEffect",Loc.Text(p.Effect),Theme.Info);
            PlayerLoadoutPanel.Explain(card,"ProjectCosts",Costs(session,p)+(p.Money>0?"\n"+Theme.Won(p.Money):""));
            var why=RaidProgression.BlockReason(session.Save,session.Data,p);
            var action=AddButton(card,"Project_"+p.Id,exchange?"재료로 교환하기":"시설 개선하기",()=>Apply(s=>exchange?RaidProgression.Barter(s,session.Data,p.Id):RaidProgression.Upgrade(s,session.Data,p.Id),exchange?"교환 완료 · 창고에서 장비를 착용하세요":"시설 개선 완료 · 다음 탐색부터 효과를 확인하세요"));
            action.interactable=why==null;
            if(why!=null) PlayerLoadoutPanel.Explain(card,"ProjectWhy",Loc.Text(why),Theme.Warn);
            string goal=(exchange?"barter:":"upgrade:")+p.Id;
            AddButton(card,"TrackProject_"+p.Id,session.Save.RaidBase.TrackedProject==goal?"탐색 재료 목표로 추적 중":"필요 재료 추적하기",()=>Apply(s=>{s.RaidBase.TrackedProject=goal;return true;},"재료 목표를 추적합니다. 탐색 준비와 파밍 선택지에서 확인하세요."));
        }
        private void Apply(Func<GameSave,bool> command,string receipt)
        {
            try { if(session.ExecuteSavedAction(command)) {shell.AfterAction();Draw(Loc.Text(receipt));} else Draw(Loc.Text("진행할 수 없습니다. 재료와 창고 공간을 확인하세요.")); }
            catch(Exception) { Draw(Loc.Text("저장하지 못했습니다. 물품과 자금은 변경되지 않았습니다. 다시 시도하세요.")); }
        }
        internal static string Costs(GameSession session,RaidProject p)
        {
            var lines=new List<string>();
            foreach(var cost in p.Costs) lines.Add(Loc.Text("{0} {1}/{2}",ItemPresentation.Name(session.Data,cost.ItemId),Warehouse.CountOf(session.Save.Warehouse,cost.ItemId),cost.Count));
            return string.Join(" · ",lines);
        }
        private static UnityEngine.UI.Button AddButton(Transform body,string id,string text,Action click)
        {
            var button=Ui.Button(id,body,Loc.Text(text),click,Theme.AccentDim,28);Ui.Size(button.gameObject,82,flexWidth:1);return button;
        }
    }
}
