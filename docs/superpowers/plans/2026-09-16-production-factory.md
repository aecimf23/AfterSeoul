# Production factory implementation

Design: docs/FACTORY_PRODUCTION_DESIGN.md. User explicitly requested the redesign and chose auto-delivery for wages. Continue in this checkout, preserving previous uncommitted onboarding changes.

- [x] Core: ProductionState, balance-configured gun catalog, work/upgrade/unlock/select/assign APIs, deterministic offline wages, first-hire support integration. Core implementer owns Assets/Game, balance.json and its standalone regression harness.
- [x] Art: investigate actual mainline gun art read-only and provide a UI-only ASCII catalog matching gun IDs. Art implementer owns ProductionArt.cs and its provenance note.
- [x] UI: parent implements factory production/equipment navigation, fixed artwork region and responsive work controls, progression and roster UI, legacy workshop navigation, updated onboarding copy/locales.
- [x] Review core/art tasks independently; parent handles integration. Review complete change against approved design.
- [x] Verify standalone simulations, full Unity EditMode and actual rendered UI. Package Android with process-local short TEMP/TMP if the existing Java socket-path issue recurs.

Ruling: retain legacy components and paid assistants in an equipment/workshop subview. This preserves existing purchased upgrades and production saves while giving the new wage-production loop the primary surface.

