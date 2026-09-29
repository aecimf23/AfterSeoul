# Factory tutorial and income guidance

The first factory visit opens four short pages: manual assembly and wages, choosing upgrades, activating automatic production, and using income for the next goal. Closing or following a destination persists FactoryTutorialSeen through ExecuteSavedAction; failed persistence keeps the guide open and allows retry. Existing saves receive the guide once, and account reset restores the first-visit state.

Production retains a replayable help button, a context-aware automation entry point, and an income-use entry point. Each of the four production upgrades has its own help explaining the actual effect and prerequisites. The supply upgrade description now explicitly includes its benefit to assigned workers.

Automation guidance distinguishes idle workers, no hired workers, unavailable workers, and active production. It links to assignment or personnel management without purchasing or assigning automatically. Income guidance links to exploration preparation, base objectives, or factory upgrade comparison, and explains resupply, quests, material uses, production commissions and reinvestment.

Verification: two new tests failed before implementation, then passed. Six actual Unity portrait captures cover all four pages, idle-worker guidance and equipment help. Independent read-only review found no actionable defects. Back navigation is tested to dismiss the guide before returning home. Physical phone testing remains unavailable.

Final EditMode results: 715 passed, zero failures (`Logs/factory-guide-final.xml`). Android development build completed successfully at 11:38 KST, with Unity exiting code 0 (`Logs/factory-guide-android.log`).
