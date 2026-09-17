# Local progress reset and consumable movement recovery

Settings now separates presentation preferences from “계정 초기화 · 처음부터 시작”. The latter opens a dedicated confirmation with cancel and explicit local-progress consequences. Confirmation writes a new save atomically before replacing the live save, cancels notifications and rebuilds onboarding UI. A failed save preserves the old session. Remote accounts and uploaded shipments are not deleted. Active mail operations block reset to avoid racing network callbacks.

Food/drink recovery previously updated energy without refreshing existing route button interactability. Routes now update every frame after consumption; movement/extraction is disabled while consuming and the remaining use time is visible. Energy recovery immediately restores both route buttons without needing a scene change.

The regression reproducing FOOD01 then FOOD05 after zero energy failed before the change (Restored energy must update the existing route buttons). Reset tests cover confirmation/cancel, fresh persisted onboarding and rollback on write failure.

Validation: full EditMode run 500 tests / 488 passed; 12 failures match the previous baseline. All three reset tests and the food/drink route UI regression passed. Sources compile successfully. The live user's save was not reset by this work.

Android build succeeded (0 errors, 1 existing Unity Services warning). Built from the isolated verification project and copied to Builds/Android/AfterSeoul-alpha.apk.
