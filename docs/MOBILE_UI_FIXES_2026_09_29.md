# Mobile preparation and factory fixes — 2026-09-29

Based on origin/main fdaf6c4, including the newer player profile and regional story quest work.

- First departure name input inherited a decorative panel with raycastTarget disabled. Enable raycasts on the input field itself so a touch can activate Unity's native text input. Existing validation, confirmation and save behavior remain in place.
- Recommended packing now updates the existing preparation modal in place, including quantities, confirmation and missing-supply warnings. From manual selection, recommending returns to preparation. No automatic purchase or departure.
- Manual packing uses separate minus/count/plus controls. Counts clamp to owned stock instead of wrapping to zero; adjusting a count does not rebuild the scrolling list.
- Selecting the main factory tab always selects production. Normal refreshes do not reset the selected factory subtab.
- Production artwork is left aligned, with current facility effects beside it. Production upgrades precede contracts and show current/next effects, next-level cost and owned funds. Preview calculations use deep copies, never the live save.
- Recruitment prompts and the personnel shortcut were removed from the production list. Factory staffing remains available under equipment; recruiting remains under personnel.

Verification:
- Three targeted regressions failed before implementation: name touch target, packing feedback, factory selection.
- Full EditMode suite passed: 713 tests, zero failures (`Logs/mobile-ui-tests.xml`).
- After refining in-place packing feedback, all 67 exploration/factory UI tests passed again (`Logs/mobile-ui-final.xml`), including the same-modal assertion.
- Six actual Unity portrait captures cover name entry, recommended packing, quantity selection, production, upgrade comparisons and tall production (`Logs/mobile-ui-preview`).
- Independent read-only code review found no actionable defects.
- Physical phone keyboard, touch and performance testing are not available on this host.
- Android development APK built successfully at 11:28 KST: zero errors, three warnings (two existing unused-field compiler warnings and Unity Services project-link warning). Unity exited with code 0. APK size: 203,006,167 bytes.
