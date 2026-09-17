# Map selection and ammunition correction — 2026-09-17

Ammo BasePrice comes from the mainline per-stack Price. Mobile inventory uses individual rounds, so ItemPricing.UnitValue divides ammunition by MaxStack. Market, shop and expedition recovered-value calculations now use this unit. PC transfer BasePrice validation remains unchanged.

Both direct exploration and scav dispatch use the survival route: Yongsan Market → Guro → Han River → Namsan → Gangnam → Yongsan Base; Myeongdong and Uijeongbu follow the completed main route. Old level/trust map gates were replaced for the real registry. Quest availability still filters sources by actually opened regions. Existing saves retain recorded successful raid results.

SeoulMapSelection uses original maps.json MapX/MapY positions on the generated 2D background. Overview screens contain no map-card scroll lists and no locked markers. A region tap opens its detail modal. Direct preparation equipment/supplies and scav team/operations have separate detail panels.

New raids remain in Routes with AwaitingEntryChoice and NodeIndex -1. Enemy is absent, combat time cannot advance, extraction is unavailable. Choosing an entrance moves to node 0 and starts the first of 6–10 encounters. First-quest container hints agree with the guaranteed first container.

Verification: 496 EditMode tests, 484 passed, the same 12 baseline failures as regional-exploration-tests.xml. New tests cover per-round sale payment, 100 safe entry seeds including save reload, route unlock parity, and map/details UI. Actual 1080×1920 captures: Logs/map-navigation-preview. Existing tests requiring all maps now explicitly record route completion instead of relying on level 99.

Generated background: Assets/Resources/ToonArt/seoul-map.png. Original output exec-1b234a1f-6f20-4eaf-b361-ff48ee858c22.png retained in Codex generated_images. No third-party game artwork copied.

Final focused verification: 48/48 passed (map/ammo, exploration core, exploration UI). Android build succeeded with 0 errors; existing Unity Services project-link warning remains. APK: Builds/Android/AfterSeoul-alpha.apk.
