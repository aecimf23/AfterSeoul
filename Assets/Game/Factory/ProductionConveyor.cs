using System;
using System.Collections.Generic;
using AfterSeoul.Core;
namespace AfterSeoul.Factory {
    public enum ProductionStrikeKind { Hit, Miss, Cooldown, Blocked }
    public readonly struct ProductionStrikeResult {
        public readonly ProductionStrikeKind Kind;
        public readonly int Tier;
        public readonly double WorkAdded;
        public readonly ProductionResult Production;
        public ProductionStrikeResult(ProductionStrikeKind kind,int tier=0,double work=0,ProductionResult production=default) { Kind=kind;Tier=tier;WorkAdded=work;Production=production; }
    }
    public sealed class ConveyorPart {
        public long Id { get; }
        public int Tier { get; }
        public double Position { get; internal set; }
        public double Work { get; }
        internal ConveyorPart(long id,int tier,double position,double work) {Id=id;Tier=tier;Position=position;Work=work;}
    }
    /// <summary>Runtime only: explicit active-screen time, never wall-clock or saved production.</summary>
    public sealed class ProductionConveyor {
        readonly List<ConveyorPart> parts=new List<ConveyorPart>();
        readonly IReadOnlyList<ConveyorPart> view;
        long sequence;
        double untilFeed;
        double recoveryDuration;
        bool initialized;
        public IReadOnlyList<ConveyorPart> Parts => view;
        public double HitStart => .46;
        public double HitEnd => .60;
        public double CooldownRemaining { get; private set; }
        public ProductionConveyor() { view=parts.AsReadOnly(); }
        internal void Initialize(GameSave save,IDataRegistry data) {
            if(initialized)return;
            initialized=true;Spawn(save,data,.1);untilFeed=ProductionWork.FeedInterval(save,data);
        }
        internal void Clear(GameSave save,IDataRegistry data) { initialized=true;parts.Clear();untilFeed=ProductionWork.FeedInterval(save,data); }
        void Spawn(GameSave save,IDataRegistry data,double position) {
            if(parts.Count>=8)return;
            int tier=ProductionWork.MaterialTierForSequence(save,++sequence);
            parts.Add(new ConveyorPart(sequence,tier,position,ProductionWork.MaterialWork(data,tier)));
        }
        internal void Advance(GameSave save,IDataRegistry data,double delta) {
            Initialize(save,data);
            if(double.IsNaN(delta)||double.IsInfinity(delta)||delta<=0)return;
            delta=Math.Min(.25,delta);
            CooldownRemaining=Math.Max(0,CooldownRemaining-delta);
            foreach(var part in parts)part.Position+=delta/3;
            parts.RemoveAll(p=>p.Position>1);
            untilFeed-=delta;
            if(untilFeed<=0) { Spawn(save,data,Math.Max(0,-untilFeed/3));untilFeed+=ProductionWork.FeedInterval(save,data); }
        }
        internal ConveyorPart Strike(out ProductionStrikeKind kind) {
            if(CooldownRemaining>1e-9) {CooldownRemaining=recoveryDuration;kind=ProductionStrikeKind.Cooldown;return null;}
            ConveyorPart chosen=null; double center=(HitStart+HitEnd)/2;
            foreach(var p in parts) if(p.Position>=HitStart && p.Position<=HitEnd && (chosen==null || Math.Abs(p.Position-center)<Math.Abs(chosen.Position-center)))chosen=p;
            if(chosen!=null) {parts.Remove(chosen);CooldownRemaining=recoveryDuration=.18;kind=ProductionStrikeKind.Hit;return chosen;}
            // Early attempts consume the next arriving part. Repeated taps cannot cover a window.
            foreach(var p in parts)if(p.Position<HitStart && (chosen==null || p.Position>chosen.Position))chosen=p;
            if(chosen!=null)parts.Remove(chosen);
            CooldownRemaining=recoveryDuration=.75;kind=ProductionStrikeKind.Miss;return null;
        }
    }
}

