# First hire and playable onboarding

Approved design: the September 16 conversation. Apply in the user's current AfterSeoul checkout so their next test uses the changes. Do not modify EscapeFromSeoul or live saves.

- [x] Add a regression that completes one free practice, sells its output, and hires a tier-1 scav without spending cash. Verify it fails before implementation.
- [x] Add optional saved starter progress. Initialize legacy saves only when no hire/departure history exists. Record practice and sale through GameSession; consume eligibility on any successful hire. Keep paid recruitment available and higher tiers paid.
- [x] Prefer this actionable guide over ordinary quest guidance until the first hire. Show practice/sale/hire actions in the relevant screens; replace the initial six-page briefing with one short invitation (retain replay).
- [x] Mount a paused game preview on selection, focus the workbench, explicitly start it, forward the first held signal input, and prevent preview interaction. Preserve existing hold/timing/inspect behavior.
- [x] Verify failed practice, reloads, migration, invalid purchases, second hires, lost teams, and the existing three-minute orientation. Run Unity UI/core tests and inspect rendered starter screens. Record remaining device-only checks.
