# Round 006 — M2 part 1: movement core

**Status:** Built 2026-10-25, **unbuilt** (no .NET SDK or NuGet in the sandbox; statically reviewed only). All query
answers took the recommended option; one more question asked at the start of the build (D-110).
Base: r005 (built and ran; the only failure, a demo circuit-count test, was fixed in the r005 zip).

## Pre-round check (Gavin's notes)

- Placing from LIBRARY: double-click works (devices → PLACE, connectors → CONNECT); drag-and-drop was never
  decided, now D-098.
- Creating new definitions: library editor, planned for M3 (BRIEF §16, §22.5).
- Running studies: M2, this round and the next. Nothing built yet (`Throng.Sim` has only the clock and RNG;
  `WorldCompiler` emits no portals; no RUN toolbar group).

## Query record (answers → decisions)

| Topic | Answer | Decision |
|---|---|---|
| M2 split | Two rounds: 006 movement core, 007 full demand + demo end to end | D-095 |
| Demand model | Emission rules on spawn zones | D-096 |
| Editing during a run | Locked until RESET | D-097 |
| LIBRARY drag-and-drop | Drop starts the tool at the drop point | D-098 |
| 006 demand scope | Constant / Poisson, one profile, any/weighted sinks, saved (v4) | D-099 |
| Route choice | Fastest expected time + noise | D-100 |
| Devices before M3 | Obstacles; walk through pass-through lanes | D-101 |
| Agent look | Capsules in 3D, discs in plan, coloured by profile | D-102 |
| Run settings | RUN section on STUDY tab (start, duration/indefinite, seed) | D-103 |
| Live readout | Clock + basic counters in the status bar | D-104 |
| Keep side | Keep left default, study setting | D-105 |
| Bad runs | CHECK gates PLAY; stuck ring at 30 s, removed at 120 s | D-106 |
| Escalators | Belt speed + walkers + entry step-loading cap | D-107 |
| r005 open questions | Parked until after M2 | D-108 |
| Demo | Constant-rate flows across all three levels | D-109 |
| Back-of-house stair (asked at build start) | Staff-only flag; public agents never route on it | D-110 |
| Demo froze at the platform stair (asked after the first test run) | Light congestion term in route choice | D-111 |

## What was built

**Core**
- `EmissionRule` / `SinkWeight` (immutable records) on `Region.EmissionRules`; `Region.IsSpawn` / `IsSink`; copies get
  new rule ids. `StudySettings.KeepSide`; new studies start at 07:00 for 30 min.
- `Connector.StaffOnly` (D-110), copied by `Clone` / `CopyFrom`.
- `ConnectorPortals`: piece end edges, joints between pieces, floor ends (served or pass-over), one-way rules.
- `WorldCompiler` now emits portals: piece ↔ piece joints and floor ↔ piece ends on served levels, with
  per-direction flags (`Portal.AllowsAToB` / `AllowsBToA`, `ConnectorId`).
- `StudyTemplates`: resolves profiles and connector presets (study library, then built-ins; Commuter fallback).
- `StudyValidator` demand rules: a missing or non-sink destination, no chosen sink with weight > 0, or a nearest-sink
  rule with no other sink are errors; a zero rate, rules on a zone that isn't a spawn, or an unknown profile are
  warnings.

**IO**
- File format **v4** with `UpgradeV3ToV4` (rules lists made non-null). Demo: the back-of-house stair is staff only;
  each street entrance emits 16/min commuters + 4/min infrequent travellers to the 12 platform doors (equal
  weights); each door emits 3/min commuters to West 40 / East 40 / Bus 20.

**Sim** (`Throng.Sim`)
- `Nav/`: `NavSurface` (floor or piece grid), `GridRaster` (scanline, non-zero winding), `NavWorldBuilder` (floors
  from `WalkableArea`, Slow zones as slowness, chamfer wall distances; pieces with belt speed and throughput from
  the preset; joint and floor-end links and transfer segments; sinks), `NavCosts` (step times), `FieldBuilder`
  (reverse Dijkstra), `FlowField` (cost sample, bilinear gradient), `NavRoutes` (floor-side, connector-side and sink
  fields; Bellman–Ford remaining times per mobility class), `RoutePlanner` (D-100 choice with noise and stair
  correction).
- `Engine/`: `AgentStore` (SoA), `SpatialHash` (counting sort), `SimEngine` (demand, social force, keep side,
  walls, belts, transfers with the escalator token bucket, arrivals, stuck rule, snapshot), `SimStats`.
- `SimSetup` (validation → nav world → fields → per-rule spawn and reachability checks), `SimModel`, `SimRunner`
  (background thread, play/pause/step/reset/speed, double-buffered snapshot, auto-pause at the end),
  `DistributionSampler`, `DeterministicRandom.NextGaussian` / `NextExponential`.

**Render**
- `GpuAgents` + agent shaders: one instanced draw of a 12-sided body, domed head, front nub and a blinking stuck
  ring. `SceneRenderer.SetAgents(data, count)`: plan draws them last without depth; 3D after the solids with depth.

**App**
- `MainViewModel.Run.cs`: RUN toolbar group (PLAY/PAUSE, STEP, RESET, ×½ ×1 ×2 ×4 ×8 MAX), edit lock
  (`PlanEditor.IsLocked`, the draw strip, LIBRARY list and right panel disabled, undo/redo and Copy to Levels
  refused; the LEVELS list stays usable), CHECK gate with a findings dialog, status counters at 4 Hz, run clock,
  STUDY ▸ RUN (start, end, duration, seed + RE-ROLL, keep side). New/Open/Demo reset any run.
- SELECTION ▸ EMISSION RULES on spawn zones (`EmissionRuleViewModels.cs`; every edit replaces the list, one undo step;
  a built-in profile is embedded when first used). Connector panel: **Staff only**.
- LIBRARY drag-and-drop (D-098): `PlanEditor.DropAt`; the viewport accepts drops and feeds agents per frame
  (`AgentFeed`), filtered to the active level's height band in plan and to visible levels in 3D.

**Tests:** 163 methods (136 in r005): raster, fields, portals (straight, switchback, escalator, demo back-of-house
stair with and without exclusion), validator demand rules, v4 round trip, demo compile and reachability, corridor
arrivals, determinism and reset, Poisson rate, stair and escalator crossings, escalator cap, demo flows, snapshot.

## How to test

1. Build the solution; run all tests (163). Fix-first list for round 007 = whatever fails here.
2. DEMO ▸ CHECK: still 0 findings.
3. DEMO ▸ PLAY: the ticker reports surfaces, portals and 18 emission rules; agents appear at the three street
   entrances and the twelve platform doors.
4. 3D: commuters (cyan) and infrequent travellers (yellow) walk down the street stair and escalators, through the
   gate lanes, down the platform stair and escalators to the doors; door flows go the other way. Nobody uses the
   back-of-house stair.
5. Escalators: standers keep left, walkers pass on the right; a queue forms at a busy comb.
6. Plan (F2): switch levels in LEVELS; only that level's agents show, as discs with a facing nub.
7. Speed chips ×½ … MAX change the pace; STEP pauses and advances; the status bar counts AGENTS / IN / OUT /
   JOURNEY / STUCK and the clock runs from 07:30.
8. While running: drawing, SELECTION/LEVEL/STUDY fields, LIBRARY, undo/redo and Copy to Levels are refused
   ("RUNNING ▸ RESET TO EDIT"); level visibility still works. RESET unlocks and clears the agents.
9. Errors block PLAY: delete the platform stair and both platform escalators, PLAY ▸ the findings dialog lists the
   unreachable rules and editing stays unlocked.
10. Draw a spawn zone and a sink, add a rule (ADD RULE), switch to ChosenSinks, tick the sink, change rate and
    pattern; each edit undoes on its own. Save, reopen: rules, keep side and staff-only survive (format v4).
11. STUDY ▸ RUN: End = Indefinite hides the duration; RE-ROLL changes the seed; same seed = same run after RESET.
12. Drag a ticket machine from LIBRARY onto the plan: it lands at the drop point facing north. Drag a stair: CONNECT
    starts with its bottom landing at the drop point; aim and click.
13. Don't-regress (HANDOVER §8): old files open, shortcuts only with viewport focus, ghosted level below in plan,
    undo/redo everywhere, offline viewport panel if GL fails.

## Fixes after Gavin's first build (r006b)

Built and ran: two test failures (`Stair_CarriesPeopleUpALevel`: 2 stuck; `Demo_PeopleReachThePlatform`: 28
arrived). Diagnosed with a Python replica of the engine (scratch, not in the repo), which reproduced both exactly:

- **Agents stuck beside a stair's bottom corner:** the push-through near a portal aimed straight in, so anyone
  beside the edge pressed into the footprint side. Now `FlowField.PushThrough` heads for the nearest point of the
  edge (a body radius in from its ends), and crossing snaps sideways into the edge's span (±0.3 m tolerance).
- **Demo freeze:** every agent took the 1.5 m platform stair both ways (it's 11 m nearer the gates than the
  escalators) and the two crowds locked at its foot; the escalators sat empty. D-111 adds an expected wait per
  connector to route choice (people heading for or on it ÷ capacity). Replica: 260 arrived / 2 stuck in 5 min
  (was 28 / 103).
- `Connector.StaffOnly` property was missing (compile error, fixed earlier).

## Judgement calls

- **Stand side follows the keep side:** keep left = stand left, walk right (the Australian convention); keep right =
  stand right, walk left; no keep side = stand right. The decision text said "stand right / walk left".
- **Boarding only through the planned entry:** on a floor an agent crosses onto a connector only at the portal it's
  heading for, so nobody slips onto the neighbouring unit of an escalator pair by accident.
- **Gradient across piece joints:** cells past a grid edge are extrapolated from the two cells inside, so agents keep
  walking across flight ↔ landing joints instead of stalling at the edge (found in review).
- **Mode changes halve the stuck timer** (capped at 15 s) instead of zeroing it, so an agent that keeps re-routing
  is still removed eventually.
- **Spatial hash cells are 2.5 m** (the interaction range) with a 3 × 3 query, not 1 m cells.
- **Staff-only connectors are left out of the navigation entirely** until staff routing (M3); their cuts stay.
- Dropping a LIBRARY row on the 3D view switches to plan and waits for a click; a dropped device faces north (90°).
- STEP with no run compiles and advances one step (paused).
- Plan view shows agents in the active level's height band: from 0.5 m below its floor to 0.5 m below the next.

## Accepted defaults

- None this round: D-110 was asked and answered.

## Known limitations

- **Unbuilt:** expect compile fixes first, mostly in the new Sim and App files.
- Stairs set to Up or Down are still walked both ways (only escalators and travelators are one-way).
- Route choice has no congestion awareness (D-100); sinks absorb without limit; gates are walked through without
  stopping (D-101); lifts aren't simulated (M4); step-free profiles can only use ramps and travelators.
- Performance is untested beyond the demo; fields are rebuilt on every PLAY (no cache between runs).
- Spawning happens on floors only; a crowded zone holds arrivals (up to 500 per rule) until there's room.

## Watch list

- Escalator boarding in a dense crowd reaches about 45–55/min, under the 100/min cap: standers keep full personal
  space on the belt. Tighter belt repulsion helped a little in the replica; left for round 007 tuning.
- Piece ends that join nothing (a half landing's far end) have no wall force yet.
- Crowd crunches at connector feet where people stepping off meet people queueing to get on.

- Agents stalling at piece joints, comb edges or the top of escalators (transfer tolerances: 0.3 m before the
  line to 0.6 m past it, then up to 0.3 m further to find the floor).
- Wall forces at narrow gate lanes (0.9 m clear) and the 1.5 m platform stair.
- Agents near the stair openings on upper floors being pushed by the opening walls.

## Open questions (for round 007's Query phase)

1. Should stairs marked Up or Down be one-way for routing too? (Recommended: yes.)
2. Stand side: keep it tied to the keep side, or always stand right / walk left? (Recommended: keep it tied.)
3. In 3D, show agents on hidden levels? (Recommended: no, as built.)
4. Gavin's run (r006b): **nobody uses lifts** (expected: lifts are M4; the demo has shafts only and emits no
   lift-only profile). **Peak build-up: most take escalators and ignore nearby stairs.** Causes: (a) route choice
   prices every escalator rider at belt + 0.5 m/s, so standers' real ride time (belt only, about twice as long) is
   under-costed; (b) the D-111 wait uses the nominal 100/min cap, but the model boards only about 45–55/min, so
   escalator queues look shorter than they are; (c) agents choose once (spawn or new level) and never reconsider while
   queueing. Recommended for 007: cost belts per agent (stander vs walker, like the stair correction); base the wait
   on measured boardings per minute and people actually queued near the entry; let queueing agents re-choose every
   few seconds once their wait passes a threshold (a light balk). Decide whether the demo should emit some
   mobility-impaired agents before M4 (they'd have no route without lifts or ramps).

## Fix first in round 007 (Gavin's r006b run)

1. **Escalator pairs gridlock at both ends.** The up unit's exit sits 0.6 m from the down unit's entry queue (top),
   and the down unit's exit beside the up unit's queue (bottom). Queues spread over the neighbour's exit landing,
   riders can't step clear, the belt keeps feeding them in, and both units lock. Planned fix (validate in a Python
   replica first, then port):
   - **Keep-clear run-off zones:** about 2.5 m × unit width in front of every connector exit, a soft obstacle for
     everyone except agents who have just stepped off there.
   - **Walk clear first:** for about 1.5 s after stepping off, the desired direction is straight out of the exit.
   - **Hold boarding when the exit is jammed:** an escalator stops taking riders while its own exit landing is full.
2. **Escalator preference during build-up** (open question 4): per-agent belt cost (stander vs walker), wait from
   measured boardings and the queue at the entry, light re-choice for agents queueing past a threshold.
3. **Agents seen through floors** in 3D at about 8% brightness clutter busy levels; options: hide agents on levels
   below the one nearest the camera, fade them further, or a toggle.
4. Escalator boarding reaches only about 45–55/min in a crowd (cap 100).
5. Piece ends that join nothing (a half landing's far end) have no wall force.

Tip: the r006b diagnosis used a Python replica of the nav + engine with matplotlib plots of the jam; rebuilding one
is the quickest way to check runtime behaviour while the sandbox has no .NET SDK.

## Next

Round 007: full demand (rate curves, timetables, batches, profile mixes), train door flows, demo end to end, tuning.
