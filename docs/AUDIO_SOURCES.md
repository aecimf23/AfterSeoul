# AfterSeoul audio sources

The assets below are reused from the local EscapeFromSeoul mainline at
`D:/devSource/EscapeFromSeoul`. The mainline files are read-only inputs; copied
audio bytes are unchanged, while AfterSeoul assigns independent Unity GUIDs.

| AfterSeoul resource | Mainline source | Use |
| --- | --- | --- |
| `Audio/Cold_Iron_Floor` | `Assets/Resources/bgm/Cold_Iron_Floor.mp3` | Looping factory music, streamed without preloading |
| `Audio/reload_complete` | `Assets/Resources/sfx/reload_complete.wav` | Finished production mechanical latch |
| `Audio/weapon_equip` | `Assets/Resources/sfx/weapon_equip.wav` | Workshop miss / slipped tool |

Existing reused resources remain `Where_the_River_Bends` (base music),
`ui_move`, `ui_confirm`, `ui_error`, `ui_buy`, `loot_open`, and `extract_success`.

NPC speech uses the waveform from mainline
`Assets/Scripts/SeoulLogic/AudioPlayer.cs`, `CreateNpcTalkBlipClip` (line 941 at
the time of copying): 22,050 Hz mono, 0.06 seconds, 1,450/1,120 Hz tones at
70/30 weighting, exponential decay 55, clamped to ±0.9. Mainline
`PlayNpcTalkBlip` uses gain 0.32; AfterSeoul preserves that gain with stable
character pitch. Mainline `Program.Views.Progression.cs` reveals a blip every
three characters during quest narrative speech; callers can use that cadence.
AfterSeoul additionally limits speech to one blip per 0.1 seconds, avoiding
bursts when several characters reveal in one frame. A dedicated source prevents
NPC pitch from changing other effects.

`sfx_workshop_hammer` is a newly synthesized 0.34-second metal impact, because
the reused mainline cues do not include a dedicated hammer strike. It combines
a short noise transient, low body, and three inharmonic ringing partials.
Manual strikes use gain 0.65; automatic strikes use 0.22. Completion gain is
0.8 and miss gain is 0.4, all multiplied by the user's effect volume.

Factory context changes fade the current music out over 0.45 seconds and the
selected music in over 0.65 seconds. Re-selecting the current context preserves
playback position. Music and effects retain separate persisted volume/mute
settings. Runtime-generated clips are destroyed with the audio owner;
resource-backed clips remain managed by Unity.
