# Combat feedback and regional followups — 2026-09-17

- Departure, successful extraction, base return, and rescue return use bundled mainline sound effects. Damage plays impact and one of three injury voices, with a voice cooldown and existing effect-volume controls.
- Hits briefly tint the screen red. HP <= 30 uses red HP text and an explicit heal/extract warning; recovery clears it. Reduced-motion mode attenuates the flash.
- Guro (Choi), Han River (Dokkaebi), and Namsan (Wildman) each offer two optional followups after their initial survey report. The NPC dialogue adapts the mainline trade-route, missing-cargo, and survivor-route themes to supported mobile exploration mechanics.
- First followup: a newly accepted raid, at least 3 movements, at least 1 recovered item, successful extraction. Second: 4 movements and 2 item units. Recovered goods remain the player's property. Rewards: 20,000 / 30,000 won and +1 NPC trust per report.
- Optional quests do not block map entry. Acceptance and reports have fixed visible actions; map details show active objectives and the raid result directs completed objectives back to the NPC.
- Existing initial quest completion is retained in older saves. No player save was reset during validation.

Validation: Unity EditMode 573/573 passed (`Logs/combat-quest-all.xml`). Five 1080x1920 actual uGUI captures in `Logs/combat-feedback-preview` cover offer, accepted map, low HP, hit flash, and healed state. All five locale files parse with the new 23 keys. Physical-device sound balance still needs listening during playtesting.
