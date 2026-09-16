# Production workshop ASCII

`Assets/Unity/UI/ProductionArt.cs` is a UI-only catalog with compact workstations and a weapon catalog:

- `ProductionArt.Weapon(string weaponId)` returns an original silhouette for all 25 mobile guns: WPN01–WPN23, WPN26, WPN27. Unknown or null identifiers return a neutral boxed-part silhouette with a question mark.
- `ProductionArt.WorkerFrame(int frame)` returns rest (0), hammer lift (1), impact (2), or recovery (3). Other values return rest. The worker wears goggles, a cap and a pocketed apron; a gun receiver sits in a bench vise for hammer work. All four poses use the same eight-line footprint and stationary bench, receiver, vise and boots. Only the arms and hammer move, and only impact shows a spark.
- `ProductionArt.Worker(bool striking)` remains a compatibility wrapper: false returns rest (0), true returns impact (2).
- `ProductionArt.ScavFrame(int frame, int variant = 0)` shares the four anchored work poses with three headgear variants for assigned scavengers.
- `ProductionArt.EmptyBench()` returns an eight-line vacant station with the same bench and vise anchors.

The caller composes the weapon and worker, and reads the weapon name from the existing localized item data. Weapon art contains no localized labels, gameplay state or production rules. WPN04 is the P17 silhouette used for initial production.

## Source and provenance

The mainline `D:/devSource/EscapeFromSeoul` was inspected read-only. Gun identities, names and weapon types were checked against `Assets/Scripts/SeoulLogic/ItemDatabase.cs` and `Assets/Resources/Locales/en.json`. Filename searches under Assets, the resources inventory, code searches for weapon ASCII/sprites/icons/art, and the narrative `AsciiSequenceCatalog` were inspected. No reusable per-weapon gun image or ASCII catalog was found in those searches. The narrative ASCII sequences are scene art, not an item-art catalog.

These silhouettes and worker poses are newly authored mobile ASCII representations, **not extracted or copied mainline visual assets**. They retain the existing fictional weapon identifiers/names. Shapes emphasize readable weapon families: pistol slides/grips; long bolt-action barrels/scopes; shotgun tubes/pumps; rifle stocks/magazines; compact PDW bodies; and the RKP-16 drum. Small-scale silhouettes are illustrative rather than exact mechanical drawings.

## Rendering contract

Use `Theme.ArtFont` (the bundled D2Coding monospace font), disable rich text and wrapping, and preserve spaces/newlines. Each weapon occupies at most 36 columns and five lines. Player, scavenger and empty workstations occupy at most 20 columns and eight lines, allowing four stations across the factory panel. All art characters are printable ASCII plus line breaks. Render localized weapon names separately using the normal UI font, so changing language cannot disturb silhouette alignment. `EmployerSceneView` provides the existing monospace/best-fit UI precedent.

No raster generation, Unity execution, or mainline edits were used to create this catalog.

## Commission dialogue provenance

`ProductionDialogue.cs` contains original AfterSeoul dialogue for Yongsan Kim's prototype offer, acceptance and completion. Voice references were read from mainline `Assets/Resources/Locales/ko.json`: `QUEST_Q_YONGSAN_01_ACCEPT`, `QUEST_Q_DONGDAEMUN_03_ACCEPT`, `QUEST_Q_DONGDAEMUN_03_COMPLETE`, `QUEST_Q_YONGSAN_KIM_GUNSMITH_1_ACCEPT`, and `QUEST_Q_YONGSAN_KIM_GUNSMITH_1_COMPLETE`. These establish a practical technician who asks for concrete results, accepts rough finish, checks completed work and pays for it. The mobile dialogue is newly written, not copied mainline quest content, and contains no physical manufacturing instructions.

The offer uses three short paragraphs and formatted weapon name, quantity and reward. Acceptance and completion use the localized weapon name. All dialogue is passed through `Loc.Text`, with corresponding entries in the five mobile locale dictionaries (`ko`, `en`, `jp`, `zh`, `ru`).
