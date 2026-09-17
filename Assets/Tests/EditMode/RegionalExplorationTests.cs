using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class RegionalExplorationTests
    {
        [Test] public void LockedRegionCannotAcceptAndAcceptanceSurvivesSerialization()
        {
            var save = new GameSave();
            Assert.IsFalse(RegionalExplorationQuest.Accept(save, "GURO_FACTORY"));
            save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
            Assert.IsTrue(RegionalExplorationQuest.Accept(save, "GURO_FACTORY"));
            var codec = new NewtonsoftJsonCodec();
            var copy = codec.Deserialize<GameSave>(codec.Serialize(save));
            Assert.IsFalse(RegionalExplorationQuest.NeedsIntroduction(copy, "GURO_FACTORY"));
            Assert.IsFalse(RegionalExplorationQuest.Accept(copy, "GURO_FACTORY"));
        }
        [TestCase(ExplorationOutcome.Death, 3, true, false)]
        [TestCase(ExplorationOutcome.Emergency, 3, true, false)]
        [TestCase(ExplorationOutcome.Success, 1, true, false)]
        [TestCase(ExplorationOutcome.Success, 3, false, false)]
        [TestCase(ExplorationOutcome.Success, 3, true, true)]
        public void OnlySurveyLootAndSurvivalCompleteQuest(ExplorationOutcome outcome, int node, bool loot, bool ready)
        {
            var save = new GameSave(); save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
            RegionalExplorationQuest.Accept(save, "GURO_FACTORY");
            save.Exploration = new ExplorationState { MapId = "GURO_FACTORY", NodeIndex = node, Result = new ExplorationResult { Outcome = outcome } };
            if (loot) save.Exploration.Loot.Add(new ItemStack("JUNK01", 1));
            RegionalExplorationQuest.OnSuccessfulReturn(save);
            Assert.AreEqual(ready, RegionalExplorationQuest.Progress(save, "GURO_FACTORY").ReadyToReport);
            Assert.IsFalse(RegionalExplorationQuest.Report(save, "GURO_FACTORY"));
            save.Exploration.Result.Acknowledged = true;
            Assert.AreEqual(ready, RegionalExplorationQuest.Report(save, "GURO_FACTORY"));
            Assert.IsFalse(RegionalExplorationQuest.Report(save, "GURO_FACTORY"));
            Assert.AreEqual(ready ? 15000 : 0, save.Player.Money);
        }
        [Test] public void EveryRegionHasPortraitAndDistinctDialogue()
        {
            foreach (var map in RegionalExplorationQuest.Maps) Assert.IsNotNull(GameArt.Npc(RegionalExplorationQuest.Npc(map)), map);
            Assert.AreEqual(7, RegionalExplorationQuest.Maps.Select(RegionalExplorationQuest.Offer).Distinct().Count());
            Assert.AreEqual("DOKKAEBI", RegionalExplorationQuest.Npc("HAN_RIVER"));
        }
        [Test] public void AudioTransitionsAreSilentOnRedrawAndUseActualRounds()
        {
            var before = new ExplorationAudioSnapshot { RunId = "a", Phase = ExplorationPhase.Combat, Ammo = 20, EnemyHp = 100, Hp = 100, Weapon = "WPN01" };
            Assert.IsEmpty(ExplorationAudioCues.Between(before, before));
            var after = new ExplorationAudioSnapshot { RunId = "a", Phase = ExplorationPhase.EncounterResult, Ammo = 15, Shots = 5, EnemyHp = 0, Hp = 100 };
            var cues = ExplorationAudioCues.Between(before, after);
            Assert.AreEqual(5, cues.Count(c => c == "rifle_shot"));
            Assert.AreEqual(1, cues.Count(c => c == "body_fall"));
            Assert.IsFalse(cues.Contains("reload_start"));
        }
        [Test] public void FootstepsAndEnemyFireUseBundledOriginalAudio()
        {
            var before = new ExplorationAudioSnapshot { RunId = "a", Phase = ExplorationPhase.Combat, EnemyHp = 100, Hp = 100, EnemyAction = EnemyAction.Aiming };
            var after = new ExplorationAudioSnapshot { RunId = "a", Phase = ExplorationPhase.Combat, EnemyHp = 100, Hp = 73, EnemyAction = EnemyAction.Firing, EnemyWeapon = "WPN01" };
            CollectionAssert.AreEqual(new[] { "rifle_shot", "player_hurt" }, ExplorationAudioCues.Between(before, after));
            after.Node = 1;
            CollectionAssert.AreEqual(new[] { "footstep_01", "footstep_02" }, ExplorationAudioCues.Between(before, after));
            foreach (var cue in new[] { "footstep_01", "footstep_02", "pistol_shot", "rifle_shot", "shotgun_shot", "reload_start", "reload_complete", "melee_swing", "armor_hit", "body_fall", "player_hurt", "fabric_drag", "loot_pickup" })
                Assert.IsNotNull(Resources.Load<AudioClip>("Audio/Exploration/" + cue), cue);
        }
    }
}
