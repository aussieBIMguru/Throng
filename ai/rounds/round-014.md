# Round 014: lift finish-off (M4)

**Status:** built 2026-10-30, **unbuilt in sandbox** (no .NET SDK again; statically reviewed: bracket balance, CRLF,
a second read of every lift-engine change). **288 tests expected** (281 + 7). **Format v9** (no change). One build, no
splits (Gavin).

Gavin's 013b build: clean, 281 pass. Nothing to fix first.

## Decided (D-252–D-258)

| # | What |
|---|---|
| D-252 | Scope: lift finish-off in one build; the 013b re-review fixes go in. |
| D-253 | Patience at landings: after the profile's patience, re-choose with that bank barred; if there's no other way (lift-only people), keep waiting. Landing waits count toward Long wait. |
| D-254 | The bar lasts until they've left that level by another way; if the bank is their only route after all, the bar lifts and they go back. |
| D-255 | Destination dispatch: each person is given a car grouped by destination and stands beside that car's door; cars stop only for their own riders and people. |
| D-256 | Destination is offered in schedule rows too. |
| D-257 | Office tower: one switchback stair beside the core, every floor, open to everyone. |
| D-258 | 3D: centre-opening sliding leaves on car and landing doors, timed by the preset's open/close; landing doors open only where a car stands. |

## 013b re-review (done at the start of this round)

The lift engine held up: off before on, collective stopping, call assignment, priority boarding and the 60 s penalty
match D-236–D-248. People waiting or riding are never removed as stuck or re-routed, so a car can't cycle its doors
for a rider who vanished. Found and fixed (D-252):

1. **LIFTS library click wasn't run-locked**: with a shaft selected, clicking a preset mid-run changed the lift through
   the undo stack. Now the ticker says RUNNING ▸ RESET TO EDIT.
2. **Dropping a lift preset off a shaft** used whichever shaft was selected before, and dropping it outside the plan
   overwrote the ticker with "Switched to plan ▸ click to place it". Now only a drop onto a shaft does anything;
   otherwise the ticker says to drop it onto a shaft.
3. **Neighbour hash rebuilt every step** for doorway checks whenever a study had lifts; now built once per step, and
   only when someone actually gets off.
4. **3D cars ignored level visibility**; they now hide with their level (same height bands as the riders).

Left as watch items: someone whose landing has no usable exit stop stands on the landing until removed as stuck; two
cars letting people off onto the same landing in one step can overlap them slightly.

## As built

**Patience at landings (D-253, D-254)**
- `AgentStore.LiftBar / LiftBarFloor / LiftGiveUpAt`; `SimEngine.TryLeaveLift` (from `UpdateJourney`, LiftWait phase):
  at `LiftGiveUpAt` (start of wait + profile patience) it tries `RoutePlanner.Choose` with the bank barred. A way found:
  counts in `SimEngine.LiftGaveUp(lift)`, notes the wait (Long wait), walks that way with the bar set on the current
  floor. None: keeps waiting and looks again after another patience.
- `RoutePlanner.Choose(…, barredConnector)`; every engine route choice passes `Barred(i)`, which clears the bar once
  the person is on another surface (a stair, an escalator, another level). `Decide` lifts the bar and chooses again if
  the barred choice finds no way (D-254).
- Boarding notes the landing wait too (`NoteWait`), so journey stats' Long wait counts lift waits.
- Switch `SimTuning.LiftPatience` (off = r013b); diagnostics row "no lift patience".

**Destination dispatch (D-255, D-256)**
- `SimLift.DispatchAt` no longer maps Destination to Collective; `LiftFactory` and the preset swap keep a preset's
  Destination. SELECTION ▸ LIFT dispatch offers "Destination (assigned cars)"; schedule rows offer "Destination".
- Engine: the mode per bank is worked out once per step (`_liftMode`). Under Destination, `CountHallCalls` counts the
  people already given each car (`_destCalls` per stop and way, `_destExit` per exit stop, `_destPlaces`) instead of
  landing calls; `AssignDestinations` gives each new waiter the car with the lowest `CallEta` + 10 s per stop they'd
  add (boarding, getting off) + 120 s if they wouldn't fit with those already given it, then `AimBesideCarDoor` sends
  them beside (not in front of) that car's door. Only people given a car board it (`FindBoarder`); a car opens for
  them only if one can board now (way and room), so it can't cycle. When a car closes, its people left at that stop
  are given a car afresh (`ReleaseDestinationWaiters`). A schedule switching away from Destination drops given cars.
- Switch `SimTuning.DestinationDispatch` (off = runs as Collective); diagnostics row "no destination dispatch".

**3D sliding doors (D-258)**
- `SimEngine.LiftDoorShare(car)` (0 shut … 1 open, ramped over the preset's open/close times); the runner publishes it
  (car stride 5) and `SimRunner.CopyLiftDoorShares()` hands it out.
- `Render/Scene/{LiftDoorDraw,LiftSceneDraw}.cs`: a door is two centre-opening leaves that slide apart. `LiftCarMeshes`
  now leaves the door openings out of the car walls (the leaves close them) and frames them (green while opening/open).
  `SceneRenderer.SetLiftCars(LiftSceneDraw?)`, `ViewportView.LiftCarFeed` returns the scene.
- `MainViewModel.FetchLiftCars3D`: car leaves just inside the car, landing leaves just outside the shaft wall in front
  of every car door at every stop and side with a landing, open only where that car stands. Rebuilt every frame while
  any door is sliding, otherwise only when a car moves, changes state or a level is shown/hidden.

**Office tower stair (D-257)**
- `OfficeTowerBuilder.BuildStair`: "Tower stair", the 1.5 m switchback preset from the ground lobby to Level 10, bottom
  landing at (28, 7) running east, returning on the north side; east of the core, south of the desks.

**Tests** (+7): `Round014Tests` (a barred lift leaves no way up; lift-only waiters never give up; destination gives
every waiter a car and people arrive, with the switch on and off; Destination in a schedule row; door share 0 idle /
1 open / part-way while sliding), `Round014FileTests` (tower stair ground → top, open, east of the core; Destination
round-trips in the lift and its schedule). Updated: `OfficeTower_LoadsClean_WithAFourCarBank` (1 connector), the
Round007 diagnostics list (+2 rows).

## How to test

1. Build; all tests pass (288). Demo and Office tower load with 0 errors, 0 warnings.
2. Run the demo; while it runs click a LIFTS preset with a shaft selected → ticker RUNNING ▸ RESET TO EDIT, nothing
   changes. RESET. Drag a lift preset onto empty floor → ticker says drop it onto a shaft; nothing added. Onto a shaft
   → the lift goes in (undo removes it).
3. 3D, demo running: cars have grey sliding leaves; landing doors stand in front of every car door at every served level.
   When a car arrives, its car leaves and the landing leaves at that floor slide apart together, frame goes green,
   then slide shut. Landing doors at the other floor stay shut. Hide a level in LEVELS: its landing doors and any car
   at it disappear with its people.
4. Office tower: CHECK clean; "Tower stair" east of the core on every floor. PLAY: most go up by lift; some short
   trips (Levels 1–2) use the stair. 3D: four cars with sliding doors.
5. Office tower, SELECTION ▸ Lift core ▸ dispatch "Destination (assigned cars)", delete the schedule rows. PLAY: people
   reaching the lobby walk to one car and stand beside its door; cars fill by destination and stop at fewer floors.
   Add a schedule row 08:15 Collective → behaviour switches back at 08:15.
6. Patience: in the tower (collective, busy), people who've waited past their patience leave the lobby for the stair.
   Wheelchair users never leave. Journey results: lift waits now count toward Long wait.
7. Save, reopen: Destination in the lift and in a schedule row survives. Undo/redo dispatch edits.
8. Diagnostics test output has the rows "no lift patience" and "no destination dispatch".
9. Don't-regress list (HANDOVER §8).

## Judgement calls

- A give-up isn't a journey "gave up" (they carry on another way); it's counted per bank in `LiftGaveUp` only (not yet
  in the RESULTS tab).
- After giving up, people take the best other way even if it's a long stair climb; no comparison against staying.
- No other way → look again after another full patience (not every step).
- Destination costs: 10 s per added stop, 120 s if they'd overfill the car; a waiter is only given a car with a door on
  their landing's side.
- People given a car stand beside its door (0.4–1.4 m out, past the door's edge) so the doorway stays clear for people
  getting off.
- Car walls now always leave the door opening out (the leaves fill it), instead of only while open.
- Leaves sit 5 cm inside the car / outside the shaft line; door height 88 % of the car height, on landings too.
- Tower stair at (28, 7), running east, returning north.

## Accepted defaults

- None beyond the judgement calls (all cheap to change).

## Known limitations

- Lift give-ups aren't in the RESULTS tables or CSVs yet.
- Destination dispatch has no landing keypad or car indicator in the plan; the grouping shows only by where people
  stand and where cars stop.
- Landing doors are drawn as leaves only; the shaft walls themselves still aren't drawn in 3D.
- A busy collective bank with an open stair loses people to the stair once waits pass patience, even for long climbs.

## Watch list

- Destination with one car: everyone gets the same car; fine, but the 10 s stop penalty does nothing there.
- Doorway blocking (013a): people standing beside the door should leave the door clear; check with dense lobbies.
- The 013b watch items above (no-exit landing, same-step overlap).

## Open questions (for round 015)

- Lift give-ups in RESULTS (a LIFTS table: waits, rides, gave up per bank)?
- Shaft walls in 3D (the landing doors currently stand on their own)?
- Next milestone: M6 views polish (section box, exploded levels, iso presets), the watch-list tuning (crowd stress,
  hold points, gate throughput), or M8 scale and replay?

## Next

Round 015, scope to be queried.
