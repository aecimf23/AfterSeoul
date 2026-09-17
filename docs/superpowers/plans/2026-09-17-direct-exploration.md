# Direct Exploration Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Play a complete mobile exploration run from equipment through combat to extraction and NPC return.
**Architecture:** Pure C# exploration state/rules, separate equipment helpers, Unity full-screen exploration controller. Existing production and dispatch remain compatible.
**Tech Stack:** Unity 6000.3, C#, code-built uGUI, Newtonsoft saved state.
**Spec:** docs/superpowers/specs/2026-09-17-direct-exploration-design.md

## Global constraints
- Preserve existing local UI changes and ProjectSettings changes; no unrelated file replacement.
- Six to ten encounters, two routes, guaranteed intermediate and final extraction.
- Equipment transfers actual warehouse items; loot settles exactly once with overflow retained.
- Character level has no new unlocks, stats or experience awards.
- Pause combat behind help and while backgrounded. Persist enemy, resources and RNG.

## Task 1: exploration rules and saved state
Files: new Assets/Game/Exploration/{ExplorationState,ExplorationSystem,PlayerEquipment}.cs; modify GameSave.cs and ExpeditionSystem.cs; add ExplorationTests.cs.
- [x] Write and run failing tests for length, exit guarantee, mutual dispatch exclusion, resource exhaustion, death attribution, single settlement and round-trip persistence.
- [x] Add Player.CharacterLevel/CharacterExp, Hp/Hydration/Energy, Equipment; GameSave.Exploration, ExplorationOverflow and ExplorationTutorialStep.
- [x] Implement static ExplorationSystem.Start/Move/Choose/Tick/Attack/Cover/Use/Extract/ClaimOverflow with saved state. Reject invalid phase actions without mutation.
- [x] Implement PlayerEquipment.TryEquip/TryUnequip and support supplies with actual inventory transfers.
- [x] Run new rule tests plus compile; review rule invariants.

## Task 2: warehouse browser
Files: WarehouseScreen.cs, inventory presentation helper as needed, Warehouse UI tests.
- [x] Preserve existing sale/transmission checks; replace expanded list rows with compact rows and item detail modal.
- [x] Add category filters, translated original descriptions, item icon, quantities and relevant equipment stats. Keep scroll position after closing.
- [x] Verify 458 imported item IDs and description availability; don't blindly rerun extractor over custom data.
- [x] Compile and verify sale callbacks don't duplicate transactions.

## Task 3: main exploration screen
Files: new Assets/Unity/UI/ExplorationView.cs; AppShell.cs, HomeScreen.cs.
- [x] Add home entry and full-screen preparation, all equipment slots, supplies and map selection.
- [x] Render contextual two-route exploration, explicit exits, weather effects and animated enemy tells. Connect attack modes, melee, cover and item menus.
- [x] Persist commands; save periodically during combat and on pause. Modal and background pauses do not advance combat.
- [x] Add first-run contextual tutorials and replay help.
- [x] Render success/death/exhaustion outcome and item lists, killer data and character level.
- [x] Fade to home and selected employer portrait/message after acknowledgement; persist result acknowledgement.

## Task 4: integration and verification
Files: AlphaPreview.cs, exploration integration tests, locale resources, validation notes.
- [x] Add deterministic preview for preparation, encounter, combat, success/death and warehouse detail.
- [x] Run Compile-GameSources.ps1, targeted Unity EditMode tests, full tests and git diff --check.
- [x] Inspect portrait previews and repair clipping/overlaps.
- [x] Review complete diff for save compatibility, no duplicated loot and gameplay dead ends.

## Execution ledger
Ruling: Continue in the existing working directory because this feature depends on earlier uncommitted UI work; avoid a checkout that omits those changes.
Ruling: User approved execution on 2026-09-17; no further design approval gates.
Ruling: Use bounded subagents under subagent-driven-development for pure rules and warehouse while the primary agent builds screen integration. No shared-file editing across agents.

Completion: Unity full EditMode suite 437 tests, 425 passed, 12 pre-existing failures unchanged; all 25 added exploration/warehouse tests passed. Compile and 16978 locale checks passed. Reviewer findings resolved and covered by UI regressions.
Ruling: Initial main content must be playable on zero-money existing saves, so a one-time explicit NPC starter kit and free home treatment/meal are provided. Supplies are still actual warehouse transfers; repeat kits are blocked.
