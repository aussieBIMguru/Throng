# Round 007 — part 1: escalator ends and deferral; part 2: boarding and rate curves

**Status:** r007h builds; all 169 tests pass (Gavin, 2026-10-25). First drop was (no .NET SDK or NuGet in the sandbox; statically reviewed only, no Python
replica this time to save quota, D-114). Base: r006b (built, tests passing on Gavin's machine).
Part 2 of round 007 (full demand, timetables, profile mixes, demo end to end) still needs its Query phase.

## Query record (answers → decisions)

| Topic | Answer | Decision |
|---|---|---|
| Escalator-pair gridlock | All three: run-off zones, walk clear, boarding hold | D-112 |
| Deferral to nearby options | Per-agent belt cost + measured wait + light balk | D-113 |
| Validation | Static review and tests only | D-114 |
| r006 open questions 1–3 + dead-end ends | Recommendations: one-way Up/Down stairs, stand side tied to keep side, floors hide agents, dead-end walls | D-115–D-118 |

## What was built

**Core**
- `ConnectorPortals`: stairs and ramps with Direction Up/Down are one-way (`AllowsEntry` / `AllowsExit`);
  `FloorEnd.IsUpperEnd` (init property) marks ends on the top level so intermediate levels stay two-way (D-115).

**Sim**
- `RoutePlanner.Choose` gains optional `beltWalkSpeed`, `stayConnector`, `staySeconds`; new
  `RoutePlanner.BeltCorrection` (standers pay the slower ride, D-113). Existing callers are unchanged.
- `AgentStore.OffPortal` / `OffTime`: where and when an agent last stepped off.
- `SimEngine`:
  - **Run-offs (D-112):** one-way exits per floor (`BuildRunOffs`); `RunOffForce` pushes non-exempt agents out of
    the 2.5 m in front of each exit towards its nearest open side (4 m/s² at the comb, half at the far edge).
  - **Walk clear:** for 1.5 s after stepping off, the desired direction is the exit's outward normal.
  - **Boarding hold:** `UpdateWaits` counts people on each exit landing (first 1.2 m); a metered entry holds riders
    while its connector's exit landing is jammed.
  - **Waits (D-113):** belts use queued-near ÷ measured boarding rate + on-their-way ÷ cap; stairs keep D-111.
  - **Balk (D-113):** `Review` every 3 s (staggered by spawn tick) for queuers within 5 m of an entry with a wait
    ≥ 20 s; no noise, 8 s bonus for staying.
  - **Dead-end ends (D-118):** piece ends with no joint or floor end push like a wall (`MarkOpenEnds`).
  - Walkers' belt factor is now a named constant (0.6), shared by movement and route choice.

**Render**
- `SceneRenderer.RenderOrbit`: agents are drawn last, after a depth-only floor pass, so floors hide people on
  other levels (D-117).

**Tests:** 169 methods (+5, including the diagnostics run, in `Throng.Sim.Tests/Round007Tests.cs`): Up stair one-way, bidirectional stair stays
two-way, belt correction, escalator pair used both ways without gridlock.

## How to test

1. Build; run all tests (167). `EscalatorPair_BothWays_DoesNotGridlock` is new and untuned: if it fails, report
   the arrived / stuck numbers in the message.
2. DEMO ▸ PLAY at ×4 for 10+ minutes. Watch both ends of each escalator pair: people stepping off walk straight
   out; queuers stay out of the neighbouring unit's run-off; no lock-up at either end.
3. During build-up, some people now take the street and platform stairs once escalator queues grow; people in
   long queues occasionally peel off to the stair.
4. 3D: agents on the level below no longer show through the floor; outlines of lower levels still do.
5. Select a stair, set Direction = Up, PLAY: nobody walks down it (CHECK may now report unreachable rules if it was
   the only way down; that's expected).
6. Don't-regress (HANDOVER §8).

## Fixes after Gavin's first build (r007b)

Compiled first time. Two test failures, both from D-112 being too strong (fixed by D-119, unreplicated):

- `Demo_PeopleReachThePlatform`: 32 stuck vs 162 arrived. The 4 m/s² run-off push beat walking drive (~2.7 m/s²),
  so paths crossing in front of an exit were walled off. Now scaled by how fast the person is moving (full when
  standing, 15% at walking pace).
- `EscalatorPair_BothWays_DoesNotGridlock`: 127 arrived (> 150 expected). The boarding hold counted riders walking
  clear, so it paused in normal flow. Now only slow people (< 0.3 m/s) count, threshold 2 for a 1 m unit.
- Walk clear now applies at one-way exits only; at a two-way stair end it walked people into the boarding queue.

**r007c:** the pair test then gave 114 arrived. Standers stepped off from rest (their walking velocity on the belt is
about zero), so they briefly counted as slow on the landing and tripped the hold during normal flow. D-120: belt
momentum carries over on stepping off, and fresh step-offs (< 1.5 s) don't count towards the jam.

**r007d:** r007c gave demo 22 stuck vs 173 arrived (1 over the limit) and pair 143 arrived (150 wanted). Rather than
keep guessing, D-121 adds `SimTuning` switches for each round 007 rule, a stuck log on the engine, and
`Round007Tests.Diagnostics_Round007Rules` (always passes; prints a table to the test output). Next step: Gavin pastes
that output; tune from it.

Gavin also asked: the unused stair on the left of the top floor is the back-of-house stair, staff only by design
(D-110). The demo emits about 96 people/min (60 down from the entrances, 36 up from the doors), within the stairs'
and escalators' capacity on paper; lifts won't change that much, as they carry a small share.

**r007e (from the diagnostics output):**

| Run | Demo arrived / stuck | Pair arrived / stuck |
|---|---|---|
| all on | 173 / 22 | 143 / 45 |
| all off | 259 / 2 | 93 / 88 |
| no run-offs | 238 / 2 | 88 / 92 |
| others off one at a time | 157–193 / 7–26 | 140–143 / 45 |

- Demo: the run-offs caused it (D-122): the sideways push drove people into the platform stair foot beside the
  escalator pair. Now the push is straight out along the exit only.
- Pair: run-offs help a lot; the other rules don't matter there. The remaining "stuck" were queue tails from
  overload, so the test now runs at 40/min each way (D-123).
- Open: escalator boarding in crowds (about half the cap) is now the main limit, so it's open question 1 for part 2.

**r007f:** r007e passes every test. Diagnostics: demo 247 arrived / 2 stuck with every rule on (same stuck as all
off); pair at 40/min 263 / 0. Gavin saw people stuck at stair bases in the viewport: D-124 fades the keep-side bias
near connector ends, just after stepping off, and in jams. Diagnostics gain a "no keep-side fade" row.

**r007g:** Gavin's plan views: people wedged into the 0.6 m strip between escalator units (D-126 closes footprint
gaps under 1 m), and down walkers blocking up walkers head-on on a stair (D-125: constant lanes on and at two-way
stairs and ramps, replacing D-124's fade there). +1 Core test (pair gap closed).

## Judgement calls

- Run-offs only apply to **one-way exits** (escalators, travelators, one-way stairs): on a two-way stair the exit
  is also an entry, and a keep-clear zone would push away the people waiting to get on.
- People who stepped off are exempt from their own exit's run-off until they next board anything.
- The boarding-rate estimate starts at half the cap (what the r006b replica measured in crowds) and is only
  updated while two or more people are queued, so a quiet escalator doesn't look slow.
- A balk can also switch to walking straight to the sink when that's now quicker.
- 3D agents are now drawn after the outlines; an outline in front of an agent is drawn under it (minor).

## Accepted defaults

- Tuning values (2.5 m run-off, 1.5 s walk clear, 1.2 m landing at 2.5 p/m², 5 m queue radius, 30 s smoothing,
  3 s review, 20 s balk wait, 8 s stay bonus): all constants at the top of `SimEngine`, cheap to change.

## Known limitations

- **Unbuilt and unreplicated:** expect compile fixes and some tuning.
- Escalator boarding still reaches only about half the cap in a crowd (watch item); waits now use the measured
  rate, so route choice accounts for it.
- Lifts aren't simulated (M4); lift-only profiles still need ramps or travelators.

## Watch list

- The run-off push fighting people who are trapped by the crowd behind them (it's soft and capped by MaxAccel).
- Boarding hold too eager (landing threshold of 3 for a 1 m unit) at high but healthy flows.
- Balk flip-flopping between two crowded options (the 8 s stay bonus should prevent it).

## Open questions (round 007 part 2 Query phase)

1. Escalator boarding rate in crowds: tighten belt spacing so dense crowds approach the cap? (Recommended: yes,
   after seeing this round's runs.)
2. Should the demo emit a few lift-only agents once lifts land in M4? (Recommended: yes, with M4.)
3. Then D-095's part 2: rate curves, vehicle timetables and batches, profile mixes, train door flows, demo end to end.

## Next

Fix whatever this round's build and tests report, then round 007 part 2's Query phase.

---

# Part 2 (r007i): boarding spacing and rate curves

**Unbuilt:** no .NET SDK in the sandbox again; statically reviewed only. Expect small compile fixes.

## Query record (answers → decisions)

| Question | Answer | Decision |
|---|---|---|
| How to close the boarding gap (about half the cap in crowds) | Tighter spacing (recommended) | D-128 |
| How stuck spots get reported | Paste diagnostics from this build (recommended) | D-127 |
| Scope this round | Boarding + rate curves (recommended) | D-127 |
| Curve shape / scale / end / editor | Presets + editable points; multiplier × rate; hold last value; summary + sparkline + point grid (all recommended) | D-129 |

## What was built

- **Core:** `RateCurvePoint` and `EmissionRule.RateCurve` (`Model/EmissionRule.cs`); `Model/RateCurves.cs` (presets,
  validity, compile to arrays, allocation-free evaluation); validator warns about bad curve points.
- **IO:** format **v5** (`UpgradeV4ToV5`: null curves → flat, null points dropped).
- **Sim:** `SimEngine` runs each rule's arrival clock at the curve multiplier (D-129); comb spacing (D-128): agents
  in the same metered belt's boarding group (`BoardingGroup`) push each other at 30%; per-connector boarding totals
  and `BoardingsPerMinute(k)`; `SimTuning.CombSpacing` switch.
- **App:** SELECTION ▸ rule: **Rate curve** preset menu (shows "Custom" once edited), summary line (range, span,
  mean and mean people/min), sparkline, minute / × rate grid with remove buttons, ADD POINT. Every edit is one undo step.
- **Tests:** +9 methods (`RateCurveTests` ×4 Core, `RateCurveFileTests` ×1 IO, `RateCurveSimTests` ×2 Sim; diagnostics
  gain a "no comb spacing" row, a boarding column and a saturated pair run). Expect **176** tests (169 + 7 new;
  the diagnostics method was already counted).

## How to test

1. Build; run all tests. All should pass (the diagnostics one always passes).
2. Run `Round007Tests.Diagnostics_Round007Rules` and **paste its output**. Look at the saturated pair: "boards
   N/min (P% of cap)" with all on vs "no comb spacing". Hoping for 80%+ with it on, about 50% off.
3. Demo, select the West entrance, rule 1 ▸ Rate curve ▸ *AM peak*: the summary reads "0.4–1.8× over 60 min…",
   the sparkline rises and falls, five points show.
4. Edit a multiplier: the menu switches to *Custom*; Ctrl+Z restores *AM peak*.
5. Remove every point: the rate is flat again. ADD POINT on a flat rule adds (0 min, 1×).
6. Set Event surge, PLAY: quiet for 10 min, then a crowd at the escalators from about 12 min (use a faster sim speed).
7. Save, reopen: the curve survives. A v4 file opens with flat rules.
8. Report any stuck spots you see (screenshot plus the diagnostics' "where").

## Judgement calls

- The boarding group covers people within 1.5 m of the entry *and heading for it*, so passers-by keep their full
  personal space; riders count only on their own belt.
- Curves run on minutes from the run start (no clock time in runs yet). Presets span an hour; runs shorter than that
  see only the start of the curve (the AM peak's peak is at 30 min).
- Summary and sparkline cover the curve's span (at least 1 min).

## Known limitations

- Unbuilt; CombSpacing's 30% and 1.5 m are first guesses, to be tuned from the diagnostics.
- No drag-editing on the sparkline yet (by choice).
- Diagnostics don't exercise rate curves (they're flat by design, for comparable rows).

## Watch list

- Weaker push at the comb letting people overlap visually at high density (contact correction should stop it).
- The boarding hold (D-112) may now trip more often, as landings fill faster when boarding is quicker.

## Open questions (next Query phase)

1. Comb spacing tuning from the pasted output (factor, range).
2. Vehicle timetables and batches next? (Recommended: yes, as a train door zone with arrive → unload → board → depart.)
3. Profile mixes per rule (one rule, several profiles by share) or keep one profile per rule? (Recommended: mixes.)

## r007j (from Gavin's r007i build and diagnostics)

Built first time; 175/176 passed. The failure was `ThrongFileTests.Save_WritesFormatV4` still expecting format 4
(now `Save_WritesFormatV5`, expects 5).

| Run (r007i) | Boards, % of cap | Arrived / stuck |
|---|---|---|
| Demo, any row | 26–31% (demand-limited) | 270–303 / 0–3 |
| Pair 40/min, any row | 38–39% (= demand) | 266–277 / 0 |
| Saturated pair, all on | **56%** | 302 / 32 |
| Saturated pair, no comb spacing | 21% | 94 / 210 |
| Saturated pair, all off | 20% | 40 / 262 |
| Saturated pair, no run-offs / no keep-side fade | 49% / 54% | 225 / 105, 242 / 98 |

Comb spacing works (21% → 56%, stuck 210 → 32). D-131 takes one more step: 20% force within 2.0 m. Saturated-pair
stuck removals are queue tails from 120/min demand on a 100/min unit, so they're expected. Demo stuck spots are down
to 0–3 on the Platform stair, which is acceptable for now.

## Next

Gavin builds r007j and pastes the diagnostics (saturated pair, all-on row vs 56%). If boarding dropped or stuck
rose, go back to 0.3 / 1.5 m. Then the Query phase for timetables, batches and mixes.

---

# r007k: counterflow-gated stair lanes, escalator weighting (quick round)

**Unbuilt:** no .NET SDK in the sandbox; statically reviewed only. Quick round on Gavin's request (89% of the daily
quota used): built the two proposals above as written, without a Query batch.

## What was built

- **D-132, `SimEngine`:** `UpdateWaits` counts each stair's and ramp's up and down users (`CountStream`: riders by
  exit height against their own height; people within 4 m of a two-way end by planned exit height). A per-connector
  lane factor (minority share ÷ 20%, capped at 1, smoothed over 2 s) blends `KeepSideStrength` from the
  oncoming-only nudge to D-125's full lane (`Lane`). New switch `SimTuning.CounterflowLanes`.
- **D-133, `SimEngine`:** escalator boarding-rate seed 0.8 × cap (`BoardingRateSeed`); stair and ramp waits add slow
  queuers (< 0.3 m/s) within 5 m of the entry: (load + stalled) ÷ capacity, only with `MeasuredWait` on.
- **Tests:** +1, `Round007Tests.WideStair_OneWay_UsesTheCentre` (3.6 m stair, 120/min up, 180 s: at least 15% of
  the people on the stair in its middle third; untuned). Diagnostics gain a "no counterflow gate" row and a
  "STAIR 3.6 m one way" run. Expect **177** tests.

## How to test

1. Build; run all tests. If `WideStair_OneWay_UsesTheCentre` fails, paste its message (x of y in the middle third).
2. Run `Diagnostics_Round007Rules` and paste the output. Check: saturated pair, all on, above r007i's 56% (D-131);
   STAIR run, all on vs "no counterflow gate"; demo stuck counts.
3. Demo ▸ PLAY ×4: down commuters on the wide stairs spread across the width; when people come up as well, they
   still split into lanes.
4. More people take the escalators during build-up than in r007j.

## Watch list

- Lanes switching off too readily when a few people go against a big one-way stream (minority under 20% gets a
  partial lane; the oncoming nudge still applies to them).
- Stair waits now higher in jams: people may over-favour escalators. If so, drop the stalled-queue term.
- Stander belt cost (D-113, standers pay the slower ride) left as is; revisit if escalators are still under-used.

## Next

Gavin builds r007k and pastes the diagnostics. Then the Query phase for vehicle timetables and batches, profile
mixes, train door flows and the demo end to end.

---

# r007l: landings and stair approaches (quick round)

**Unbuilt.** Gavin on r007k: people take landing corners too shallow and cut across the other direction; people
entering stairs cut each other off. D-134: landings of stairs and ramps now get the lane push at 1.5× (counterflow
gated, so one-way flow still uses the full width); the approach lane at two-way ends starts 6 m out (was 4 m).
No new tests (177 expected).

**Test:** demo ▸ PLAY, watch switchback landings with traffic both ways: each stream turns on its own side. Paste the
diagnostics (STAIR run, demo stuck counts). **Fallback if landings still choke:** let landing turns use a wider path
(flow field biased to the outer edge per direction), or let people overlap slightly on landings.

# r007m: landing squeeze (quick round)

**Unbuilt.** Gavin on r007l (screenshot): up-goers on the left jam at a switchback landing within 2 minutes. D-135:
same-direction people on a stair/ramp landing push each other at 30% (`SimTuning.LandingSqueeze`, new diagnostics
row); the 1.5× landing lane push from D-134 is back to 1.0×. **If it still jams:** the next step is a per-direction
flow field on landings that takes the outer side of the turn for one stream and the inner for the other.

# r007n: stall escape (quick round)

**Unbuilt.** Gavin on r007m: two people stuck 2 min at the corner where a landing meets a flight. D-136: after 4 s
without progress a person drops the keep-side push and stalled pairs push at 30% (`SimTuning.StallEscape`, diagnostics
row). Root cause not found (quota). **Next session, first:** reproduce in a test with a switchback stair and
two-way flow, log the two agents' desired directions at the corner (head-on? flow field pointing into the inner
corner wall? dead-end wall from D-118 at the joint?), then fix at the source. Fallback: per-direction landing paths.

# r007o: landing carry (quick round)

**Unbuilt.** Gavin on r007n: the corner stall is people turning as soon as they reach the landing. D-137: on stepping
from a flight onto a landing, carry straight on 0.6 m before turning (`AgentStore.Joint*`, `SimTuning.LandingCarry`,
diagnostics row). If it holds, the corner-stall investigation above can be closed.
