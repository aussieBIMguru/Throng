# Round 011: results (M5), part a — live heatmap

**Status:** 011a done and confirmed by Gavin 2026-10-29 (r011a-2: all tests pass). Originally built (no .NET SDK in the sandbox): live density / LoS heatmap, wheelchair
gate fix (D-209). Format stays v7. Starting point: r010b-5 (Gavin: builds, 235 pass + 1 skipped, CHECK clean, demo runs).

## Decided

| # | What |
|---|---|
| D-200 | M5 split: 011a live heatmap, 011b cumulative/peak maps, device stats, time series, CSV. Wheelchair gate fixed as a side item. |
| D-201 | Modes: Off / Density / LoS. No speed-ratio map yet. |
| D-202 | Renderer draws a per-level texture on floors and stair/ramp flights; plan and 3D. |
| D-203 | 0.5 m cells, 1 m kernel, ~5 s smoothing. |
| D-204 | BRIEF §11.3 bands as-is (unverified); legend + peak-LoS ticker. |
| D-205 | Stair table on flights, queuing table in queue lines/snakes/dwell zones, walkway elsewhere; belts unmapped. |
| D-206 | Classic Fruin ramp; colour-blind-safe ramp as a STUDY setting. |
| D-207 | People drawn smaller and dimmed while a map shows. |
| D-208 | Live only; format stays v7 (results saving decided in 011b). |
| D-209 | Wheelchair gate: lane stays occupied until the previous person is through; test un-skipped. |

## What changed

- **Sim**
  - `Engine/DensityGrid.cs`, `DensityLayer.cs`, `DensityCellKind.cs` (new): one layer per floor, landing and stair or
    ramp flight (belts skipped), ~0.5 m cells on the surface's own axes. Each person spread over a 1 m disc (soft
    edge 0.75–1.25 m) on mapped cells only, normalised so they add exactly one; blended into a smoothed map with a
    5 s time constant every 0.5 s of sim time. Cell kinds: walkway (floors, landings), stair (flights), queue (within
    0.45 m of any device queue slot, including snakes, and dwell-zone cells).
  - `Engine/SimEngine.Density.cs` (new): `SimEngine.Density`, sampled at the end of `Step`, cleared by `Reset`.
  - `SimRunner.DensityLayout` + `CopyDensity(ref buffer, ref version)`: copies only when the map changed.
  - D-209: `SimTuning.LaneClear` (on). A person whose gate service is done waits (still in Service) until anyone who
    went before in that lane is past the middle of the gate (`LaneAheadClear`); the walk-through timeout restarts
    whenever the walker gets 0.25 m closer (`AgentStore.ThroughLeft`), so it only fires with no progress.
- **Analysis**: `HeatmapMode`, `HeatmapRamp`, `HeatmapPainter` (texels per layer, legend rows, peak finder,
  cell kind → Fruin table).
- **Render**: `Scene/HeatLayerDraw`, `Gl/GpuHeatLayer` (texture + quad lifted 3 cm), `SceneRenderer.SetHeatmap`
  (drawn over the floor fill in plan, after floors in 3D); agent shader `uDim` (70 % size, greyed) while a map shows.
- **App**: MAP chips (NO MAP / DENSITY / LOS) in the RUN toolbar; `MainViewModel.Heatmap.cs` paints the visible
  layers (plan: active level; 3D: visible levels; a flight shows with the level it rises from) only when the map,
  mode, ramp or view changed; legend bottom right of the viewport; ticker "PEAK ▸ LoS E at <place> (2.3 p/m²)" when
  the worst cell reaches E (place = connector, else nearest device within 6 m, else level; set only when the line
  changes). STUDY ▸ RUN "Colour-blind-safe heatmap" (`StudySettings.ColourBlindHeatmap`, undoable, additive in v7).
- **Tests**: Sim `Round011Tests` (8): map conserves people, ~63 % after 5 s, stair/belt kinds, demo queue cells, engine
  samples and resets, LoS table per kind, painting mapped cells only + peak, switch on. IO `Round011FileTests` (1).
  `DevicePreference_WheelchairUsersPickTheAccessibleGate` un-skipped. Diagnostics row "no lane clear (D-209)".
  **245 expected, all pass** (236 + 9 new; the wheelchair test no longer skipped).

## How to test

1. Build; run all tests (245 pass, 0 skipped). If the wheelchair test still fails, paste its message (it prints the
   diagnosis) and the diagnostics block's "no lane clear (D-209)" demo row.
2. New from Demo ▸ PLAY ▸ MAP ▸ DENSITY: a soft map appears on the concourse, platform and stair flights (not on
   escalators); people shrink and grey a little. Legend bottom right shows 0.5…4+ P/M².
3. MAP ▸ LOS: bands A–F; queues at the ticket machines and gates read on the queuing table (greener than the same
   crowd on open floor), flights on the stair table. After a train, the platform stair and gate line should go E/F
   briefly; the ticker reads "PEAK ▸ LoS E at …".
4. Switch levels in plan: the map follows the active level. 3D: all visible levels; hide a level, its map goes.
5. RESET, STUDY ▸ RUN ▸ Colour-blind-safe heatmap on, PLAY: purple → teal → yellow ramp; legend matches. Undo works;
   save/reopen keeps it.
6. MAP ▸ NO MAP: map and legend go, people back to full size.
7. Accessible gate: wheelchair users go through without the next person walking into them; no jam at the lane.
8. Don't-regress list (HANDOVER §8). Demo throughput at the gate line should be unchanged (normal walkers clear the
   middle of the gate within a tap); compare the diagnostics' "no lane clear" row.

## Judgement calls

- Landings use the walkway table (D-205 names flights only); ramps' flights use the stair table as decided.
- A flight is shown with the level it rises from (in plan it only shows on the lower level).
- LoS A is drawn faint (22 % alpha) so the mapped area reads; B–F at 72 %. Density ramp 0 → 4 p/m², fading in from
  0.02 to 0.3 p/m².
- Flat 1 m kernel rather than a peaked one: a lone person standing still reads ~0.3 p/m² (walkway B), not C; walkers
  smear along their path through the 5 s smoothing and read A.
- Linear texture filtering, so cells blend rather than show as squares.
- The colour-blind setting sits in STUDY, which is locked during a run: set it before PLAY.
- D-209 waits at the end of service rather than at the start, so a tap still overlaps the previous walker; only a
  walker still short of the gate's middle holds the next one back.

## Known limitations

- Unbuilt in the sandbox: compile errors are possible; nothing tuned against a real run.
- Fruin band values unverified (BRIEF §11.3).
- People on a piece only count on that piece (no spill across a floor end); belts unmapped by design.
- Map texels in plan are drawn under connector symbols and outlines but over underlays.
- No hover readout of a cell's density yet.

## Watch list

- Frame time with a large study and the map on (painting is only on change, every 0.5 s of sim time).
- Whether the 5 s smoothing hides the post-train surge too much or still flickers at MAX speed.
- Gate throughput with D-209 on (diagnostics row).

## r011a-2 (after Gavin's 011a build)

- Builds; the Fruin map is right. `DevicePreference_WheelchairUsersPickTheAccessibleGate` still failed: 26 % at the
  accessible gate, only 6 served there in 240 s, two people in Walk at the lane mouth (19.0–19.6, 7.0) and the stuck
  removal inside it. They had timed out of the walk-through without ever entering the lane.
- Cause: the wall push (3 × e^((r − d)/0.2)) from the lane-mouth corners, about 17 m/s² for a 0.45 m wheelchair that
  arrives slightly off-centre, points back out of the lane and beats the walking drive (~2 m/s²).
- **D-211** (under `SimTuning.LaneClear`): `LaneAxis` gives the lane's service point and walking direction for a line
  head, someone being served or walking through. Walking through steers to a point 0.8 m ahead on the lane centreline
  (lines up before the mouth); the wall push loses its along-lane part for those people (sideways only).
- **D-210**: demo gates 5 m further south (gap y 2.5–8.5, gate line y 4.5, accessible y 7.9; count zone follows).
- **D-212**: cumulative maps confirmed for 011b.
- **Unbuilt.** Still 245 tests expected, all pass. If the wheelchair test fails again, paste its message.

### How to test (r011a-2)

1. All tests; the wheelchair test should pass. Diagnostics: compare "no lane clear (D-209)" against "all on".
2. New from Demo: CHECK clean; the gate line sits 5 m further south; PLAY past a train: no clog at the gates.

### r011a-2 result (Gavin, 2026-10-29)

- Builds; all tests pass including the wheelchair gate test. 011a closed. D-213 (hard accessible-device rule) goes
  into 011b. Gavin's 011b handover: `HANDOVER.md` header.

## Next (011b)

Cumulative maps (max/mean density, time at LoS E or worse, trail), device stats (served, mean/95th/max wait, max
queue, utilisation, balks), time-series graph, CSV export, and whether results are saved in the file (format v8).

## 011b Query answers (Gavin, 2026-10-29; to log as D-214 onwards at Build)

1. Results not saved in the file; CSV export only. Format v8 carries only the D-213 flags.
2. Cumulative maps: all four (max density, time at LoS E or worse, mean density, trail / footfall).
3. MAP picker: two chip rows, LIVE (NO MAP / DENSITY / LOS) and RUN (MAX / E+ TIME / MEAN / TRAIL).
4. Warm-up (`WarmUpS`) excluded from cumulative maps and stats; the live map and graph still show it, the graph
   with the warm-up shaded.
5. Device stats in a new RESULTS tab on the right (sortable table + graph; viewport selection highlights the row).
6. Graph hand-drawn in WPF (no new dependency).
7. Series: people in model, queue length per device, peak density with Fruin bands, arrival / exit rate.
8. CSV: `devices.csv` + `timeseries.csv` into a chosen folder; map grids stay in the app.
9. D-213 "Accessible": preset default with a per-device override in SELECTION (saved in v8).
10. D-213: people without the need soft-avoid accessible devices (penalty, like luggage's +15 s in reverse).
11. D-213: CHECK finding is a warning (PLAY allowed).
12. Graph queue lines: devices ticked in RESULTS, else the five longest max queues.

## 011b as built (2026-10-29)

**Unbuilt:** no .NET SDK in the sandbox again; statically reviewed only (brace/paren balance, XAML parse, CRLF).
Decisions logged first: D-214–D-226 (011b builds D-213, D-215–D-218, D-223–D-225; D-219–D-222 and D-226 are 011c).

### What changed

- **Cumulative maps (D-216, D-218).** `Sim/Engine/DensityMeasure.cs` (Live / Max / Mean / LosEOrWorseS /
  Footfall). `DensityGrid` gathers at every 0.5 s sample once `SimModel.WarmUpS` (from `StudySettings.WarmUpS`) has
  passed: max and mean of the smoothed map, seconds at or above `LosEOrWorseDensity(kind)` (1 / Fruin D bound per
  table), and footfall: each person's cell at the last sample (`AgentStore.TrailCell`), counting every cell on the
  straight line to the new one (jumps over 16 cells count only the end). `Values(measure)`, `AccumulatedS`,
  `WarmUpS`; `Clear()` (RESET) empties them all.
- **Runner:** publishes all five measures back to back; `CopyDensity(ref buffer, ref version, measure)` copies one;
  `DensityAccumulatedS`.
- **Painter (Analysis):** `HeatmapMode` + MaxDensity / MeanDensity / LosEOrWorse / Footfall; `MeasureOf`,
  `IsCumulative`, `IsScaled`, `ScaleFor` (map top rounded up to 1/2/5 × 10ⁿ, at least 10, one scale over every
  level); `PaintLayer(..., scale)` and `Legend(..., scale)`. Max on the LoS bands, mean on the density ramp, E+ time
  linear and footfall square-root on the ramp.
- **App:** MAP ▸ RUN chip group (MAX, E+ TIME, MEAN, TRAIL) after the live chips; NO MAP turns either off. Legend
  titles per map and a note line: "WARM-UP ▸ COUNTS FROM 5:00", then "12:30 AFTER WARM-UP". The peak ticker stays on
  the live maps only.
- **Accessible rule (D-213, D-223–D-225).** `DeviceTemplate.Accessible` (built-in accessible gate on, version 2),
  `Device.AccessibleOverride` (null = preset), `StudyTemplates.IsAccessible`, `AgentProfileTemplate.NeedsAccessibleDevices`
  (built-in wheelchair profile on, version 3; `BuiltInLibrary.MobilityProfileKey`). `SimDevice.Accessible`.
  `ChooseDevice`: flagged people skip a non-accessible device when any device of the step on that device's floor is
  accessible; others add `SimEngine.AccessibleAvoidS` (15 s) to an accessible device unless their profile favours
  it (also in `TryBalk`'s like-for-like). Switch `SimTuning.AccessibleRule` + diagnostics row "no accessible rule
  (D-213)". SELECTION ▸ device: "Accessible" switch (undoable; matching the preset clears the override) and where the
  flag comes from.
- **CHECK (D-225):** warning "Mobility-impaired (wheelchair) people have no accessible Gate on Concourse; they'll use
  the others there." once per profile, type and level, for spawn mix lines with a share and a journey.
- **Format v8:** the three flags. `UpgradeV7ToV8` sets them on embedded copies of the built-in accessible gate and
  wheelchair profile (same key); everything else keeps the old behaviour.
- **Tests:** 253 expected (245 + 6 in `Round011bTests` + 2 in `Round011FileTests`); the wheelchair test now expects
  more than 95 % at the accessible gate; v7 save tests now check the current version (`Save_WritesFormatV8`).

### How to test

1. Build; all 253 tests pass. Paste any failure message (the wheelchair test prints its diagnosis).
2. Diagnostics: compare "no accessible rule (D-213)" against "all on" (demo arrivals should match: the demo has no
   wheelchair users in its mixes).
3. New from Demo: CHECK clean (0 errors, 0 warnings). PLAY: MAP ▸ RUN ▸ MAX shows the warm-up note until 5:00, then
   fills; E+ TIME, MEAN and TRAIL fill; the legend scale steps up as they grow. PAUSE / end of run: maps stay. RESET:
   gone.
4. Switch between live and run maps while playing and paused: each shows at once (no stale map from the other).
5. Select the accessible gate: "Accessible" on, "From the preset". Select the gate line, switch it on: "Set on this
   device"; undo/redo; save, reopen: kept.
6. Add a Mobility-impaired mix line on the street entrance, delete the accessible gate: CHECK warns about Gate on
   Concourse; PLAY runs and they use the gate line. Undo the delete: no warning.
7. Open a v7 study saved by 011a: loads clean; the accessible gate is still accessible.
8. Don't-regress list (HANDOVER §8).

### Judgement calls

- RUN chips sit as a labelled group in the same toolbar strip rather than a second stacked row, so the toolbar
  doesn't grow taller. Easy to stack if you'd rather.
- The run maps fold in the 5 s smoothed map (what the live map shows), so max is a sustained peak, not a one-sample
  spike, and E+ time matches the LoS map you watched.
- Footfall counts cells walked onto between samples (a person standing still adds nothing; a queue shows as its
  entry path, not its length).
- The soft avoid doesn't apply to people whose profile favours that device, so luggage keeps its +15 s preference
  (D-213) instead of it being cancelled by the 15 s avoid.
- The hard rule applies per floor of the candidate device, as written; the CHECK warning only covers device types
  that have an accessible version at all (a preset or a placed device), so ticket machines don't warn on every
  study.
- Profiles have no in-app editor yet, so "Needs accessible devices" is set on the built-in wheelchair profile (and
  readable in its summary) but can't be toggled in the UI.

### Known limitations

- Mean density is faint on the ramp (most places average well under 0.3 p/m²); it's honest, but you may want its
  own ramp top.
- Results aren't saved (D-215): reopening a study shows no maps until it runs again.

### Needs your OK

- Nothing new. Project doc sync and returning the zip were done in one step at the end.

### 011b result (Gavin, 2026-10-29)

- Builds; no issues. 011b closed.

### Next (011c, the last part of round 011)

RESULTS tab (D-219), device stats, the hand-drawn graph (D-220, D-221, D-226) with the warm-up shaded (D-218), CSV
export (D-222).

## 011c as built (2026-10-29)

**Unbuilt:** no .NET SDK in the sandbox; statically reviewed only (brace/paren/bracket balance, XAML parse, CRLF).
Builds the already-logged D-218–D-222 and D-226; no new decisions. Round 011 ends here (D-214).

### What changed

- **Sim:** `Engine/SimEngine.Results.cs` gathers, after the warm-up, per device: served and each wait (join to service
  start), max queue (every step), busy lane-seconds (utilisation = busy ÷ lanes × measured time), balks (left the line
  for another device) and gave up (patience ran out in its line, leaving or skipping). A time series from the start
  every 5 s (`RunResults.SeriesIntervalS`): people in model, arrivals and exits per minute, peak smoothed density,
  queue length per device. `CopyResults()` builds an immutable `RunResults` (`DeviceResult` rows; 95th percentile by
  nearest rank); RESET clears it. `SimRunner.LatestResults`, rebuilt at most every 0.5 s while playing and at once on
  pause, finish or reset.
- **Analysis:** `ResultsCsv` (`devices.csv`, `timeseries.csv`; invariant numbers, RFC 4180 quoting, UTF-8 without BOM,
  `Export` returns a `Result`); `FruinLevelOfService.GradeStartDensities`.
- **App:** RESULTS tab (fourth tab), outside the edit-locked panel so it works while running: summary line, DataGrid
  (sortable by header; ▸ tick puts a device's queue on the graph), GRAPH chips PEOPLE / QUEUES / DENSITY / FLOW, the
  hand-drawn `Views/TimeSeriesGraph` (axes, four grid lines, warm-up shaded, Fruin walkway bands in the heatmap's LoS
  colours, legend), EXPORT CSV (`IDialogService.PickFolder`, the .NET folder dialog). Theme: `ResultsGrid` styles.
- **Selection while running:** in the plan, a click with SELECT picks what's under it (nothing moves, no edits). On
  RESULTS the tab stays and the device's row lights up; on other tabs it shows in SELECTION as before.
- **Tests:** 257 expected (253 + 4 in `Round011cTests`).

### How to test

1. Build; all 257 tests pass.
2. New from Demo, PLAY at MAX. RESULTS: summary says WARM-UP until 5:00, the graph records from the start with the
   warm-up shaded; after 5:00 the table fills.
3. Sort by a column (e.g. 95% S). Click the gate line in the plan: its row lights up and the tab stays on RESULTS.
4. GRAPH chips: PEOPLE, QUEUES (the five longest; tick two rows, only those show), DENSITY (bands A–F behind the
   peak line), FLOW. Untick all: back to the five longest.
5. Pause and end of run: the table and graph stay. EXPORT CSV into a folder: two files open in Excel, numbers with
   dots. RESET: RESULTS empties.
6. Colour-blind heatmap on: the DENSITY graph's bands follow the ramp.
7. While running, click things on other tabs: SELECTION shows them (greyed, not editable); Delete or drag does
   nothing but flash RUNNING.
8. Don't-regress list (HANDOVER §8).

### Judgement calls

- The graph shows one measure at a time (chips), since the four series have different units.
- Peak density on the graph uses the walkway bands (the commonest table); the map itself still grades each place by
  its own table.
- "Gave up" per device counts both leaving and skipping to another step; the status bar's GAVE UP still counts only
  people who left.
- Waits count when service starts after the warm-up, whenever they joined.
- Table rows update in place; a sorted column re-sorts when you click it again, not live.
- Accepted defaults (cheap to change): 5 s series interval; CSV column names as above.

### Known limitations

- Per journey and per measurement zone stats (BRIEF §11.4) aren't in this round.
- Graph points grow with the run (720 per hour); fine for hours, not days.

### Needs your OK

- Nothing new.

### Next

Round 011 (M5 results) is complete. Next round: pick from the wishes on record (3D lift car M4, D-068; visual
node editor, stretch) or the BRIEF milestones. That's a Query phase.
