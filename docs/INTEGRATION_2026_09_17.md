# Integration verification — 2026-09-17

Merged origin/main c046f3a with the direct-exploration work. Retained upstream production, conveyor, starter support and NPC banter while keeping the latest illustrated portraits, image-only opening, Yongsan-first map progression, popup region details, inventory and safe raid entry.

Unified progression reset: preserve paid support and external shipment records from upstream, retain the busy-link guard and explicit confirmation, restart the illustrated prologue and first exploration quest. Updated older UI tests to the current map modal and prologue flows. Region detail dialogs now refresh when a selected scav becomes busy or the region becomes unavailable.

Final EditMode result: 547/547 passed, 0 failed (Logs/merged-push-tests.xml). Game/UI/editor sources compile. Android build succeeded with 0 errors and 1 existing service-link warning. Final editor-only title cleanup uses DestroyImmediate outside play mode.

The nested AfterSeoul checkout is an independent clean clone already at origin/main and is ignored, not embedded. Build, Library and Logs remain excluded from Git. Image and audio assets use the repository Git LFS rules.
