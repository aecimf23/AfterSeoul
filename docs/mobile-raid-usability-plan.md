# Mobile raid usability implementation plan

Goal: Make equipment effects, departure preparation, combat outcomes and loot decisions understandable on a portrait phone.

Approved scope: user's 2026-09-18 request to implement all five proposed improvements in order. Preserve map order, safe first route choice, NPC story, level-only character progression, and atomic saved actions.

1. Equipment comparisons: use CombatProfiles damage/caliber/modes, armor mitigation formula, hearing and actual loot capacity. Show current-to-candidate changes in shared slot picker and warehouse detail. Test weapon swap and melee/armor descriptions.
2. Preparation: extend map popup with seven compact slot buttons, health/water/energy, ammo/medical/food/water summary, one-touch owned supply recommendation. Keep selected map when returning from equipment or supplies. Keep departure button fixed outside scroll. Warn without blocking intentional low-supply runs; display actual blocking reason. Test swap, compatible ammo, safe entry and failed save.
3. Combat: persist short player/enemy feedback separately from enemy tells; report damage, misses, cover reduction and automatic reload. Display last exchange in victory receipt and current quest objective. Test hit/miss/cover and pause.
4. Loot decisions: base capacity eight distinct loot item types, backpack adds GridSlots/4 and rig GridSlots/8 (integer). Packed supplies separate. Store excess loot as pending until take/discard/leave decision, never silently lose it or duplicate rewards. Confirm whole-stack discard. Exits remain at existing route nodes. Test full bag, replacement, save/reload, same-item merge and blocked continuation.
5. Verification: full EditMode suite, portrait actual uGUI renders, Android build, source review, commit and push. Physical phone verification is not available from this workspace. Search name-input code again and report unresolved source mismatch if absent.

Verification completed 2026-09-18:
- 576/576 Unity EditMode tests passed (Logs/raid-verified-tests.xml), including multi-seed intermediate/final extraction, onboarding, failed-save rollback, consumable recovery, alternative supplies, pending loot reload and repeat actions.
- Actual uGUI renders inspected at 1080x1920 and 1080x2400 (Logs/raid-preview); departure footer remains visible, equipment comparisons and combat feedback do not overlap controls.
- Android build succeeded, 0 errors, 1 existing Unity Services project-link warning (Logs/raid-android.log). APK Builds/Android/AfterSeoul-alpha.apk.
- Independent read-only code review found no actionable important defects.
- Physical-device touch verification was not performed. The previously reported character-name prompt remains absent from this source tree; only its generic modal input propagation is covered by existing tests. Do not claim that separate prompt is verified.
- Existing runs retain their stored loot, including legacy bags above capacity; additional distinct types require space. Identical item types stack together. Supplies are separate from loot capacity.
