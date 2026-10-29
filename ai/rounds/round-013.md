# Round 013: lifts (M4)

**Status:** 013a built 2026-10-29 and confirmed by Gavin's build (one test fix, below). **013b built 2026-10-29,
unbuilt in sandbox** (no .NET SDK, NuGet blocked; statically reviewed: bracket balance, XAML well-formed, CRLF).
**281 tests expected** (274 + 7). **Format v9** (train shapes additive). Round 013 is complete.

## Decided

| # | What |
|---|---|
| D-236 | A lift is added inside an existing shaft from a lift preset; served levels = the shaft's levels minus its no-stop levels. |
| D-237 | Banks of 1–4 cars; front and/or rear doors per served level: entry / exit / both / closed, each with its landing. |
| D-238 | Dispatch: Collective (default), Shuttle, Up-peak, time-switched by a schedule. |
| D-239 | Landings: loose crowd, board in arrival order, wheelchair users first at accessible-priority lifts. |
| D-240 | 013a sim + placement + plan symbols + format v9; 013b 3D cars with riders. No further splits. |
| D-241 | Landings are automatic rectangles in front of each door side (edge width × depth, default 2.5 m). |
| D-242 | Front edge picked per lift (default the first drawn edge); rear = the edge most nearly opposite; cars side by side along the front. |
| D-243 | Car space by body size: places = (radius ÷ 0.25 m)². |
| D-244 | Lobby level per lift (default lowest served); Up-peak returns there; Shuttle runs lowest ↔ highest. |

## 013a as built

**Core**
- `Model/Lift.cs` (+ `LiftDoorUse`, `LiftLevelDoors`, `LiftScheduleEntry`), `Shaft.Lift` (cloned in `CopyFrom`, so undo
  snapshots don't share it), `Study.LiftStops(shaft)`, `ConnectorKind.Lift` (never a connector preset; the sim's kind).
- `Geometry/LiftLayout.cs` (+ `LiftSide`, `LiftCarLayout`): front/rear edges, car footprints, a door per car per side
  (≤ 1.1 m, 80 % of the car width), landing rectangles. Shared by the sim and the plan symbols.
- `Editing/LiftFactory.cs`, `StudyTemplates.FindLiftTemplate / AvailableLifts`, CHECK: fewer than two door levels,
  missing preset, front edge off the outline (warnings), shuttle with more than two stops (info).
- IO: format **v9** (`UpgradeV8ToV9` is a no-op: older shafts have no lift).

**Sim**
- Nav: each lift bank is a `NavConnector` (kind Lift, step-free, no pieces) with a landing `NavPortal` per stop and door
  side (`LiftStop`, `LiftSide`, `IsLiftLanding`; floor cells = the landing's walkable cells). Route options pair every
  entry landing with every exit landing on another stop; the ride is timed (`LiftTiming.TravelS`, trapezoidal) plus a
  fixed part: half the usual interval between cars (the uncrowded wait), doors and getting on/off. Lift-only profiles
  can use lifts. A lift's ride and wait are weighted by the profile's **lift preference** like the mechanised
  preference (D-138). Shuttles only get landings at their two end stops.
- `SimLift` (`SimSetup.BuildLifts`): preset values, stops, landing portals, car layout, lobby, schedule.
- `SimEngine.Lifts.cs`: cars with Idle / Moving / Opening / Open / Closing (`LiftCarStatus`); hall calls counted from
  the people waiting each step and assigned to the car with the soonest estimated arrival; collective stopping
  (nearest stop ahead with a rider getting off or a call its way, else the farthest call ahead, then turn); Up-peak
  returns empty idle cars to the lobby; a schedule switches Collective / Up-peak by time of day. Doors open: people get
  off first, then on, one per 0.8 s per car; doors stay open 1.5 s after the last one and at least the preset's dwell.
  Boarding: going the car's way (any way when it has none; the first on sets it), fitting the places left (D-243;
  anyone fits an empty car), wheelchair users first at accessible-priority lifts, then arrival order.
- People keep their agent slot while riding (`AgentPhase.LiftRide`): held at a spot in the car's footprint, carried at
  the car's height (snapshot Z), skipped by movement, forces, density, footfall and zones. Getting off puts them just
  past the door on the new floor (walk-clear applies at exit-only doors) and they choose their route again.
  Waiting (`AgentPhase.LiftWait`) starts on reaching the landing cells; they stand at a random spot on the landing
  (≥ 0.8 m from the door when possible), counted as held up for stress.
- Route choice's live wait for a lift: people waiting ÷ the bank's places per round trip × round trip.
- `SimStats.AtLifts`; the runner publishes car states (`CopyLiftCarStates`).

**App**
- SELECTION ▸ shaft ▸ LIFT: preset (No lift removes it), name, cars, front edge, landing depth, dispatch, lobby,
  schedule rows (24 h time + Collective/Up-peak, ADD ROW, ✕), doors per served level (front · rear: IN + OUT / IN /
  OUT / —; rear disabled without a rear edge). All undoable.
- Plan: landings dashed, car outlines, doors in use at the active level as thick ticks. While running: a car at the
  active level is filled (green while its doors are open); cars show ▲/▼/■ and riders. Hovering a shaft shows its lift
  wait. Status bar: AT LIFTS while the study has lifts.

**Tests** (+9): `Round013Tests` (layout; wheelchairs ride a lift to the upper level with ≤ 4 per car; commuters use it
as the only way up; cars start at the lobby; schedule; CHECK warning; no lift = no lift model), `Round013FileTests`
(v9 round-trip; shaft clone copies the lift).

## How to test

1. Build; all tests pass (274). Demo loads with 0 errors, 0 warnings (its shafts have no lifts yet).
2. New from Demo. Select "Accessible lift shaft" (platform ↔ concourse). SELECTION ▸ LIFT ▸ preset "Accessible / goods
   lift". Its south side is edge 1 (the door side the core walls leave open). Dashed landings appear on both levels.
3. CHECK: no lift warnings. PLAY. Wheelchair users heading between platform and concourse wait on the landing, the car
   (■/▲/▼ + riders) comes, doors go green, they get off on the other level and carry on. Status bar shows AT LIFTS.
4. Hover the shaft while running: "~N S" is the lift's current wait.
5. RESET. Cars = 2, then front edge another side; the plan symbols follow. Add rear doors where there's a rear edge.
6. Dispatch Up-peak with lobby = concourse: idle cars go back to the concourse. Add schedule rows (e.g. 07:00 Up-peak,
   07:15 Collective) and watch the behaviour switch at those times.
7. Doors: set the platform front to OUT only; people can only get off there (no one waits on that landing).
8. Undo/redo every lift edit; save, reopen (format 9); open a v8 study (no lifts).
9. 3D view: riders are drawn at the car's height inside the shaft (the cars themselves come in 013b).
10. Don't-regress list (HANDOVER §8).

## Judgement calls

- Lift preference weights lifts like mechanised preference weights belts (1 − 0.4 × preference).
- Schedule rows offer Collective and Up-peak only; a Shuttle is a lift's fixed mode and ignores the schedule.
  Destination dispatch (in the preset enum) runs as Collective.
- Anyone fits an empty car, so an oversized person can't block a lift for ever.
- A car with riders that has just closed its doors doesn't reopen for calls at the same stop (they were full out or
  going the other way); it delivers its riders and comes back.
- Landing door width ≤ 1.1 m (80 % of the car width when narrower).
- The demo isn't given a lift (it would change the wheelchair routes the demo tests rely on); see open questions.

## Accepted defaults

- Lift preference weighting as above (cheap to change).

## Known limitations

- 013b: no 3D car boxes yet; riders float at car height in the shaft.
- No giving up at lift landings (patience doesn't apply there yet); lift waits aren't in the journey Long wait.
- Calls are assigned once (re-assigned only when a car closes its doors there); a far car may keep a call while a
  nearer one becomes free.
- Riders of a car share one doorway stream per car (0.8 s each); front and rear doors don't double it.
- No `SimTuning` switch for lifts yet.

## Watch list

- Doorways blocked by the waiting crowd: people step out up to 1.7 m from the door; if all three spots are taken the car
  waits with its doors open.
- Long landings in busy banks: the loose crowd stands where it lands, so later arrivals may wait behind earlier ones.

## Open questions (non-blocking, for 013b)

- Put the accessible lift in the demo now (platform ↔ concourse), and the goods lift (platform ↔ street, no
  concourse stop) as staff-only?
- Patience at lift landings: give up and take the stairs (or leave) like device queues?
- 3D: car boxes with open/closed doors, and landing doors in the shaft walls?

## Next

013b: 3D moving cars with riders visible (D-240).

## 013b queries answered (D-245–D-250)

- Gavin's r013a build: all good except `ThrongFileTests.Save_WritesFormatV8` (expects 8, now 9) - fix first.
- He couldn't add a lift: clicking a LIFTS library row says "Agent profiles, lifts and schedules are set up in later
  milestones". The way in is SELECTION ▸ shaft ▸ LIFT; 013b makes the library row add the lift to a selected shaft
  (or say where to do it).
- 013b scope (one build): 3D lift cars with riders; demo lifts (new street ↔ concourse accessible shaft, lift in the
  concourse ↔ platform shaft, goods shaft removed); wheelchair users board first at every lift and others treat a lift
  with a wheelchair user waiting as +60 s (D-247, D-248); a drawn train outline per service (D-249); an office tower demo
  (D-250).

## 013b as built (D-245–D-251)

**r013a fixes**
- `ThrongFileTests.Save_WritesFormatV9` (was V8, expected 8).
- LIFTS library: clicking a lift preset with a shaft selected puts it in that shaft (or swaps the preset of the lift
  already there), undoable, and switches to SELECTION; with no shaft selected the ticker says where lifts go. Dropping
  a lift preset onto a shaft in the plan selects that shaft and does the same.

**Accessible priority (D-247, D-248)**
- `RoutePlanner.HasLiftPriority(profile)`: needs accessible devices or lift/ramp only. Those people board first at
  every lift (was: accessible-priority presets only), and `NavRoutes.ClassOf` makes them step-free, so they keep to
  lifts and ramps even if a custom profile isn't marked lift-only.
- `SimEngine.LiftPriorityPenaltyS` = 60: while anyone with priority waits at a bank, `RoutePlanner.Choose` adds 60 s
  to that lift for everyone else, only when its landing is in their sight (D-179), not scaled by their lift
  preference. Passed to every route choice as `priorityPenaltyS`. Switch `SimTuning.LiftPriority` (off = r013a);
  diagnostics row "no lift priority".

**3D lift cars**
- `Render/Scene/{LiftCarDraw,LiftCarMeshes}.cs`, `SceneRenderer.SetLiftCars`: per car an opaque floor plate, translucent
  blue walls (a doorway gap while open) and outlines; door posts and head on each door side used at the stop, green
  while open, a centre line while closed. Drawn in 3D after the riders (walls without depth writes, so riders show
  through). Height 2.4 m, less where stops are closer than 2.8 m.
- `MainViewModel.FetchLiftCars3D` (new `MainViewModel.LiftCars.cs`) builds the list from `CopyLiftCarStates`, reusing
  it while no car moved, so meshes are only rebuilt when something changed; `ViewportView.LiftCars` feed.

**Train shapes (D-249)**
- `TrainService.Shape` (plan polygon, empty = automatic box); `SimSetup.BuildTrains` uses it as the train's drawn box.
- STUDY ▸ TRAINS ▸ service: "Train shape" line, DRAW TRAIN (arms the polygon tool for that service on its doors' level;
  close the polygon or double-click to finish, Esc cancels; `PlanEditor.Train.cs`) and AUTO (clears it). Undoable.
  Drawn shapes show dashed in the plan while editing; the running train uses the shape.

**Demo (D-246)**
- Concourse ↔ platform "Accessible lift shaft" gets "Platform lift" (Accessible / goods preset, doors south).
- New "Street lift shaft" (22–24.4, 11–13.4) concourse ↔ street on the unpaid side with "Street lift" and three core
  walls (south open), spanning both levels.
- Goods lift shaft removed.
- Flows: 2 % wheelchair users in (ticket then gates, holding a ticket; infrequent ticket line 20 → 18 %) and out of the
  train doors (commuters 98 / wheelchair 2).
- Train shapes: southbound 2–58 × 4.2–7 (south of the platform), northbound 2–58 × 23–25.8 (north of it).

**Office tower (D-250)**
- `IO/OfficeTowerBuilder.cs`, File ▸ New from Demo ▸ Office tower (the interchange keeps Ctrl+Shift+D). Ground lobby
  (4.5 m) + Levels 1–10 (3.6 m), 40 × 24 m floors, a 8.8 × 2.4 m shaft with four 13-person cars facing south, core
  walls, desks (sink) zones on every floor, street door spawn at 16/min Poisson on the AM peak curve, 97 % commuters
  and 3 % wheelchair users, run 08:00–09:00. Schedule: Up-peak from 07:30, Collective from 09:30. No stairs.

**CHECK:** a level a lift stops at counts as vertically linked ("…no stair, escalator, ramp or lift…").

**Tests** (+7): `Round013bTests` (priority profiles; penalty only for others and only in sight; wheelchair users'
share of waiting lower with priority; demo compiles two accessible lifts with wheelchair flows; office tower carries
people up with ≤ 2 stuck), `Round013bFileTests` (train shape round-trip and demo shapes beside the platform; office
tower structure, 0 errors / 0 warnings, schedule saved). Updated: `Demo_HasItsVerticalSet` (both shafts have lifts, no
no-stop levels), `Round013FileTests` (demo shafts now have lifts). Diagnostics: "no lift priority" row and an
OFFICE TOWER 300 s study.

## How to test (013b)

1. Build; all tests pass (281). Demo loads with 0 errors, 0 warnings.
2. LIBRARY ▸ LIFTS: click a preset with nothing selected → ticker explains. Select a shaft, click it → the lift is
   added (undo removes it). Drag a preset onto a shaft → same.
3. Demo: PLAY, 3D view. Cars sit in both shafts; when a wheelchair user arrives the car comes, doors go green, riders are
   visible inside the box, it moves to the other level. Compare plan symbols (unchanged from 013a).
4. Hover a lift while a wheelchair user waits: other people mostly take the stairs/escalators; anyone who does come
   boards after them.
5. STUDY ▸ TRAINS: both services show "Drawn (4 points)"; while running the trains sit outside the platform edges.
   AUTO → back to the automatic box; DRAW TRAIN on the platform level → draw a new outline; undo/redo; save, reopen.
   DRAW TRAIN from another level → "switch to the doors' level".
6. File ▸ New from Demo ▸ Office tower: 11 levels, CHECK clean. PLAY: cars start at the lobby, people board, cars stop
   at several floors, empty cars return to the lobby (up-peak). 3D shows the four cars moving with riders.
7. Change the tower's schedule start (STUDY ▸ RUN start 09:45) → collective behaviour (idle cars stay where they are).
8. Don't-regress list (HANDOVER §8).

## 013b judgement calls (D-251)

- Priority covers lift/ramp-only profiles as well as "needs accessible devices", and both route step-free.
- The 60 s penalty only counts with the lift in sight and isn't scaled by lift preference (it's other people's turn,
  not the ride).
- Lifts count as vertical links in CHECK (the tower would warn on every floor otherwise).
- 3D car height 2.4 m; translucent walls drawn after the riders.
- Train shape is drawing only and additive within v9 (like D-169/D-231); older v9 files load with the automatic box.
- Demo street lift position and the 2 % wheelchair share; the tower's demand (16/min AM peak, 3 % wheelchair).

## 013b known limitations

- No landing doors in the shaft walls in 3D; car doors are posts and a head, not sliding leaves.
- Train shapes are drawn in the plan overlay only (as in 010b), not in 3D.
- Still no giving up at lift landings; lift waits aren't in the journey Long wait (013a list).

## Open questions (for round 014)

- Patience at lift landings: give up and take the stairs (or leave) like device queues?
- 3D: landing doors in the shaft walls and sliding car doors?
- Destination dispatch (in the preset enum, runs as Collective).
- Fire stairs in the office tower (lifts only for now, per D-250)?
