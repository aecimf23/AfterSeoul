# Tab clarity redesign

User goal: each bottom tab must show its own relevant content, make its purpose obvious, and provide a clear next action without mixing unrelated systems. Preserve gameplay, saves, tutorial replay, item actions, quests and unlocked-map rules.

1. Add tab ownership tests. Home retains tracked objective/reward/action, quest journal and base facilities; secondary status/link/support is collapsed. Exploration defaults to direct exploration with a distinct dispatch section. Personnel separates owned roster from recruitment. Warehouse defaults to inventory with an explicit equipment section. Factory keeps production and compact upgrades while detailed tools/contracts/assignment remain in management.
2. Implement ownership changes in Screens/HomeScreen, ExpeditionScreen, PersonnelScreen, WarehouseScreen, FactoryScreen and ProductionFactoryView. Use clear page titles, section buttons and next-action copy. Preserve modal return paths and existing object IDs where their actions remain available.
3. Update navigation tests for deliberate section choices, test direct/dispatch switching and equipment/warehouse switching, and confirm tab reentry defaults without resetting normal refreshes.
4. Render the five landing screens and secondary sections at portrait phone sizes, inspect readability and access to primary actions, run EditMode suite and Android build, review, commit and push.

## Implemented and verified — 2026-09-29

- All five screens use compact shared headers with an explicit purpose in the title. Home foregrounds one tracked objective, reward and action, with quest journal/base facilities below. Status, reports and legacy links remain under optional details.
- Exploration defaults to direct departure with vitals and a single preparation action. Dispatch has a distinct section with team controls and the existing unlocked-region map. Explicit dispatch and recruiting links use dedicated entry points instead of misleading default-tab routes.
- Personnel separates roster management from recruitment, with an empty-state recruitment action. Warehouse separates stored inventory from the body-slot equipment view, preserving item detail, sales, equip and comparison actions.
- Factory retains immediate upgrades in compact cards; full explanations and assistance remain in management/help.
- Portrait renders exposed an existing rapid-tab animation defect: interrupted slide offsets accumulated by 48px. A failing regression test reproduced it; canceling the positional tween and resetting each stretched screen root before entry fixed clipping.
- Final EditMode suite: 719 passed, zero failures (`Logs/tab-final-tests.xml`). Eight final Unity screen renders: `Logs/tab-clarity-preview`. Independent review verified the corrected navigation and animation handling.
- Physical phone touch testing remains unavailable.
- Android build succeeded with zero errors and three warnings (`Logs/tab-android.log`, `Builds/Android/build-result.txt`); refreshed APK: `Builds/Android/AfterSeoul-alpha.apk` (228,006,703 bytes).
