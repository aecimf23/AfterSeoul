using System;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Exploration
{
    [Serializable] public sealed class RaidBaseProgress
    {
        public int Clinic, Supplies, Workbench;
        public bool RecoveryReady;
        public string TrackedProject="upgrade:workbench";
    }
    public sealed class RaidProject
    {
        public string Id, Name, Effect, Map, Reward;
        public long Money;
        public int Tier;
        public ItemStack[] Costs;
    }
    public static class RaidProgression
    {
        public static int Level(GameSave s,string id) => id=="clinic" ? s.RaidBase.Clinic : id=="supplies" ? s.RaidBase.Supplies : s.RaidBase.Workbench;
        public static RaidProject Next(GameSave s,string id)
        {
            int level=Level(s,id); if(level>=2) return null;
            if(id=="clinic") return new RaidProject { Id=id,Tier=level+1,Name="응급 처치대",Effect="탐색 중 치료 아이템 회복량 +"+((level+1)*10)+" HP",Money=level==0?5000:12000,
                Costs=level==0 ? new[]{I("JUNK34",2),I("MED05",2)} : new[]{I("MED01",2),I("JUNK09",2)} };
            if(id=="supplies") return new RaidProject { Id=id,Tier=level+1,Name="보급 정리대",Effect="이동마다 에너지·수분 소모 각각 −"+(level+1),Money=level==0?4000:10000,
                Costs=level==0 ? new[]{I("JUNK20",2),I("JUNK18",2)} : new[]{I("JUNK07",1),I("JUNK09",2)} };
            if(id=="workbench") return new RaidProject { Id=id,Tier=level+1,Name="장비 작업대",Effect=level==0?"지역 장비 교환 해금":"고급 군수 장비 교환 해금",Money=level==0?6000:15000,
                Costs=level==0 ? new[]{I("JUNK03",2),I("JUNK23",2)} : new[]{I("JUNK29",1),I("JUNK08",1)} };
            return null;
        }
        public static readonly RaidProject[] Barters={
            B("smg","용산 김씨 · 기관단총","YONGSAN_MARKET","WPN08",0,I("JUNK23",2),I("JUNK20",2)),
            B("bag","동대문 최씨 · 배낭","YONGSAN_MARKET","BPK01",0,I("JUNK34",2),I("JUNK15",2)),
            B("armor","황 상사 · 경량 방탄복","YONGSAN_MARKET","AMR01",0,I("JUNK34",2),I("JUNK18",2)),
            B("rifle","구로 연락망 · 소총","GURO_FACTORY","WPN01",1,I("JUNK03",3),I("JUNK16",2)),
            B("unarmor","한강 연락망 · 방탄복","HAN_RIVER","AMR11",1,I("JUNK32",2),I("JUNK37",2)),
            B("marksman","남산 연락망 · 정밀 소총","NAMSAN_WOODS","WPN11",1,I("JUNK29",2),I("JUNK24",2)),
            B("rig","기지 연락망 · 전술 조끼","YONGSAN_BASE","RIG03",2,I("JUNK38",2),I("JUNK32",2))
        };
        static RaidProject B(string id,string name,string map,string reward,int tier,params ItemStack[] costs) => new RaidProject {Id=id,Name=name,Map=map,Reward=reward,Tier=tier,Costs=costs};
        static ItemStack I(string id,int n)=>new ItemStack(id,n);
        public static string BlockReason(GameSave s,IDataRegistry d,RaidProject project)
        {
            if(project==null) return "완료한 시설입니다";
            if(ExplorationSystem.IsActive(s)) return "귀환 후 이용할 수 있습니다";
            if(project.Map!=null && ExplorationSystem.RouteLockReason(s,project.Map)!=null) return "지역 연락망이 아직 열리지 않았습니다";
            if(project.Reward!=null && s.RaidBase.Workbench<project.Tier) return "장비 작업대 "+project.Tier+"단계가 필요합니다";
            if(s.Player.Money<project.Money) return "자금이 부족합니다";
            foreach(var x in project.Costs) if(Warehouse.CountOf(s.Warehouse,x.ItemId)<x.Count) return "필요한 재료를 더 모으세요";
            return null;
        }
        public static bool Upgrade(GameSave s,IDataRegistry d,string id)
        {
            var project=Next(s,id);
            if(BlockReason(s,d,project)!=null || !Exchange(s,d,project)) return false;
            if(id=="clinic") s.RaidBase.Clinic++; else if(id=="supplies") s.RaidBase.Supplies++; else s.RaidBase.Workbench++;
            return true;
        }
        public static bool Barter(GameSave s,IDataRegistry d,string id)
        {
            var project=Array.Find(Barters,x=>x.Id==id);
            if(project==null || BlockReason(s,d,project)!=null || !Exchange(s,d,project)) return false;
            if(s.RaidBase.TrackedProject=="barter:"+id) s.RaidBase.TrackedProject=null;
            return true;
        }
        public static RaidProject Goal(GameSave s)
        {
            string goal=s.RaidBase.TrackedProject;
            if(goal!=null && goal.StartsWith("barter:")) return Array.Find(Barters,x=>"barter:"+x.Id==goal);
            var next=goal!=null && goal.StartsWith("upgrade:") ? Next(s,goal.Substring(8)) : null;
            return next ?? Next(s,"workbench") ?? Next(s,"clinic") ?? Next(s,"supplies");
        }
        public static bool Needed(GameSave s,string itemId)
        {
            var goal=Goal(s); if(goal==null) return false;
            foreach(var cost in goal.Costs) if(cost.ItemId==itemId && Warehouse.CountOf(s.Warehouse,itemId)<cost.Count) return true;
            return false;
        }
        static bool Exchange(GameSave s,IDataRegistry d,RaidProject p)
        {
            var trial=new WarehouseState {Capacity=s.Warehouse.Capacity,BonusCapacity=s.Warehouse.BonusCapacity,Stacks=new List<ItemStack>(s.Warehouse.Stacks)};
            foreach(var cost in p.Costs) if(!Warehouse.TryRemove(trial,cost.ItemId,cost.Count)) return false;
            if(p.Reward!=null && (d.GetItem(p.Reward)==null || Warehouse.TryAdd(trial,d,p.Reward,1)>0)) return false;
            s.Warehouse.Stacks=trial.Stacks; s.Player.Money-=p.Money; return true;
        }
        public static long SupplyPrice(GameSave s,IDataRegistry d,string id,int count)
        {
            if(count<=0 || !(id=="MED05" || id=="FOOD01" || id=="FOOD02" || d.GetItem(id)?.Category=="Ammo")) return -1;
            return checked((Market.SellPrice(s,d,id)+Math.Max(1,(long)Math.Ceiling(ItemPricing.UnitValue(d.GetItem(id))*.1)))*count);
        }
        public static bool BuySupplies(GameSave s,IDataRegistry d,string id,int count)
        {
            if(ExplorationSystem.IsActive(s)||d.GetItem(id)==null||count<=0||count>100) return false;
            long price=SupplyPrice(s,d,id,count); if(price<0||s.Player.Money<price) return false;
            s.Player.Money-=price;
            return AddPurchased(s,d,id,count);
        }
        static bool AddPurchased(GameSave s,IDataRegistry d,string id,int count)
        {
            // Purchased overflow uses the existing return storage; paid items are never discarded.
            int overflow=Warehouse.TryAdd(s.Warehouse,d,id,count);
            if(overflow>0) { s.ExplorationOverflow.Add(new ItemStack(id,overflow)); }
            return true;
        }
        public static bool CanRequestRecovery(GameSave s) => !ExplorationSystem.IsActive(s) && (s.Exploration?.Result==null || s.Exploration.Result.Acknowledged) && s.Player.Money<5000;
        public static bool RequestRecovery(GameSave s)
        {
            if(!CanRequestRecovery(s)||s.RaidBase.RecoveryReady) return false;
            s.RaidBase.RecoveryReady=true; ExplorationSystem.Rest(s); return true;
        }
        public static ItemStack[] LoanKit()=>new[]{I("AMO05",50),I("MED05",2),I("FOOD01",2),I("FOOD02",3)};
    }
}
