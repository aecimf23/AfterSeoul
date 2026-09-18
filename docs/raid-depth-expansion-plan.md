# Raid depth expansion — approved implementation scope

User asked to implement all four gameplay improvements sequentially, with enemy patterns/grenades, weather and indoor/outdoor differences, scav dialogue, distant-contact avoidance, regional loot, material sinks and recovery economy.

1. Contact and combat: all newly generated human contacts enter a safe decision phase; no damage while deciding. Unnoticed contacts can be bypassed, noticed contacts allow a clearly labeled risky retreat. Rain outside conceals footsteps and grants the player initiative; buildings remove weather penalties. Scavs have a persisted attitude and region-specific dialogue with aid, exchange, departure or combat. Mixed rifleman/rusher/sniper/grenadier behavior with readable windups; dodge counters grenade/rush, cover counters gunfire. Save RNG, all telegraphs and one-time rewards.
2. Regions: distinct indoor/outdoor safe and dangerous routes and preferred container/material drops for all eight currently supported regions. Dangerous route advertised before selection, more human contact and extra loot choice, preserving one-choice reward. Existing map unlock order and 6–10 nodes/exits unchanged.
3. Material goals: base workshop UI shows only the next tier for medical station, supply storage and workbench (two tiers each), actual effects, required/owned materials, and regional gear barter. No level stat/unlocks. Atomic trial inventory for costs and rewards. Return useful materials instead of selling everything.
4. Economy: early combat less punishing, field consumables and appropriate ammunition affordable through modest resupply transactions. Broke players may request loan supplies for a Yongsan run; loan weapon/supplies never enter the warehouse or sell economy, and unused loan items are returned at any outcome. Facilities consume scavenged materials and money. Verify net rewards, money/item nonduplication, and multi-seed progression.

Verification: red/green domain and UI tests, seeded simulations, full EditMode suite, actual portrait UI captures, independent review, Android build, commit/push. Physical touch testing remains unavailable. Source project D:/singleProject/AfterSeoul; do not change original reference game.

## Verification results — 2026-09-18

- Final EditMode suite: 598 passed, zero failures (`Logs/depth-final-tests.xml`). Game, UI and editor source compilation passed.
- A conservative automated policy over 100 Yongsan seeds survived 100 runs, entered combat in 30 runs, and produced median net recovered value 59,640 (mean 66,074.97; p10 34,020; p90 94,740), after consumed supplies and replacement markup. This is a scripted balance check, not a prediction of human survival rates.
- Seven actual uGUI captures at portrait 1080x1920 and 1080x2400 cover preparation, scav dialogue, grenade response, facilities, barter, recovery and regional routes. Relevant contact, dodge and departure buttons passed viewport bounds assertions; captures were visually inspected.
- Independent review found two issues, reproduced with failing tests and fixed: Yongsan entrance container metadata now matches the safe entrance labels; dangerous combat awards exactly one selected reward from three options, without an extra automatic reward. Follow-up review found no further actionable defects.
- Physical Android touch, performance and perceived combat difficulty still require device playtesting.
- Final Android development build succeeded with zero errors and one warning; Unity exited with code 0. APK: `Builds/Android/AfterSeoul-alpha.apk`, 191,875,212 bytes, built at 14:07 KST.
