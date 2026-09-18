# Player profile and first departure name — 2026-09-17

The persistent base header now opens a character profile via the player's name and character level. The profile shows the saved name, character level/XP, separate base level/XP, vitals, employer, and all seven equipment slots with item stats. Existing character progression values are displayed without changing progression rules.

Before an unnamed player begins exploration, the selected employer asks for their name. Confirmation saves the trimmed name before proceeding to the existing map introduction/start flow. Closing the question never starts a raid; a later attempt asks again. Existing saves with no name remain compatible. Names accept 1–16 text elements; blank names, control characters, and rich-text delimiters are rejected. Name labels never interpret rich text.

Validation: initial regression failed because unnamed players previously departed directly. Full EditMode suite: 580/580 passed (`Logs/player-profile-all.xml`). Actual 1080x1920 uGUI captures: `Logs/player-profile-preview/`. Native phone keyboard interaction is not covered by these editor captures. The player's real save was not changed by testing.
