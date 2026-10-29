# Round 012: results extras (zones, journeys, PNG)

**Status:** Built 2026-10-29, **unbuilt in sandbox** (no .NET SDK; statically reviewed: bracket balance, XAML
well-formed, CRLF). 011c confirmed by Gavin's build. One build, not split (D-235). **265 tests expected** (257 + 8).

## Decided

| # | What |
|---|---|
| D-227 | Round 012 = measurement zone stats, per-journey stats, PNG export. Lifts move to 013. |
| D-228 | Zones: occupancy over time, peak occupancy, flow = entries per minute and per metre of the zone's narrower side. |
| D-229 | Journeys: rows profile × journey + ALL per profile; journey time mean / 95th, % within the profile's `AcceptableJourneyS`, % with any single wait over Long wait, stuck count, in progress. |
| D-230 | Count people spawned after the warm-up who finished; in progress and stuck apart. |
| D-231 | `StudySettings.LongWaitS` (STUDY ▸ RUN, default 120 s), additive within format v8. |
| D-232 | RESULTS sub-tab chips DEVICES / ZONES / JOURNEYS; graph gains ZONES. |
| D-233 | CSV adds `zones.csv`, `journeys.csv`; zone occupancy columns in `timeseries.csv`. |
| D-234 | PNG: view + legend + title block (study, level, sim time, map, "Conceptual, not a compliance tool"), 2× viewport size. |

## Build notes (for the build chat)

- Zones: `ZoneKind.Measurement` regions already exist (demo: "Gate line count" on the concourse, x 27–29, y 2.5–8.5).
  Rasterise each to floor cells in `SimSetup` like sinks; count occupancy each step from agent cells, entries on
  outside → inside transitions (a per-agent last-zone field in `AgentStore`, swap-removed like the rest). Narrower
  side = the shorter side of the zone's oriented bounding box. Add to `RunResults` and `SimEngine.Results.cs`.
- Journeys: per agent keep the longest single queue wait (join → service start) and spawn time; on arrival file the
  journey under (profile, journey) if spawned after `WarmUpS`. Agents with no journey (straight to a sink) get a
  "(no journey)" row.
- RESULTS UI: chips above the table switch the table's columns/items; graph metric ZONES = occupancy per zone
  (all zones, or the ticked ones like D-226).
- PNG: render the GL scene to an offscreen framebuffer at 2× (GLWpfControl), read pixels, compose legend and title
  block in WPF, encode with `PngBitmapEncoder`; FILE ▸ EXPORT PNG… (or a RESULTS button) with a save dialog. No new
  packages. Errors → `Result` + log, never thrown.
- Tests: zone occupancy and entries on the corridor study; journey rows and the warm-up rule; CSV headers; Long wait
  round-trips and defaults to 120 s for older files.

## 012 as built

**Sim**
- `Core/Geometry/OrientedBounds.cs`: smallest rotated bounding rectangle (convex hull + one try per hull edge);
  zones use its short side as the width crossed.
- `Sim/SimZone.cs`, `SimSetup.BuildZones`: every `ZoneKind.Measurement` region (≤ 64) has its walkable floor cells
  flagged with its bit in `SimModel.ZoneCellBits` (one `ulong[]` per floor that has zones). `SimModel.LongWaitS`.
- `AgentStore`: `ZoneBits` (zones stood in at the last count), `MaxWaitS` (longest single queue wait), `LeftEarly`
  (gave up and left), all swap-removed.
- `SimEngine.ZoneResults.cs`: `CountZones` each step (cell lookup per agent, no LINQ, skipped with no zones):
  occupancy now, entries on outside → inside after the warm-up, peak and mean after the warm-up; occupancy per zone in
  the time series from the start. Someone spawned inside a zone isn't an entry.
- `SimEngine.JourneyResults.cs`: rows from the emitters' mix lines (profile, then journey; "(no journey)" first).
  `RecordJourneyEnd` files each removal of a person spawned after the warm-up: finished (time, within acceptable,
  long wait, gave up) or stuck. In progress is counted at copy time. ALL row per profile with 2+ journeys.
  Long wait uses device queue waits (join → service start, or join → giving up).
- `RunResults`: `Zones`, `ZoneNames`, `ZoneOccupancy`, `Journeys`, `LongWaitS` (all optional, so older initialisers
  still compile); records `ZoneResult`, `JourneyResult`.

**App**
- RESULTS: TABLE ▸ DEVICES / ZONES / JOURNEYS chips (`ResultsTable`, `ZoneResultRow`, `JourneyResultRow`), the
  summary line follows the table; GRAPH ▸ ZONES (ticked zone rows, else all).
- `ResultsCsv`: `zones.csv`, `journeys.csv`, `Zone <name>` columns at the end of `timeseries.csv`.
- STUDY ▸ RUN ▸ Long wait (seconds), undoable; `StudySettings.LongWaitS` default 120, additive in v8.
- PNG: FILE ▸ Export PNG… and RESULTS ▸ EXPORT PNG (`MainViewModel.Png.cs`). `ViewportView.CaptureAsync` asks the next
  GL frame to draw again into an offscreen 4× MSAA framebuffer at 2× the on-screen pixels
  (`Render/Gl/OffscreenCapture.cs`, previous framebuffer and viewport restored), then draws the WPF plan overlay over
  it. `Services/PngExport.cs` adds the legend (top right: heatmap legend, else profile colours or the stress ramp
  while running) and the title block (bottom left: study, level / view, sim time, map, "CONCEPTUAL, NOT A COMPLIANCE
  TOOL", export time) and writes it with `PngBitmapEncoder`. Errors → `Result` + log + dialog.

**Tests** (+8): `Round012Tests` (oriented bounds, zone occupancy and entries on the corridor, zone warm-up, journey
warm-up rule, journey rows + ALL + long waits on the gates study, CSVs), `Round012FileTests` (Long wait round-trip,
missing in an older v8 file → 120).

## How to test

1. Build; all tests pass (265). Demo loads with 0 errors, 0 warnings.
2. New from Demo, PLAY. RESULTS ▸ ZONES: "Gate line count" shows peak, mean, entries and flow per minute and per metre
   (width ≈ 2 m) once the 5 min warm-up has passed; during the warm-up the summary says so.
3. GRAPH ▸ ZONES draws the zone's occupancy from the start, warm-up shaded; ticking the row keeps only ticked zones.
4. RESULTS ▸ JOURNEYS: a row per profile × journey in the demo mix, ALL rows for profiles with two or more journeys;
   DONE + STUCK + IN PROGRESS grows with people spawned after the warm-up; MEAN ≤ 95% roughly; WITHIN % near 100 in
   light flow.
5. RESET, STUDY ▸ RUN ▸ Long wait = 5, PLAY: LONG WAIT % rises at the gate journeys. Undo restores 120.
6. EXPORT CSV: four files; zones.csv and journeys.csv open in Excel; timeseries.csv ends with a `Zone Gate line count`
   column.
7. With a map on (e.g. LOS), FILE ▸ Export PNG…: the image is twice the viewport size, shows the map, plan labels,
   the legend top right and the title block bottom left. Try 3D too, and with NO MAP (profile legend) and STRESS.
8. Save, reopen: Long wait is kept; an older study opens with 120.
9. Don't-regress list (HANDOVER §8): demo PLAY crosses all three levels, editing locks until RESET, undo/redo,
   save → reopen, offline viewport panel (EXPORT PNG then says the viewport is offline).

## Judgement calls

- Gave-up count added to JOURNEYS (people who left after their patience ran out still count as finished, so the
  column says how many of the finished gave up). PEAK P/M² (peak occupancy ÷ zone area) and WIDTH M added to ZONES.
- ALL rows only for profiles with two or more journeys (one journey would repeat its own row).
- Someone spawned inside a measurement zone isn't counted as entering it.
- Long wait counts device queue waits only (not train platform waits or lane hold points); a wait ended by giving up
  counts too.
- The PNG includes the WPF plan overlay (labels, queues, trains, selection) since it's part of the view as shown; the
  HUD text is replaced by the title block. If the 2× image would pass the card's render size, the export says so.
- PNG legend without a map: profile colours (or the stress ramp) while running; none when not running.

## Accepted defaults

None.

## Known limitations

- Unbuilt in sandbox: the GL calls (`OffscreenCapture`) and the WPF composition are written against OpenTK 4.9.4 and
  WPF APIs from memory; check FILE ▸ Export PNG… on Gavin's machine first if anything fails.
- Zones count people on floor cells only; someone on a stair flight passing over a zone isn't in it.
- More than 64 measurement zones: the extra ones aren't counted (warning).
- Journey rows are keyed by profile and journey; people who switch to another step via a give-up JumpTo stay in their
  journey's row.

## Watch list

- Zone entries at a boundary: people jostling on the edge can step out and back in; expect up to ~10 % above the
  people passing in dense crowds.
- Carried over: crowd stress (D-194) barely shows at 2.5 p/m² with 1 m counting; hold points in front of an adjacent
  device's lane mouth; gate throughput with D-209/D-211 ("no lane clear").

## Open questions (non-blocking)

- Directional counting lines (in / out across a line) as well as zone entries? (D-228 said later.)
- Save results in the study (D-215) now that there are three tables?

## Next

Round 013a: lifts (D-236–D-240), format v9.

## Lifts, decided for round 013 (D-236–D-240)

- Added inside an existing shaft from a lift preset (`Core/Library/LiftTemplate.cs` exists, with capacity, speed,
  acceleration, doors, dwell, dispatch, car size, accessible priority); served levels = shaft levels minus no-stop.
- Banks of 1–4 cars, front and/or rear door, per level entry / exit / both / closed with its own landing zone.
- Dispatch: Collective (default), Shuttle, Up-peak, time-switched by schedule.
- Landings: loose crowd in the landing zone, board in arrival order, wheelchair users first at accessible-priority
  lifts.
- 013a: sim, placement, landings, dispatch, lift-vs-stair choice, plan symbols, format v9. 013b: 3D cars with riders.
  No further splits.
