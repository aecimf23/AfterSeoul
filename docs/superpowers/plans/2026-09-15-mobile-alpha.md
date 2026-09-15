# AFTER SEOUL mobile alpha implementation plan

**Goal:** Make the existing mobile loop readable, audible, and verifiable in the atmosphere of EscapeFromSeoul.

**Architecture:** Keep GameSession and the code-built uGUI screens. Reuse the original project's D2Coding font and selected UI/music assets by copying them into AfterSeoul. Preserve original project files and existing saves. Use isolated in-memory sessions for verification.

**Design:** Near-black terminal surfaces, cyan headings, muted green actions, amber warnings, square bordered panels, numbered dossiers, a Seoul field-map motif and a clear home operations summary. Sound should include actual original UI clips, quiet base music, independent persisted controls, and an audio listener when the scene has none. No new external service credentials are assumed.

## Tasks

- [x] UI: bundle D2Coding with license; update Theme/Ui/AppShell/EmployerScreen/HomeScreen; add non-interactive terminal decorations and working audio controls; verify all five tabs in portrait screenshots.
- [x] Audio: replace fallback-only UI audio with original clips and music, maintain existing Sfx API, add volume persistence and listener handling, verify resources and lifecycle.
- [x] Economy: reproduce team/tier comparisons with real data; adjust only after regression expectations are defined; preserve deterministic outcomes, document measured tradeoffs.
- [x] Runtime: run complete EditMode tests, fix first-boot regression and any discovered failures, validate Android compilation/build and attached device availability.
- [x] Documentation: replace stale README status with actual changes, test/build evidence and remaining PC integration/store account work.

## Verification

Run Unity Test Runner against the current project with in-memory test stores. Review initial employer choice and each tab, audio sources and font glyphs. Build Android into a new Builds output, without changing signing identity or publishing. Do not claim physical-device or real-money/network service verification without evidence.
