# Mobile first-run guide and languages

New local saves start a six-page introduction after employer selection. It explains the actual loop: free workbench minigames, warehouse sales, recruiting, the free three-minute supply expedition, and the 50,000-won delivery. Closing or skipping completes the introduction; Settings can replay it without changing progress or granting rewards. Each page transition is saved. Failed writes retain the previous page and show an error inside the guide.

`GameSave.WelcomePage` defaults to `-1` so old JSON saves do not get a new popup. Only `SaveService.CreateNew()` opts in with `0`, including recovery from a corrupt save (whose original backup is retained). Account login is optional and independent of the introduction. Existing adaptive next-action guidance remains active after the introduction.

## Languages

Korean (`ko`), English (`en`), Japanese (`jp`, matching the main game), Simplified Chinese (`zh`), Russian (`ru`). First boot follows the system language; other languages fall back to English. The title's LANGUAGE button applies a choice before entering gameplay. Settings saves in-game changes for the next launch so active minigames and reports are not interrupted. Saves and employer choices are unchanged.

The 679 mobile translation keys cover the introduction, opening, employer screen, navigation, settings, all five main screens, minigames, crafting, expeditions, personnel/equipment/treatment, warehouse/trade, link/dispatch status, notifications, and quest/event dialogue. Existing main-game item, map and trader translations remain separate.

`Loc.Text(koreanSourceTemplate, args)` uses the Korean source as a key, then the selected mobile dictionary and English fallback. `Loc.Get(resourceId, args)` remains for existing data/dialogue and guide IDs. Stable navigation IDs, item tags and saved names are never translated in storage or comparisons; translate them only at display sites. New strings must be added to all five `Assets/Resources/Locales/mobile/*.json` files, preserving format placeholders. The main-game extraction script does not overwrite these mobile files.

Bundled Noto Sans CJK (SIL OFL license included) provides Japanese/Chinese and fallback glyphs. D2Coding remains the ASCII art font. Mobile labels wrap and fit within their allocated bounds.

## Verification

Run `Tools/Run-LocalizationVerification.ps1` after Unity restores packages. This checks fresh/legacy/corrupt saves, progress persistence, fallback formatting, locale key parity, dialogue preservation and all localized format arguments. The implementation was additionally compiled with the Unity response files (game, UI, edit-mode test sources), and existing orientation, delivery and vault standalone checks passed. Font cmap coverage was checked for all translated characters. Unity UI/device screenshots and Android APK builds have not been performed for this change; rebuild the APK and verify text sizing/taps on the target device before release.
