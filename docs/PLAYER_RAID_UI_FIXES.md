# Player equipment and raid UI fixes — 2026-09-17

- Warehouse has a persistent My Equipment entry, all seven player slots, current item statistics, and equip/unequip actions. Item details include actual mobile combat damage, accuracy, magazine/caliber, melee interval, armor, capacity/hearing and consumable recovery/time. Equipment uses ExecuteSavedAction and cannot change during an active raid.
- Raid bag consolidates brought supplies and found loot into one row per item with origin counts. Consumables show effects and explicit drink/eat/use actions. Found Tarcola is usable. Bag is accessible before extraction too.
- Regional quest acceptance and map entry actions sit outside scrolling dialogue. Accepting an introduction quest opens the corresponding map entry detail directly.
- Eight regions each have two distinct backgrounds selected by movement node. Raid BGM selects a mainline opening/briefing track once per raid and restores normal BGM on exit.
- Reset replays logo/title/prologue and rewinds BGM. Retired canvases detach before deferred destruction. Initial prologue background clicks no longer skip it. Focus/background/reboot regression tests preserve tutorial page and unchosen NPC; no actual auto-assignment path was found.

Validation: 558/558 Unity EditMode tests passed (Logs/player-raid-final.xml). Nine actual 1080x1920 UI captures inspected in Logs/player-raid-preview. Five-language additions checked for missing keys and placeholder consistency. Player saves were not reset or used in captures. Physical phone testing remains outstanding.
