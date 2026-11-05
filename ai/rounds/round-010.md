# Round 010: crowd-aware choice, then trains

**Status:** 010a built and confirmed by Gavin 2026-10-26 (228 pass, 1 skipped). 010b built 2026-10-26, **unbuilt**
(no .NET SDK in the sandbox): trains, landing waypoints, stress changes (D-192–D-195). Format v7.
Starting point: r009 final (223 pass, 1 skipped; wheelchair gate parked, D-176).

## Decided

| # | What |
|---|---|
| D-177 | Two parts: 010a crowd-aware choice, then 010b trains. |
| D-178 | Crowd-aware: connectors and exits (nearest-sink rules). Devices unchanged; dwell zones stay crowd-blind. |
| D-179 | Live crowd only for options on the same floor within the profile's sight radius (`SightRadiusM`, default 30 m). |
| D-180 | Re-choose every ~3 s while the chosen option is in sight, with a stay bonus. Replaces D-113's 5 m / 20 s trigger. |
| D-181 | Stairs and ramps measured like belts: people on the flight and near the entry ÷ measured flow. |
| D-182 | Exit crowding: density near the sink → seconds; nearest-sink rules and train doors only. |
| D-183 | Hover during a run shows a connector's or exit's crowd wait; diagnostics rows per new switch. |
| D-184 | Train service = group of door zones + timetable. |
| D-185 | Headway + offset + dwell + alighting count; optional load curve. |
| D-186 | "Board train" journey step; alight first, then board. |
| D-187 | Capacity per train; LEFT BEHIND in the status bar. |
| D-188 | 010a stays format v6; 010b moves to v7 with migration. |
| D-189 | Demo: two alternating services every 6 min, 3 min apart, ~150 alighting, 45 s doors. |
| D-190 | Plain train box along the edge while in; door zones lit while open. |
| D-191 | Stress by progress along the route, not speed; queuers always count (Gavin, mid-build). |
| D-192 | Stair landings: lane waypoints on your own half (deeper on the outside of the turn); built with 010b. |
| D-193 | Stress linear from the first second held to 1 at full patience; built with 010b. |
| D-194 | Density above the profile's tolerance builds held-up time too; built with 010b. |
| D-195 | 010b judgement calls (doors' own rules say who gets off; a door is a sink only while boarding; …). |

## Plan: 010a (crowd-aware choice)

1. **Core**: `AgentProfileTemplate.SightRadiusM` (default 30; built-ins: commuter 40, infrequent 20, others
   default; versions bumped). Shown in the profile summary; editing comes with profile editing.
2. **Sim: measuring**
   - `SimEngine.UpdateWaits`: stairs and ramps get the belt method (D-181): people near the entry and on the flight
     ÷ smoothed measured flow, clamped to 10–100 % of capacity. Keep D-111 load ÷ capacity behind a switch.
   - Per-sink crowd wait (D-182): people within `SinkCrowdRadius` of each sink ÷ its area → seconds via a
     density-to-delay factor; computed in the same pass.
   - Also keep each connector's and sink's *uncrowded* expected time (0 wait) for out-of-sight use.
3. **Sim: choosing**
   - `RoutePlanner.Choose` takes the agent's position and sight radius: a connector's wait counts only when its
     entry is on this floor within sight; otherwise 0.
   - Nearest-sink choice (exit step and plain nearest-sink rules) adds the sink's crowd wait when in sight.
   - `Review` (D-180): every 3 s (staggered), while the chosen connector entry or sink is within sight, re-run the
     choice without noise and with the stay bonus (8 s); switch on a clear gain. Drop the 5 m / 20 s gate.
4. **Tuning switches**: `SimTuning.StairMeasuredWait`, `SightLimited`, `ApproachReview`, `ExitCrowd`; a diagnostics
   row for each off.
5. **App**: run-time hover label on connectors and sink zones: "Stair ~14 s" / "Exit ~6 s" (engine exposes the
   current waits in the snapshot or via the runner).
6. **Tests**: a free escalator wins over a packed stair for people approaching from the stair side; people out of
   sight don't react; exit choice splits toward the quieter of two equal-distance exits; no ping-pong (switch count
   bounded); diagnostics rows present.

## Plan: 010b (trains), outline

- Core: `TrainService` (name, door zone ids, headway, offset, dwell, alighting per train, alight mix lines, boarding
  capacity, optional rate curve), `Study.TrainServices`; journey step `BoardTrain` (service or tag). Format v7 +
  migration. Validation (doors exist, on one level, dwell > alighting time).
- Sim: service clock (arrive → alight over ~20 s → board → depart), alighters spawned at doors from the service's
  mix, boarders wait spread along the platform (quieter doors, D-182), stand aside, board until capacity, LEFT
  BEHIND count.
- App: STUDY ▸ TRAINS list + SELECTION ▸ TRAIN SERVICE editor; train box and lit doors in the viewport.
- Demo per D-189. Detailed plan and any remaining queries at the start of 010b.
- Landing lane waypoints (D-192): per landing piece and direction, a waypoint in the middle of the keep-side half
  (depth by inside/outside of the turn), then the keep-side half of the next flight's mouth; switch
  `SimTuning.LandingWaypoints` and a diagnostics row; test that descending people on a switchback stay on their half.
- Stress (D-193, D-194): `StressFrom` linear 0 → 1 over patience; local density (spatial hash neighbours within
  ~1 m → p/m²) above a level set by `DensityTolerance` adds held-up time; switch and diagnostics row; update
  `StressTests` (the curve test changes by decision).

## Accepted defaults (non-blocking; cheap to change)

- Sight radius 30 m default; review every 3 s; stay bonus 8 s; sink crowd radius 3 m.

## Open questions (non-blocking)

- D-176 (wheelchairs at the accessible gate) is still parked; pick it up whenever you like.

---

## 010a as built

### What changed

- **Core**: `AgentProfileTemplate.SightRadiusM` (default 30 m; commuter 40 → version 3, infrequent 20 → version 2;
  others default). Profile summaries add "sees N m".
- **Sim**
  - `RoutePlanner.Choose` gains `sightRadius`; a connector's wait counts only when its entry is within it
    (`RoutePlanner.InSight`). Every engine route choice passes the agent's radius (`SimEngine.Sight`).
  - `Engine/SimEngine.Crowd.cs` (new partial): stair/ramp measured wait (`MeasuredStairWait`, D-181), exit crowd
    counting and waits (`CountExitCrowd`, `UpdateExitWaits`, D-182), sight tests, `ReviewApproach` and `ReviewExit`
    (D-180), public `ConnectorWaitS` / `ExitCrowdS`.
  - `SimEngine.UpdateWaits` counts people on each flight and near each exit; stair entries now count towards the
    measured flow (`_boarded`, also `BoardingsPerMinute` for stairs); stairs' boarding rate seeds at 0.8 × capacity.
  - `Review` dispatches to `ReviewApproach` (D-180) or the old `ReviewNearEntry` (D-113) by switch.
  - Nearest-sink choice (`PickSink`) adds each exit's crowd wait when in sight.
  - Stress (D-191): `AgentStore.PrevCost` / `Pace`; `UpdatePace` after each move; `UpdateStress` uses pace < 0.5 for
    walkers and always counts Queue / WaitJoin.
  - `SimTuning`: `StairMeasuredWait`, `SightLimited`, `ApproachReview`, `ExitCrowd`, `ProgressStress` (all on).
  - `SimRunner.TryGetCrowdWait(id)` publishes connector and exit waits with each snapshot.
- **App**: hovering works while a run locks the plan; connectors and exit zones add "· ~14 S" (`IEditorHost.RunHoverInfo`,
  `MainViewModel.RunHoverInfo`, `PlanEditor.RefreshRunHover` on the run timer).
- **Tests**: `Round010aTests` (5); diagnostics rows for each new switch plus "as r009 (no 010a rules)".
  **229 expected: 228 pass, 1 skipped** (wheelchair gate still parked, D-176).

### How to test

1. Build; run all tests (228 pass, 1 skipped). Paste `Round007Tests.Diagnostics_Round007Rules` output (new rows at
   the bottom of each block).
2. Demo PLAY. Hover the street stair and escalators while running: labels show "~N S" and change as crowds come and
   go. Hover an exit zone (platform door): same.
3. Street → concourse: when the stair gets busy, people approaching from the stair side peel off to the escalators
   before they reach the stair foot; when an escalator queues, people go back to the stair.
4. STRESS colouring: people stuck in a jam (e.g. a blocked stair foot) warm up after about half their patience even
   if they jostle; free walkers stay cool; commuters in short gate queues stay cool.
5. Profile summaries in LIBRARY show "sees 40 m" (commuter), "sees 20 m" (infrequent).
6. Save, reopen: format still v6; r009 files open unchanged.
7. Don't-regress list (HANDOVER §8).

### Judgement calls

- **Stair wait (D-181):** only people *stopped* near the entry (under 0.3 m/s within 5 m) are divided by the measured
  flow; everyone else heading for it, and everyone on the flight, counts against capacity. Dividing everyone near the
  entry by the flow made free-flowing stairs look queued. The flow is measured only while two or more are held at the
  entry, and clamped to 10–100 % of capacity, so the wait is never below D-111's load ÷ capacity (see r010a-2).
- **Exit crowd delay (D-182):** people in a box 3 m around the exit zone, over 0.3 p/m², ÷ (1.3 p/s × the zone's longer
  side). Counts everyone there, not only those heading for that exit.
- **Approach review:** only walking agents (not queueing, being served or dwelling); the exit is looked at first and,
  if it changes, the route is re-chosen with the usual noise. The old near-entry rule (with the switch off) also
  respects sight, so the diagnostics row isolates the trigger.
- **Sight** is a straight line to the connector's entry edge or the exit zone's box, on the agent's floor only.
- **Stress (D-191):** pace smoothed over 2 s; a jump of more than 3 s of route cost in one step (new target) restarts
  it without counting; queuers and lane waiters are always held up (before, only when slower than half speed).
- **Hover while running** also shows the hover outline; clicks still say RESET TO EDIT.

### Accepted defaults

- Sight 30 m (commuter 40, infrequent 20); review every 3 s; stay bonus 8 s; exit box 3 m; free density 0.3 p/m²;
  exit flow 1.3 p/s/m; pace smoothing 2 s, held below 0.5.

### Known limitations

- Unbuilt in the sandbox: compile errors are possible; behaviour untuned.
- Sight ignores walls (radius only, D-179's pick).
- Device choice and dwell zones are unchanged (D-178).
- A two-way stair's wait is one number for both ends.
- Weighted-sink rules ignore exit crowding by design.

### Watch list

- Escalator queues growing too long now that stairs look slower when busy (the r007o clogging).
- Ping-pong between stair and escalator (the test checks fewer than 2 switches per person).
- Stress now rising in places it didn't (long ticket queues, jams at the gate line): check STRESSED in the status bar
  and the diagnostics' stress columns.
- Hover label cost: one dictionary lookup per refresh.

### Open questions (non-blocking)

- Should the exit crowd also feed train door choice in 010b exactly as is, or with a door capacity?
- D-176 (wheelchairs at the accessible gate) is still parked.

### r010a-2 (after Gavin's r010a build)

- r010a compiled. `PackedStair_MorePeopleTakeTheFreeEscalator` failed: escalator share 47 % with the 010a rules vs
  61 % as r009. Cause: the measured stair wait left out people on the flight unless it was badly packed (a
  "flight slack" of 1.5 × the nominal ride), while D-111's load ÷ capacity counted every rider. A busy stair looked
  quieter than before, so more people took it.
- Fix: riders count against capacity again and the flight-slack term is gone (`FlightSlack` removed). Stopped queuers
  ÷ measured flow (≤ capacity) + everyone else ÷ capacity is never below the old formula and grows when a held queue
  boards slowly.
- **Unbuilt.**

### r010a-3 (after Gavin's r010a-2 build)

- r010a-2: escalator share 61 % with the 010a rules and 61 % without. The test layout never saturates the stair (about
  47/min of 75/min), so the stair wait is the same either way and nobody gains 8 s by switching: the two runs are
  the same. The test was asking for a difference this layout can't produce.
- The test is now `StairAndEscalator_NewRulesDontPushPeopleOntoTheStair`: share no worse than 2 points below r009,
  and fewer than 2 switches per person.
- Diagnostics gain two blocks: the same layout, and a **counterflow** case (120/min up, 60/min down the stair, which is
  the only way down). There a queue should form at the stair foot and the measured flow should drop, which is where
  the new stair wait should send more people to the escalator. Every row now has a `use` column: people per minute
  stepping onto each connector and its current wait. Once that output is in, we decide whether the counterflow case
  becomes a pass/fail test and whether anything needs tuning.
- Built by Gavin 2026-10-26: `StairAndEscalator_NewRulesDontPushPeopleOntoTheStair` passes.

### r010a-3 diagnostics (Gavin, 2026-10-26)

- `StairAndEscalator_NewRulesDontPushPeopleOntoTheStair` passes; full test count not yet confirmed.
- Demo with journeys: 010a rules on vs as r009: arrived 297 vs 293, queuing 13 vs 26, 0 stuck either way. Small gain.
- Stair + escalator and counterflow: identical with and without the 010a rules. In the counterflow case both are at
  capacity (escalator 94/min of 100, stair 75/min of 75; demand 180/min), so there is nothing to choose between;
  it stays a diagnostics block, not a test.
- Stress 0 nearly everywhere (counterflow peak 0.12 with progress stress, 0.08 by speed): the half-patience
  threshold, not the measure. → D-193, D-194.
- Watch: in the demo the platform stair carries 42/min with a ~20 s wait while the platform escalators run 17–20/min
  with 4–5 s waits. Could be landing corner-cutting (D-192) slowing the stair, the stair's wait shown being an end of
  run snapshot, or the down escalator's detour. Look again after 010b's landing waypoints.

---

## 010b as built

### What changed

- **Core**
  - `Model/TrainService.cs` (new, immutable record): name, tag, door zone ids, headway, first arrival (offset),
    doors-open time (dwell), people getting off per train and over how long, places (capacity), optional load
    curve; `Matching(study, tag)`, `Describe()`. `Study.TrainServices`, `FindTrainService`, `TrainServiceOfDoor`.
  - `JourneyStepKind.BoardTrain` (appended), `JourneyStep.BoardTrainStep(tag)`, `EndsJourney` (exit or board train).
  - Validator: doors exist, are sink zones on one level, belong to one service; dwell ≥ 10 s; headway ≥ dwell + 10 s;
    warnings for doors no one can get off at; Board train steps need a matching service.
- **IO**: format **v7** (`UpgradeV6ToV7` makes sure the list exists); `Save_WritesFormatV7`. Demo: Southbound
  (from 0:30) and Northbound (from 3:30), every 6:00, doors 45 s, 150 off over 20 s, 300 places (D-189).
- **Sim**
  - `SimTrain` (new) compiled by `SimSetup.BuildTrains`: door sinks, door centres, the doors' emitters (marked
    `SimEmitter.TrainService`, so they don't emit on their own), platform direction (the side of the doors with
    floor), the box on the other side (3 m deep, 3 m past the end doors).
  - `Engine/SimEngine.Trains.cs` (new): each service's clock, releasing people off through the doors in turn,
    boarding from 75 % of the getting-off time, capacity, LEFT BEHIND at departure; `AgentPhase.WaitTrain` with a
    waiting spot beside the door; Board train steps pick the quickest door with its crowd (D-182).
  - Exit and Board train arrivals at a door board only while it can take them, else wait.
  - Landing waypoints (D-192): `AgentStore.JointDepth`; `LandingCarryDepth` from the landing's depth, the turn
    direction (from the field) and the keep side.
  - Stress: `StressFrom` linear (D-193; old curve `StressFromHalfPatience`); local density `AgentStore.Crowd` from
    neighbours within 1 m; held up above `CrowdStressDensity(tolerance)` = 1 + 3 × tolerance p/m² (D-194).
  - `SimTuning.LandingWaypoints`, `EarlyStress`, `CrowdStress`; `SimStats.LeftBehind`; runner publishes train states.
- **App**: STUDY ▸ TRAINS (ADD SERVICE from selected sink zones; per service name, tag, every, first train, doors
  open, getting off, getting-off time, places, SET DOORS, DELETE; all undoable). Journey editor: "Board a train" with
  service tag suggestions. Status bar LEFT BEHIND when the run has trains. Plan overlay: grey train box while in, doors
  lit yellow (getting off) then green (boarding).
- **Tests**: Sim `Round010bTests` (4), IO `Round010bFileTests` (3); `StressTests` updated for D-193 (the old curve
  is tested as `StressFromHalfPatience`; free-flow stress < 0.1); `Save_WritesFormatV6` → `V7`; diagnostics rows
  for the three new switches, train counts on every row, "as r009" now turns off every round 010 rule.
  **236 expected: 235 pass, 1 skipped.**

### How to test

1. Build; all tests (235 pass, 1 skipped). Paste the diagnostics output.
2. New from Demo: CHECK 0 errors, 0 warnings. STUDY ▸ TRAINS shows Southbound and Northbound.
3. PLAY: before 0:30 people gather beside the southbound doors and don't vanish into them. At 0:30 a grey train box
   appears south of the platform, doors light yellow, ~150 people step off; a few seconds later the doors turn green
   and the waiting people board; the box goes at 1:15. Northbound the same from 3:30 on the north side.
4. Status bar: LEFT BEHIND stays 0 in the demo (300 places, about 180 boarders a train).
5. Edit Southbound's places to 20: PLAY: LEFT BEHIND counts people who didn't fit; they board the next train.
6. ADD SERVICE with nothing selected: ticker asks for door zones. Select two door zones, ADD SERVICE: CHECK reports
   the doors belong to two services. Undo.
7. Journey editor: duplicate "Ticket then gates", change the exit step to "Board a train", tag Northbound; use it on
   West entrance: those people only wait at northbound doors.
8. Stairs: on the platform switchback, people going down keep to their half through the landing (outer stream goes
   deep, inner turns early). Diagnostics row "no landing waypoints" compares.
9. STRESS colouring: people held in queues or crowds warm up within seconds now; STRESSED counts people past half
   their patience as before.
10. Save, reopen: format v7, services and Board train steps round-trip; r010a (v6) files open with no services.
11. Don't-regress list (HANDOVER §8).

### Judgement calls (D-195)

- The doors' own emission rules say who gets off (mix, journeys, destinations); their rates are relative shares and
  they don't emit between trains. No separate alighting editor.
- A door is a sink only while its service is boarding, so any exit into a door waits for a train (the demo's
  journeys work unchanged); the Board train step is for picking a service by tag.
- Boarding opens at 75 % of the getting-off time; people walk to the door and board until the places run out.
- Waiting spots: 1.2–2.7 m in from the door, 0.5–1.7 m past its end on a random side.
- Left behind: everyone waiting for that service since before the doors closed (counted again if left twice).
- Train box and doors drawn in the plan overlay only; 3D shows people but no train yet.
- Landing waypoints: outside of the turn carries to 0.5 m + body radius short of the far wall; inside turns at 35 % of
  the depth; straight landings and no keep side keep the 0.6 m carry.
- Crowd density counts neighbours within 1 m on the same height band.

### Known limitations

- Unbuilt in the sandbox: compile errors are possible; behaviour untuned.
- No pass/fail test for landing waypoints yet; the diagnostics row is the check.
- Train doors on one level only; no train shown in 3D; no per-door capacity.
- Door zones' rate curves are ignored for alighting (the service's own load curve applies).
- Waiting people don't re-pick a quieter door while waiting.

### Watch list

- People getting off meeting boarders who start walking to the doors at 75 %.
- Platform stair and escalator queues after each train (150 people at once) and the gate line surge.
- STRESSED counts with D-193/D-194 in long ticket queues and on busy stairs.
- Landing waypoints pinning people against a landing wall in counterflow.

### Next

Gavin's 010b build results first. Then: the parked wheelchair gate (D-176), profile editing (sight, effort, device
preferences), 3D lifts (M4) or results/heatmaps (M5), whichever Gavin picks.

### r010b results (Gavin, 2026-10-26)

- Builds and runs; everything else works. The demo's concourse gate line gridlocks after a train: crowds on both
  sides at once (screenshot). → D-196: per-lane gate directions (in / out / both) and a lighter demo (2 in + 2 out,
  100 off per train over 30 s).

### r010b diagnostics (Gavin, 2026-10-26) and r010b-2

- Demo with journeys, 300 s: 600 in, 182 out, 0 stuck, 418 still in the study; gate wait 20 s (n 410); trains boarded
  33, left behind 0; STRESSED 20 (stress mean 0.18). As r009 rules: 202 out, 51 boarded. The gate line is the
  bottleneck (no stuck removals, so a slow lock rather than people removed).
- Switching single rules off moved the demo by about ±10 % (landing waypoints off: 202 out, 53 boarded); runs diverge,
  so watch rather than act. "No landing carry" matched "all on" because the waypoints carry anyway; that row now
  turns both off.
- D-194 (crowd stress) barely shows (mean 0.17 vs 0.18): densities rarely reach 2.5 p/m² with 1 m counting.
- **r010b-2 (D-196), unbuilt:** `GateLaneDirection` (Both / FromFront / FromBack) and `Device.LaneDirections`
  (additive, still v7); fixed lanes keep their side for the run, never idle-reset or close, don't count as turning
  for the other side, and devices whose lanes all run the other way are skipped in device choice. Device panel:
  "Lane directions" text (both / front / back per lane). Demo: gate line back, back, front, front (it faces east,
  so the street side is the back); trains 100 off over 30 s. Tests: demo lanes and round-trip assertions added to
  `Round010bFileTests` (no new test methods: **236 expected, 235 pass, 1 skipped**).

#### How to test (r010b-2)

1. Build; all tests. Paste the diagnostics output (demo rows: out, boarded, gate wait).
2. Demo PLAY past 0:30: after the train, the outbound crowd queues at the two east-side (front) lanes; people coming
   in use the two west-side (back) lanes and the accessible gate; no two-way lock.
3. Select the gate line: SELECTION ▸ QUEUE shows "Lane directions: back, back, front, front". Type `both` (missing
   lanes fill with both), undo; type `sideways`: rejected in the ticker.

### r010b-3 (after Gavin's r010b-2 run)

- r010b-2 runs; the jam moved to the way in: the two inbound lanes can't take 60/min plus ticket buyers (screenshot:
  crowd on the street side of the gates). → D-197: a second gate line south of the first (barrier gap 12–16 → 8–16,
  plus the accessible gate at 16.8–18), lanes back, back, both, both; the measurement zone covers both lines.
  `Round010bFileTests` checks both lines. **Unbuilt.**
- Watch: each line's hold points sit 1 m past its ends, so the north line's southern hold point is in front of the
  south line's top lane (and vice versa). Fixed lanes make waiting for a lane rare, but if waiters block a lane mouth,
  hold points should skip other devices' lane mouths.

### r010b-3 result (Gavin, 2026-10-26)

- Moving the gates lower on the concourse fixed the jam; everything else works. Round 010 closed. Next round: Gavin's
  pick (wheelchair gate D-176, profile editing, M4 lifts, M5 heatmaps); move the demo's gate lines to match his layout
  if he wants it as the default.

### r010b-4 (final)

- D-198: demo gates moved 4.5 m south to match Gavin's layout; D-197's second line removed. Unbuilt (two-line test
  assertion reverted to one line). **236 expected: 235 pass, 1 skipped.**
- D-199: demo platform stair and escalators moved to Gavin's layout (stair (42.6, 15.8), escalators (36, 12.0)),
  read off a screenshot. **Unbuilt**: first thing next chat, confirm CHECK is clean and the demo still runs.
