using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Exploration
{
    // Pure transition snapshots: redraws, failed commands and reopened screens stay silent.
    public sealed class ExplorationAudioSnapshot
    {
        public string RunId, Weapon, EnemyWeapon;
        public int Node, Ammo, Shots;
        public double Hp, EnemyHp, Cover, Use, AttackCooldown, Dodge;
        public ExplorationPhase Phase;
        public ExplorationOutcome Outcome;
        public EnemyAction EnemyAction;
        public static ExplorationAudioSnapshot Capture(GameSave save)
        {
            var run = save.Exploration;
            if (run == null || run.Result?.Acknowledged == true) return null;
            return new ExplorationAudioSnapshot { RunId = run.Uid, Node = run.NodeIndex, Ammo = ExplorationSystem.AmmoRemaining(save),
                Shots = run.ShotsSinceReload, Hp = save.Player.Hp, EnemyHp = run.Enemy?.Hp ?? 0,
                Cover = run.CoverRemaining, Use = run.UseRemaining, AttackCooldown = run.AttackCooldown, Dodge=run.DodgeCooldown, Phase = run.Phase,
                Outcome = run.Result?.Outcome ?? ExplorationOutcome.Success, EnemyAction = run.Enemy?.Action ?? EnemyAction.Alert, Weapon = PlayerEquipment.Equipped(save, "Weapon"), EnemyWeapon = run.Enemy?.WeaponId };
        }
    }
    public static class ExplorationAudioCues
    {
        public static string Gun(string weapon)
        {
            var caliber = CombatProfiles.For(weapon)?.Caliber;
            if (caliber == "12/70" || caliber == "23x75") return "shotgun_shot";
            return weapon == "WPN04" || weapon == "WPN21" ? "pistol_shot" : "rifle_shot";
        }
        public static List<string> Between(ExplorationAudioSnapshot before, ExplorationAudioSnapshot after)
        {
            var cues = new List<string>();
            if (after == null) return cues;
            if(after.Phase==ExplorationPhase.Result) {
                if(before!=null && before.RunId==after.RunId && before.Phase!=ExplorationPhase.Result)
                    { if(after.Hp<before.Hp) cues.Add("player_hurt"); cues.Add(after.Outcome==ExplorationOutcome.Success ? "extract_success" : "body_fall"); }
                return cues;
            }
            if(before==null || before.RunId!=after.RunId) cues.Add("raid_depart");
            if (before == null || before.RunId != after.RunId || after.Node > before.Node) {
                cues.Add("footstep_01"); cues.Add("footstep_02"); return cues;
            }
            if (before.Phase != ExplorationPhase.Combat) return cues;
            int rounds = before.Ammo - after.Ammo;
            for (int i = 0; i < System.Math.Min(5, rounds); i++) cues.Add(Gun(before.Weapon));
            if (rounds > 0 && after.Shots == 0) cues.Add("reload_start");
            if (rounds == 0 && after.AttackCooldown > before.AttackCooldown && after.EnemyHp < before.EnemyHp) cues.Add("melee_swing");
            if (after.Cover > before.Cover) cues.Add("fabric_drag");
            if(after.Dodge>before.Dodge) cues.Add("footstep_02");
            if (after.Use > before.Use) cues.Add("fabric_drag");
            if (before.Use > 0 && after.Use == 0) cues.Add("loot_pickup");
            if (after.EnemyHp < before.EnemyHp) cues.Add(after.EnemyHp <= 0 ? "body_fall" : "armor_hit");
            if (after.EnemyAction != before.EnemyAction && after.EnemyHp > 0) {
                if (after.EnemyAction == EnemyAction.Firing) cues.Add(Gun(after.EnemyWeapon));
                if (after.EnemyAction == EnemyAction.Reloading) cues.Add("reload_start");
                if (after.EnemyAction == EnemyAction.Cover) cues.Add("fabric_drag");
                if(after.EnemyAction==EnemyAction.Grenade) cues.Add("grenade_pin");
                if(after.EnemyAction==EnemyAction.Explosion) cues.Add("grenade_blast");
                if(after.EnemyAction==EnemyAction.Rush) cues.Add("footstep_01");
            }
            if (before.Shots == 0 && before.AttackCooldown > 0 && after.AttackCooldown == 0) cues.Add("reload_complete");
            if (after.Hp < before.Hp) cues.Add("player_hurt");
            return cues;
        }
    }
}
