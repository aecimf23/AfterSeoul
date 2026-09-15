# Audio assets and runtime

The six short effects and base music are copied byte-for-byte from the user's original EscapeFromSeoul project. The source project is never modified.

| AfterSeoul resource | Original path under EscapeFromSeoul/Assets | Use |
| --- | --- | --- |
| Audio/ui_move.wav | Resources/sfx/ui_move.wav | Tap/navigation |
| Audio/ui_confirm.wav | Resources/sfx/ui_confirm.wav | Confirmation |
| Audio/ui_error.wav | Resources/sfx/ui_error.wav | Invalid action |
| Audio/ui_buy.wav | Resources/sfx/ui_buy.wav | Purchase |
| Audio/loot_open.wav | Resources/sfx/loot_open.wav | Open reward |
| Audio/extract_success.wav | Resources/sfx/extract_success.wav | Completion |
| Audio/Where_the_River_Bends.mp3 | Audio/BaseHome/Where_the_River_Bends.mp3 | Quiet looping background music |

These copies do not establish third-party redistribution rights. Retain the original project's asset provenance when preparing store distribution.

## Playback and settings

`Sfx.Attach(host)` owns a child audio object with independent 2D effects and music sources. Existing `Tap`, `Complete`, `Perfect`, `Good`, `Edge`, `Miss`, `Step`, and `ForScore` calls stay compatible. `Confirm`, `Error`, `Buy`, and `OpenLoot` expose the imported UI cues. Minigame judgement tones remain procedural; imported UI cues have generated fallbacks.

`Sfx.EffectsVolume` and `Sfx.MusicVolume` accept 0–1, clamp other values, and persist in PlayerPrefs under `AfterSeoul.Audio.EffectsVolume` and `AfterSeoul.Audio.MusicVolume`. Defaults are 0.55 and 0.18. Changes immediately update the sources. `Sfx.Enabled` mutes both channels without discarding either channel's volume setting.

The runtime creates a fallback listener only if no active, enabled scene listener exists. Scene changes and periodic checks relinquish that listener if another scene listener appears. It does not alter listeners owned by other systems. Repeated Attach calls do not duplicate sources. Host teardown and subsystem reset release generated clips and managed objects; imported clips remain Unity-managed resources.

## Mobile imports

Short effects use mono Vorbis, preloaded and decompressed on load for responsive repeated UI playback. Music preserves stereo and uses streaming Vorbis, background loading, and no preload to avoid holding the entire decoded track in memory. New GUIDs keep these copied assets independent of the source project.

## Verification

`AudioTests` covers repeated attachment, listener reuse, independent persisted/clamped volume and mute controls, streaming music import, generated-clip cleanup, and attachment after teardown. The parent task runs the Unity test suite; this document does not claim device listening or seamless musical loop validation. Music loop boundaries depend on the source recording.
