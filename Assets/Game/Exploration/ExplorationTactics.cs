using System;
using AfterSeoul.Core;

namespace AfterSeoul.Exploration
{
    public static partial class ExplorationSystem
    {
        public static ExplorationWeather EffectiveWeather(ExplorationState e) => e.Indoors ? ExplorationWeather.Clear : e.Weather;
        public static string ArchetypeName(string kind)
        {
            switch(kind) { case "Rusher": return "돌격병"; case "Sniper": return "저격수"; case "Grenadier": return "척탄병"; default: return "소총수"; }
        }
        static void GenerateRoutes(ExplorationState e)
        {
            var sites=RaidRegions.Sites(e.MapId);
            int split=0; while(split<sites.Length && !sites[split].Dangerous) split++;
            var safe=sites[Roll(e,split)]; var risky=sites[split+Roll(e,sites.Length-split)];
            e.Routes=new[]{safe.Name,risky.Name}; e.RouteContainers=new[]{safe.Container,risky.Container};
            e.RouteDangerous=new[]{false,true}; e.RouteIndoors=new[]{safe.Indoors,risky.Indoors};
        }
        static void BeginCombat(ExplorationState e)
        {
            e.ConversationOpen=false; e.Phase=ExplorationPhase.Combat;
            e.Enemy.Action=e.Initiative ? EnemyAction.Alert : EnemyAction.Aiming;
            e.Enemy.Remaining=e.Initiative ? 2.6 : 2.0;
            e.PlayerFeedback=e.EncounterNote ?? (e.Initiative ? "선공 기회 · 상대가 대응하기 전에 공격하세요" : "발각되었습니다 · 조준을 보고 엄폐하세요");
        }
        static bool SocialChoice(GameSave s,IDataRegistry d,EncounterChoice choice)
        {
            var e=s.Exploration;
            if(e.Enemy?.Kind!="Scav") return false;
            if(choice==EncounterChoice.Talk) { if(e.ConversationOpen) return false; e.ConversationOpen=true; return true; }
            if(!e.ConversationOpen || (choice!=EncounterChoice.RequestAid && choice!=EncounterChoice.Trade)) return false;
            if(e.ScavAttitude=="Hostile") {
                e.EncounterNote="대화를 거절하고 총을 들어 올립니다. 엄폐를 준비하세요!"; e.Initiative=false; BeginCombat(e); return true;
            }
            if(choice==EncounterChoice.Trade) {
                string food=null;
                foreach(var x in e.Supplies) if(CombatProfiles.ConsumableFor(x.ItemId)?.Energy>=30 && x.Count>0) { food=x.ItemId; break; }
                if(food==null) foreach(var x in e.Loot) if(CombatProfiles.ConsumableFor(x.ItemId)?.Energy>=30 && x.Count>0) { food=x.ItemId; break; }
                if(food==null) return false;
                if(!Remove(e.Supplies,food,1)) Remove(e.Loot,food,1);
                e.EncounterNote="식량을 건네고 지역에서 모은 자재를 받았습니다. 서로 무사히 돌아가자며 헤어집니다.";
            } else if(e.ScavAttitude=="Wary") {
                e.EncounterNote="나도 넉넉하지 않아. 대신 앞쪽 길은 조심해. 더 캐묻지 않고 서로 길을 비켜 줍니다.";
                e.Enemy=null; e.ConversationOpen=false; PrepareLootChoice(e,d); return true;
            } else e.EncounterNote="혼자 버티기 힘들지? 남는 물건이야. 살아서 돌아가. 스캐브가 물자를 건넸습니다.";
            string id=choice==EncounterChoice.Trade ? RaidRegions.Materials(e.MapId)[0] : (Roll(e,2)==0 ? "MED05" : "FOOD01");
            if(d.GetItem(id)==null) return false;
            GrantLoot(e,id,1); Add(e.EncounterLoot,id,1);
            e.Enemy=null; e.EncounterRewarded=true; e.ConversationOpen=false; e.Phase=ExplorationPhase.EncounterResult;
            return true;
        }
        public static bool Dodge(GameSave s)
        {
            var e=s.Exploration;
            if(!IsActive(s)||e.Paused||e.Phase!=ExplorationPhase.Combat||e.UseRemaining>0||e.DodgeCooldown>0) return false;
            e.DodgeCooldown=3;
            e.DodgedThreat=e.Enemy?.Action==EnemyAction.Grenade || e.Enemy?.Action==EnemyAction.Rush;
            e.PlayerFeedback=e.DodgedThreat ? "자리 이동! 예고된 공격 범위를 벗어났습니다" : "자리를 옮겼습니다 · 수류탄·돌진 예고에 맞춰 사용하세요";
            return true;
        }
        static void PickEnemyPattern(ExplorationState e)
        {
            e.Enemy.PatternStep++;
            int roll=Roll(e,100);
            bool grenade=e.Enemy.GrenadesThrown<2 && e.Enemy.PatternStep>=2 && (e.Enemy.Archetype=="Grenadier" || roll<18);
            if(grenade) { e.Enemy.Action=EnemyAction.Grenade; e.Enemy.Remaining=2.4; e.DodgedThreat=false; e.Enemy.GrenadesThrown++; }
            else if(e.Enemy.Archetype=="Rusher" && roll<65) { e.Enemy.Action=EnemyAction.Rush; e.Enemy.Remaining=1.8; e.DodgedThreat=false; }
            else { e.Enemy.Action=EnemyAction.Aiming; e.Enemy.Remaining=e.Enemy.Archetype=="Sniper" ? 2.5 : e.Enemy.Archetype=="Rusher" ? 1.35 : 1.9; }
        }
        static void ThreatDamage(GameSave s,IDataRegistry d,bool grenade)
        {
            var e=s.Exploration;
            if(e.DodgedThreat) { e.EnemyFeedback=grenade ? "수류탄 회피 성공 · 피해 없음" : "돌진 회피 성공 · 피해 없음"; return; }
            double damage=(grenade ? 32 : 21)*(1-RaidEquipment.ArmorReduction(s,d)*.5);
            s.Player.Hp=Math.Max(0,s.Player.Hp-damage);
            e.EnemyFeedback=Loc.Text(grenade ? "수류탄 폭발! HP −{0:0} · 엄폐로 막을 수 없습니다" : "돌진 공격! HP −{0:0} · 자리 이동으로 피하세요",damage);
            if(s.Player.Hp<=0) Finish(s,d,ExplorationOutcome.Death,grenade ? "수류탄 폭발" : "돌진 공격",e.Enemy);
        }
    }
}
