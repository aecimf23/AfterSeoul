# First expedition implementation plan
Goal: First hire -> free three-minute safe supply retrieval -> one-time 50,000 won delivery -> equipment guidance.
Architecture: Persist a separate orientation state and an expedition marker; resolve through the existing timeline. Mission cargo stays outside warehouse capacity. Existing regular trips remain unchanged.
- [x] Add background regression harness: timing, reload, one-time delivery, invalid teams, migration and normal departures.
- [x] Add Orientation core rules, save fields, GameSession actions and safe return report.
- [x] Add dedicated departure card, home delivery/progression guide and return narrative.
- [x] Run standalone core and UI compilation, regression harness and 144 onboarding simulations.
- [x] Review changes and document evidence and remaining visual/device verification.
No user input/window automation. No commit or deployment requested.
