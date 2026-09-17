using System;
using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Factory
{
    [Serializable]
    public sealed class ProductionState
    {
        public string SelectedWeaponId = "WPN04";
        public int UnlockedCount = 1;
        public int SpeedLevel;
        public double WorkDone;
        public long TotalProduced;
        public long TotalWages;
        public DateTimeOffset LastWorkedAt;
        public List<string> AssignedScavUids = new List<string>();
        public ProductionContractState Contract;
        public int AssemblyJigLevel;
        public int PowerToolsLevel;
        public int ExtraBenches;
    }

    [Serializable]
    public sealed class ProductionContractState
    {
        public string WeaponId;
        public int Required;
        public int Crafted;
    }

    public enum ProductionEquipment { AssemblyJig, PowerTools, ExtraBench }

    public sealed class ProductionGun
    {
        public string WeaponId;
        public double WorkRequired;
        public long Wage;
        public long UnlockCost;
        public int SampleCount;
    }

    public sealed class ProductionTuning
    {
        public double BaseWork = 10;
        public double WorkGrowth = 1.18;
        public long BaseWage = 5000;
        public double WageGrowth = 1.30;
        public double ClickWork = 1;
        public double AutoWorkPerScav = .01;
        public double SpeedMultiplier = 1.2;
        public int MaxSpeedLevel = 20;
        public long FirstSpeedCost = 5000;
        public double SpeedCostGrowth = 1.65;
        public long FirstUnlockCost = 15000;
        public double UnlockCostGrowth = 1.55;
        public int FirstSampleCount = 3;
        public int MaxSampleCount = 8;
        public int SampleIncreaseEvery = 4;
        public long FirstJigCost = 7500;
        public double JigCostGrowth = 2.1;
        public int MaxJigLevel = 5;
        public double FeedBaseInterval = 1.6;
        public double FeedLevelBonus = .095;
        public double FeedMinInterval = .55;
        public double[] MaterialWork = { 1, 2, 3, 5 };
        public long FirstToolsCost = 20000;
        public double ToolsCostGrowth = 2.2;
        public int MaxToolsLevel = 5;
        public double ToolsAutoBonus = .5;
        public long FirstBenchCost = 30000;
        public double BenchCostGrowth = 2.3;
        public int MaxExtraBenches = 2;
        internal IReadOnlyList<ProductionGun> Catalog;
    }

    public readonly struct ProductionResult
    {
        public readonly long CompletedCount;
        public readonly long Wages;
        public ProductionResult(long count, long wages) { CompletedCount=count; Wages=wages; }
    }

    /// <summary>Supplied-material contracts: completion pays money, never creates an inventory gun.
    /// Mutating callers settle the timeline first (GameSession owns that ordering).</summary>
    public static class ProductionWork
    {
        private static readonly ProductionTuning Defaults = new ProductionTuning();
        // Pistols, close-range guns, assault rifles, marksman rifles, then specialist contracts.
        private static readonly string[] Order = {
            "WPN04", "WPN21", "WPN23", "WPN08", "WPN09", "WPN14", "WPN20", "WPN13",
            "WPN19", "WPN26", "WPN01", "WPN02", "WPN05", "WPN11", "WPN17", "WPN06",
            "WPN07", "WPN15", "WPN27", "WPN03", "WPN18", "WPN12", "WPN16", "WPN10", "WPN22"
        };
        private static ProductionTuning Tuning(IDataRegistry data) => data.Balance.Production ?? Defaults;
        private static double Safe(double n,double fallback) => double.IsNaN(n)||double.IsInfinity(n)||n<=0 ? fallback : n;
        private static long Cost(double n) => (long)Math.Max(1,Math.Min(1000000000000d,Safe(n,1000000000000d)));
        public static IReadOnlyList<ProductionGun> Guns(IDataRegistry data)
        {
            var t=Tuning(data);
            if(t.Catalog!=null) return t.Catalog;
            var result=new ProductionGun[Order.Length];
            for(int i=0;i<result.Length;i++) result[i]=new ProductionGun {
                WeaponId=Order[i], WorkRequired=Math.Min(1000000,Safe(t.BaseWork,10)*Math.Pow(Safe(t.WorkGrowth,1.18),i)),
                Wage=Cost(Math.Max(1,t.BaseWage)*Math.Pow(Safe(t.WageGrowth,1.3),i)),
                UnlockCost=i==0 ? 0 : Cost(Math.Max(1,t.FirstUnlockCost)*Math.Pow(Safe(t.UnlockCostGrowth,1.55),i-1)),
                SampleCount=i==0 ? 0 : Math.Max(1,Math.Min(Math.Min(8,t.MaxSampleCount),t.FirstSampleCount+(i-1)/Math.Max(1,t.SampleIncreaseEvery)))
            };
            t.Catalog=Array.AsReadOnly(result); return t.Catalog;
        }

        public static void Initialize(GameSave save,IDataRegistry data)
        {
            if(save.Factory==null) save.Factory=new FactoryState();
            var p=save.Factory.Production;
            if(p==null) save.Factory.Production=p=new ProductionState();
            if(p.AssignedScavUids==null) p.AssignedScavUids=new List<string>();
            p.UnlockedCount=Math.Max(1,Math.Min(Order.Length,p.UnlockedCount));
            p.SpeedLevel=Math.Max(0,Math.Min(MaxLevel(data),p.SpeedLevel));
            p.AssemblyJigLevel=Math.Max(0,Math.Min(EquipmentMax(data,ProductionEquipment.AssemblyJig),p.AssemblyJigLevel));
            p.PowerToolsLevel=Math.Max(0,Math.Min(EquipmentMax(data,ProductionEquipment.PowerTools),p.PowerToolsLevel));
            p.ExtraBenches=Math.Max(0,Math.Min(EquipmentMax(data,ProductionEquipment.ExtraBench),p.ExtraBenches));
            // Legacy factories could employ nine workers. Retain the first three valid
            // workers; release only overflow production workers, never expedition scavs.
            var retained=new List<string>();var seenWorkers=new HashSet<string>();
            foreach(var id in p.AssignedScavUids) {
                if(!seenWorkers.Add(id))continue;
                var worker=Find(save,id);
                if(worker==null || worker.Status!=ScavStatus.Working)continue;
                if(retained.Count<3)retained.Add(id);
                else worker.Status=ScavStatus.Idle;
            }
            p.AssignedScavUids.Clear();p.AssignedScavUids.AddRange(retained);
            if(p.Contract!=null) {
                if(p.UnlockedCount>=Order.Length || p.Contract.WeaponId!=Order[p.UnlockedCount]) p.Contract=null;
                else {
                    p.Contract.Required=Math.Max(1,Math.Min(8,p.Contract.Required));
                    p.Contract.Crafted=Math.Max(0,Math.Min(p.Contract.Required,p.Contract.Crafted));
                }
            }
            int index=Array.IndexOf(Order,p.SelectedWeaponId);
            if(index<0 || (index>=p.UnlockedCount && p.Contract?.WeaponId!=p.SelectedWeaponId)) { p.SelectedWeaponId=Order[0]; p.WorkDone=0; }
            if(double.IsNaN(p.WorkDone)||double.IsInfinity(p.WorkDone)||p.WorkDone<0) p.WorkDone=0;
            if(p.LastWorkedAt==default) p.LastWorkedAt=save.SavedAt;
        }
        private static int MaxLevel(IDataRegistry data) => Math.Max(0,Math.Min(20,Tuning(data).MaxSpeedLevel));
        public static ProductionGun Current(GameSave save,IDataRegistry data)
        {
            Initialize(save,data);
            return Guns(data)[Array.IndexOf(Order,save.Factory.Production.SelectedWeaponId)];
        }
        public static double WorkPerClick(GameSave save,IDataRegistry data) => MaterialWork(data,1);
        public static double FeedInterval(GameSave save,IDataRegistry data) {
            var t=Tuning(data);
            return Math.Max(Safe(t.FeedMinInterval,.55),Safe(t.FeedBaseInterval,1.6)/(1+Safe(t.FeedLevelBonus,.095)*Math.Max(0,Math.Min(20,save.Factory.Production.SpeedLevel))));
        }
        public static int MaterialTier(GameSave save,IDataRegistry data) {
            int level=Math.Max(0,Math.Min(5,save.Factory.Production.AssemblyJigLevel));
            return level==0?1:level<3?2:level<5?3:4;
        }
        public static double MaterialWork(IDataRegistry data,int tier) {
            int i=Math.Max(1,Math.Min(4,tier))-1;var values=Tuning(data).MaterialWork;
            return values!=null && values.Length>i ? Safe(values[i],Defaults.MaterialWork[i]) : Defaults.MaterialWork[i];
        }
        public static double MaterialChance(GameSave save,IDataRegistry data,int tier) {
            if(tier<1 || tier>4)return 0;
            int count=0;
            for(int i=1;i<=100;i++)if(MaterialTierForSequence(save,i)==tier)count++;
            return count/100d;
        }
        internal static int MaterialTierForSequence(GameSave save,long sequence) {
            int level=Math.Max(0,Math.Min(5,save.Factory.Production.AssemblyJigLevel));
            // A coprime stride visits every percentile once per 100 parts without random save state.
            int roll=(int)(((sequence-1)%100)*37%100);
            int[] first={100,65,30,15,5,0}, second={100,100,100,50,25,10}, third={100,100,100,100,100,50};
            return roll<first[level]?1:roll<second[level]?2:roll<third[level]?3:4;
        }
        private static double Speed(GameSave save,IDataRegistry data) => Math.Pow(Math.Min(2,Safe(Tuning(data).SpeedMultiplier,1.2)),Math.Max(0,Math.Min(MaxLevel(data),save.Factory.Production.SpeedLevel)));
        public static double AutoWorkPerSecond(GameSave save,IDataRegistry data)
        {
            int count=0; var seen=new HashSet<string>();
            foreach(var id in save.Factory.Production.AssignedScavUids)
                if(seen.Add(id) && Find(save,id)?.Status==ScavStatus.Working) count++;
            return count*Math.Min(100,Safe(Tuning(data).AutoWorkPerScav,.01))*Speed(save,data)*(1+Math.Min(10,Safe(Tuning(data).ToolsAutoBonus,.5))*EquipmentLevel(save,ProductionEquipment.PowerTools));
        }

        internal static ProductionResult AddWork(GameSave save,IDataRegistry data,double work)
        {
            var gun=Current(save,data); var p=save.Factory.Production;
            bool trial=p.Contract!=null && p.Contract.WeaponId==gun.WeaponId;
            if(trial && TrialReady(save)) { p.WorkDone=0; return default; }
            double total=p.WorkDone+Math.Max(0,work);
            long count=(long)Math.Min(1000000000,Math.Floor((total+1e-9)/gun.WorkRequired));
            if(trial) count=Math.Min(count,p.Contract.Required-p.Contract.Crafted);
            p.WorkDone=Math.Max(0,total-count*gun.WorkRequired);
            if(trial && count>0) {
                p.Contract.Crafted+=(int)count;
                p.TotalProduced=(long)Math.Min(long.MaxValue,(decimal)p.TotalProduced+count);
                if(TrialReady(save)) p.WorkDone=0;
                return new ProductionResult(count,0);
            }
            if(count==0) return default;
            // Saturation prevents corrupt or very old saves wrapping money negative.
            long wages=(long)Math.Min((decimal)long.MaxValue-save.Player.Money,(decimal)count*gun.Wage);
            save.Player.Money+=wages;
            p.TotalProduced=(long)Math.Min(long.MaxValue,(decimal)p.TotalProduced+count);
            p.TotalWages=(long)Math.Min(long.MaxValue,(decimal)p.TotalWages+wages);
            StarterSupport.RecordProduction(save);
            return new ProductionResult(count,wages);
        }
        public static long SpeedUpgradeCost(GameSave save,IDataRegistry data)
        {
            if(save.Factory.Production.SpeedLevel>=MaxLevel(data)) return 0;
            var t=Tuning(data); return Cost(Math.Max(1,t.FirstSpeedCost)*Math.Pow(Safe(t.SpeedCostGrowth,1.65),save.Factory.Production.SpeedLevel));
        }
        public static bool TryUpgrade(GameSave save,IDataRegistry data)
        {
            long cost=SpeedUpgradeCost(save,data);
            if(cost<=0 || save.Player.Money<cost) return false;
            save.Player.Money-=cost; save.Factory.Production.SpeedLevel++; return true;
        }
        public static ProductionGun NextGun(GameSave save,IDataRegistry data) => save.Factory.Production.UnlockedCount>=Order.Length ? null : Guns(data)[save.Factory.Production.UnlockedCount];
        public static bool TryUnlockNext(GameSave save,IDataRegistry data)
        {
            var p=save.Factory.Production;
            var next=NextGun(save,data); if(p.Contract!=null || next==null || save.Player.Money<next.UnlockCost) return false;
            p.Contract=new ProductionContractState { WeaponId=next.WeaponId, Required=next.SampleCount };
            p.SelectedWeaponId=next.WeaponId; p.WorkDone=0; return true;
        }
        public static bool TrialReady(GameSave save) => save.Factory.Production.Contract!=null && save.Factory.Production.Contract.Crafted>=save.Factory.Production.Contract.Required;
        public static bool TryDeliverCommission(GameSave save,IDataRegistry data)
        {
            Initialize(save,data);
            if(!TrialReady(save)) return false;
            var p=save.Factory.Production; var gun=NextGun(save,data);
            long wages=(long)Math.Min((decimal)long.MaxValue-save.Player.Money,(decimal)p.Contract.Crafted*gun.Wage);
            save.Player.Money+=wages;
            p.TotalWages=(long)Math.Min(long.MaxValue,(decimal)p.TotalWages+wages);
            p.UnlockedCount++; p.Contract=null;
            return true;
        }
        public static bool TrySelect(GameSave save,IDataRegistry data,string id)
        {
            int i=Array.IndexOf(Order,id); var p=save.Factory.Production;
            if(i<0 || (i>=p.UnlockedCount && p.Contract?.WeaponId!=id) || p.SelectedWeaponId==id) return false;
            p.SelectedWeaponId=id; p.WorkDone=0; return true;
        }
        private static ScavState Find(GameSave save,string id) => save.Scavs.Find(s=>s.Uid==id);
        public static int CrewCapacity(GameSave save) => Math.Min(3,Math.Max(1+save.Factory.Production.ExtraBenches,save.Factory.Production.AssignedScavUids.Count));
        public static int EquipmentLevel(GameSave save,ProductionEquipment kind)
        {
            var p=save.Factory.Production;
            switch(kind) {
                case ProductionEquipment.AssemblyJig: return p.AssemblyJigLevel;
                case ProductionEquipment.PowerTools: return p.PowerToolsLevel;
                case ProductionEquipment.ExtraBench: return p.ExtraBenches;
                default: return 0;
            }
        }
        private static int EquipmentMax(IDataRegistry data,ProductionEquipment kind)
        {
            var t=Tuning(data);
            switch(kind) {
                case ProductionEquipment.AssemblyJig: return Math.Max(0,Math.Min(5,t.MaxJigLevel));
                case ProductionEquipment.PowerTools: return Math.Max(0,Math.Min(5,t.MaxToolsLevel));
                case ProductionEquipment.ExtraBench: return Math.Max(0,Math.Min(2,t.MaxExtraBenches));
                default: return 0;
            }
        }
        public static long EquipmentCost(GameSave save,IDataRegistry data,ProductionEquipment kind)
        {
            int level=EquipmentLevel(save,kind);
            if(level>=EquipmentMax(data,kind)) return 0;
            var t=Tuning(data);
            switch(kind) {
                case ProductionEquipment.AssemblyJig: return Cost(Math.Max(1,t.FirstJigCost)*Math.Pow(Safe(t.JigCostGrowth,2.1),level));
                case ProductionEquipment.PowerTools: return Cost(Math.Max(1,t.FirstToolsCost)*Math.Pow(Safe(t.ToolsCostGrowth,2.2),level));
                case ProductionEquipment.ExtraBench: return Cost(Math.Max(1,t.FirstBenchCost)*Math.Pow(Safe(t.BenchCostGrowth,2.3),level));
                default: return 0;
            }
        }
        public static bool TryUpgradeEquipment(GameSave save,IDataRegistry data,ProductionEquipment kind)
        {
            long cost=EquipmentCost(save,data,kind);
            if(cost<=0 || save.Player.Money<cost) return false;
            var p=save.Factory.Production;
            switch(kind) {
                case ProductionEquipment.AssemblyJig: p.AssemblyJigLevel++; break;
                case ProductionEquipment.PowerTools: p.PowerToolsLevel++; break;
                case ProductionEquipment.ExtraBench: p.ExtraBenches++; break;
                default: return false;
            }
            save.Player.Money-=cost; return true;
        }
        public static bool CanAssign(GameSave save,string uid) => AssignBlockReason(save,uid)==null;
        public static string AssignBlockReason(GameSave save,string uid)
        {
            var scav=Find(save,uid);
            if(scav==null) return Loc.Text("명단에 없는 스캐브입니다.");
            if(scav.Status!=ScavStatus.Idle || save.Factory.Production.AssignedScavUids.Contains(uid)) return Loc.Text("대기 중인 스캐브만 배치할 수 있습니다.");
            if(save.Factory.Production.AssignedScavUids.Count>=CrewCapacity(save)) return Loc.Text("작업대가 부족합니다. 추가 작업대를 제작하세요.");
            return null;
        }
        public static bool TryAssign(GameSave save,IDataRegistry data,string uid,DateTimeOffset now)
        {
            if(!CanAssign(save,uid)) return false;
            Find(save,uid).Status=ScavStatus.Working; save.Factory.Production.AssignedScavUids.Add(uid);
            AdvanceBoundary(save,now); return true;
        }
        public static bool TryUnassign(GameSave save,IDataRegistry data,string uid,DateTimeOffset now)
        {
            if(!save.Factory.Production.AssignedScavUids.Remove(uid)) return false;
            var scav=Find(save,uid); if(scav!=null && scav.Status==ScavStatus.Working) scav.Status=ScavStatus.Idle;
            AdvanceBoundary(save,now); return true;
        }
        private static void AdvanceBoundary(GameSave save,DateTimeOffset now)
        {
            var p=save.Factory.Production;
            if(save.SavedAt>now) now=save.SavedAt;
            if(now>p.LastWorkedAt) p.LastWorkedAt=now;
        }
    }

    public sealed class ProductionSystem : ITimelineSystem
    {
        public string Name => "Production";
        public IEnumerable<TimedEvent> CollectEvents(ResolveWindow window,ResolveContext ctx)
        {
            var p=ctx.Save.Factory.Production;
            if(p==null || window.To<=p.LastWorkedAt) yield break;
            var from=window.From;
            var cap=Support.OfflineCap(ctx.Save,ctx.Data,window.To);
            if(window.To-from>cap) from=window.To-cap;
            if(p.LastWorkedAt>from) from=p.LastWorkedAt;
            if(from>=window.To) yield break;
            // Snapshot before returns/treatments mutate roster status. Returning workers cannot
            // retroactively contribute to an interval during which they were on expedition.
            double rate=ProductionWork.AutoWorkPerSecond(ctx.Save,ctx.Data);
            if(ProductionWork.TrialReady(ctx.Save) && p.SelectedWeaponId==p.Contract.WeaponId) rate=0;
            double work=(window.To-from).TotalSeconds*rate;
            yield return new TimedEvent(window.To,EventOrder.FactoryOutput,"production",context=> {
                var state=context.Save.Factory.Production;
                if(state.LastWorkedAt>=window.To) return;
                state.LastWorkedAt=window.To;
                state.AssignedScavUids.RemoveAll(id=>context.Save.Scavs.Find(s=>s.Uid==id)?.Status!=ScavStatus.Working);
                if(work<=0) return;
                bool trial=state.Contract!=null && state.Contract.WeaponId==state.SelectedWeaponId;
                bool wasReady=ProductionWork.TrialReady(context.Save);
                var result=ProductionWork.AddWork(context.Save,context.Data,work);
                context.Report.ProductionProgressed=true;
                if(trial) {
                    context.Report.ProductionSamples+=result.CompletedCount;
                    context.Report.ProductionCommissionReady |= !wasReady && ProductionWork.TrialReady(context.Save);
                }
                else context.Report.ProductionCompleted+=result.CompletedCount;
                context.Report.ProductionWages+=result.Wages;
            });
        }
    }
}




