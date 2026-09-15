# Team and tier economy validation — 2026-09-15

## Change

`ExpeditionSystem` now multiplies the final loot-roll budget by:

`sqrt(teamSize) × (1 + 0.20 × max(0, averageSearch − 4))`, rounded to an integer.

Extra members therefore increase carrying/recovery capacity with diminishing returns. Search improves recovered value beyond its existing small capped bonus. Existing survival/combat checks still provide the safety benefit. Costs, JSON data, gear effects and loot entries are unchanged. A solo scav with Search ≤4 keeps the old reward budget exactly. Gear, event and trait bonuses participate in the capacity multiplier; this is intentional. This increases progression and material supply, so long-term progression pacing still needs playtesting.

No save fields or schema changed. RNG streams remain derived from the saved departure seed; return is still deterministic and resolves once. **A departure already in flight when the game updates uses the new reward budget when it resolves.** This is save compatibility, not preservation of pre-update reward outcomes. If cross-version reward preservation becomes required, snapshot/version the departure rules separately.

## Method and limits

Compiled **all actual `Assets/Game` C# sources** together with `Tools/EconomySimulation.cs`, using Unity's Mono compiler/runtime and the installed Newtonsoft package. The harness invokes `JsonDataRegistry.Load`, `ExpeditionSystem.Depart`, `OfflineResolver.Resolve`, inventory capacity and actual accident/severity logic. It does not duplicate the loot formula.

Each before/after run used 3,000 saved RNG counters (1–3,000), seven maps, tiers 1–3, members 1–4, empty warehouse capacities 60 and 9,999, and starter/full equipment: **336 configurations, 1,008,000 departures per version**. Homogeneous teams use rounded mean total tier stats divided equally across Search/Combat/Survival (4/6/8). Full gear selects the first highest `Equipment.EffectRank` item for each slot, matching the existing data tests. Starter gear uses each tier's actual starter weapon. No traits or employer bonus; unlocks/money are supplied to permit the experiment. Same seeds across configurations enable reproducible comparisons, not claims of statistical confidence for tiny differences.

All values below are mean base-price inventory values in won. `net = retained loot − departure cost`. The conservative **risk-net proxy** additionally includes event cash, then subtracts treatment cost for injuries, tier hire cost for missing/dead members, and base value of all lost gear. It does not simulate rescues, downtime, shop availability, sale markdowns, the initial equipment investment, or long-term inventory accumulation. Counting all lost gear alongside replacement hiring also ignores the value of the replacement's included starter weapon; starter-loadout comparisons are therefore conservative. It is a comparison proxy, not a claim about actual lifetime cash profit.

## Guro, full gear, default 60-slot empty warehouse

| Tier | Members | Before loot | After loot | Cost | Before risk net/scav | After risk net/scav |
|---|---:|---:|---:|---:|---:|---:|
| 1 | 1 | 423,852 | 423,852 | 85,000 | 107,065 | 107,065 |
| 1 | 2 | 472,328 | 656,662 | 170,000 | 105,070 | 197,237 |
| 1 | 3 | 485,118 | 827,926 | 255,000 | 62,308 | 176,577 |
| 1 | 4 | 485,118 | 964,506 | 340,000 | 24,989 | 144,836 |
| 2 | 1 | 465,110 | 646,938 | 133,000 | 133,112 | 314,940 |
| 2 | 2 | 485,118 | 964,506 | 266,000 | 81,494 | 321,188 |
| 2 | 3 | 485,118 | 1,162,559 | 399,000 | 11,571 | 237,385 |
| 2 | 4 | 485,118 | 1,351,026 | 532,000 | -24,912 | 191,565 |
| 3 | 1 | 508,217 | 917,420 | 193,000 | 26,574 | 435,777 |
| 3 | 2 | 529,530 | 1,344,760 | 386,000 | 41,010 | 448,625 |
| 3 | 3 | 529,530 | 1,635,731 | 579,000 | -41,170 | 327,564 |
| 3 | 4 | 529,530 | 1,912,510 | 772,000 | -78,738 | 267,007 |

Tier1 pair risk net per deployed scav is clearly better than solo, while three/four members deliver more total value but less per person than the pair. Higher-tier solo scavs now have a measured upgrade niche. The original Python notes' universal tier reversal was **not reproduced exactly**: actual baseline tier2 already beat tier1 on this fixture. Actual tier3 reversal and flat group yield were reproduced.

Accident outcomes are unchanged by this loot-only change. Guro tier1 full gear lost members per expedition: solo 0.1013, pair 0.0390, trio 0.0190, quartet 0.0193. Lost expensive equipment makes the actual resolver's team safety benefit significant.

## Best measured size by risk net per deployed scav

Default capacity60, after change. Each cell is `members (risk net/scav)`; these are sampled optima under the above assumptions, not recommended universal team sizes.

| Map | Starter T1 | Starter T2 | Starter T3 | Full T1 | Full T2 | Full T3 |
|---|---:|---:|---:|---:|---:|---:|
| MYEONGDONG | 2 (12,941) | 1 (28,034) | 1 (34,002) | 2 (73,044) | 1 (154,322) | 1 (201,610) |
| YONGSAN_MARKET | 3 (-7,637) | 2 (-28,158) | 4 (-17,514) | 2 (109,619) | 2 (181,564) | 2 (254,038) |
| GURO_FACTORY | 3 (7,239) | 2 (5,462) | 4 (20,987) | 2 (197,237) | 2 (321,188) | 2 (448,625) |
| UIJEONGBU | 1 (41,992) | 1 (65,606) | 1 (182,553) | 2 (305,364) | 1 (567,865) | 1 (869,479) |
| HAN_RIVER | 2 (6,288) | 1 (27,812) | 1 (56,824) | 2 (124,595) | 1 (262,295) | 1 (352,908) |
| GANGNAM_STREETS | 2 (-784) | 1 (-13,997) | 2 (13,326) | 2 (177,582) | 1 (337,915) | 1 (493,053) |
| NAMSAN_WOODS | 1 (29,154) | 2 (48,789) | 2 (77,390) | 2 (250,654) | 1 (388,086) | 1 (563,601) |

Neither solo nor four-member teams win everywhere. Some starter-loadout hazardous runs remain poor under the conservative loss proxy; this change does not establish complete economic balance or remove the equipment decision.

## Capacity and regression checks

The 60-slot capacity actually binds: Namsan full tier3 quartet discards mean 136,438 won per return, trio 31,961; Uijeongbu full tier3 quartet discards 2,676. Guro examples above have zero overflow. The harness reports **retained** value plus discarded value separately; 9,999-slot comparisons expose capacity effects rather than silently crediting discarded loot. A partly/full warehouse can substantially worsen earnings beyond these empty-warehouse scenarios.

`TeamEconomyTests` tests useful team/tier niches, diminishing per-person gross yield, deterministic replay, one-time settlement, and exclusion of zero-capacity overflow. Standalone compilation of those same tests (only Unity's StreamingAssets path replaced by a filesystem path) against the immutable baseline binary: **two economic tests fail, replay/overflow passes**. Against the changed binary: **all three pass**. Parent task runs the complete Unity EditMode suite separately.

## Reproduce the current matrix

Run from PowerShell after Unity has restored packages:

```powershell
./Tools/Run-EconomySimulation.ps1 -EditorData 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Data' -Samples 3000
```

CSV goes to ignored `Library/Economy/current.csv`; no editor/UI or concurrent Unity batch invocation is needed. Supply another installed Unity Editor/Data path where applicable. This session's immutable before/after binaries and full CSVs are in `Library/Economy/baseline.*` and `Library/Economy/after.*` (local verification artifacts, not tracked source).
