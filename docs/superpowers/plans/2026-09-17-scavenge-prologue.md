# Scavenge and Prologue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Execute continuously within existing user authorization.

**Goal:** Add meaningful container farming and a narrated, illustrated first quest leading to exploration.
**Architecture:** Saved loot choices extend the exploration state machine; a small saved first-quest model drives NPC dialogue and objectives. The prologue precedes employer choice and reuses Unity illustration primitives.
**Tech Stack:** Unity 6000.3, C#, Newtonsoft JSON, NUnit.
**Spec:** `docs/superpowers/specs/2026-09-17-scavenge-prologue.md`

## Global Constraints
- Preserve unrelated uncommitted work and live Unity editors.
- Maintain mobile layout, five locales, saved-action rollback, exactly-once rewards.
- No changes to mainline project or existing direct exploration map order.

## Tasks
- [x] Prologue: WelcomeBriefing, new StoryIllustration, EmployerScreen, AppShell entrance, UI tests. Implement before-choice saved four-page story and replay; remove outdated factory reference briefing.
- [x] Rules: LootContainers, ExplorationState/System, GameSave, FirstExplorationQuest, tests. Persist two choices, route hints, guaranteed appropriate first container, quest accept/report lifecycle.
- [x] Integration: ExplorationView and HomeScreen: first quest invitation before gun gift, compact objectives, item-choice UI and reports. AppShell prioritizes first quest.
- [x] Localization, meaningful rule tests, compilation, preview/UI checks; independent review; fix issues and record evidence.

## Ledger
- Ruling: Use code-native illustrations rather than generated raster assets; existing game is built around flat Unity UI scenes and the user asked for simple images.
- Ruling: First quest needs relevant container search and safe return, not a required kill; this teaches survival without an unlucky encounter blocking onboarding.
- Review: Fixed missing quest/gift dialogue after failed-save retry; added both regression cases.
- Verification: Isolated Unity copy keeps live user editors untouched. `Logs/scavenge-final-tests.xml`: 469 tests, 457 passed, same 12 baseline failures as `direct-final-tests.xml`. All 55 exploration/quest/prologue cases pass. First-boot regression tests now assert prologue → person choice → first quest.
- Verification: Game/UI/Editor compilation passed. Five locales contain 55 new keys each; 18,536 locale checks and 13 first-run checks pass.
- Visual: `AlphaPreview.CaptureScavengePrologue` renders 14 mobile screenshots at 1080×1920. Inspected story, choice, quest/gift, medical box, item selection, receipt, routes and report flow.
- Regression correction: Previously added Yongsan Base reused early market dispatch economics and failed the existing region-payoff comparison. It now shares the existing same-level Uijeongbu dispatch budget/loot table; direct exploration unlock order is unchanged. Full data suite passes.
