# Round 021: seeded batch runs with averages (M8)

**Status:** built 2026-11-01, **unbuilt in the sandbox** (no .NET SDK, no NuGet; statically reviewed: brace / XAML
checks, CRLF, every touched file read). 020 confirmed by Gavin (built clean, all 362 tests pass). Decisions
D-352–D-365 (query phase 2026-10-31), D-366 (parallel reviewed safe), D-367 (judgement calls). **370 test methods**
(+8). **Format stays v11** (batches are session only, D-362). No new dependencies (`Parallel.For` from the BCL).

## Decided (D-352–D-367)

| # | What |
|---|---|
| D-352 | Round 021 = batch runs with averages (after 020 fixes; 020 built clean). |
| D-353 | Built-ins saved to MY LIBRARY keep the built-in's id (020 call confirmed; nothing changed). |
| D-354 | N runs, default 5 (2–20), seeds base … base+N−1 (base = the study seed). |
| D-355 | Parallel across cores; each run identical to PLAY on its seed. |
| D-356 | Parallel = cores − 1, automatic, capped at N. |
| D-357 | Viewport shows nothing live: progress line + CANCEL; editing locked. |
| D-358 | All RESULTS tables: mean with min–max. |
| D-359 | Graphs: mean line + min–max band. |
| D-360 | Heatmaps: averaged cumulative; peak = highest across runs. |
| D-361 | 'Until stopped' asks a duration; 'N journeys' runs as set. |
| D-362 | Session only + CSV (summary + per run); format stays v11. |
| D-363 | STUDY ▸ RUN ▸ BATCH…; RESULTS source switch LAST RUN / BATCH (N). |
| D-364 | Overlay ACTUAL = batch mean, labelled BATCH (N). |
| D-365 | CHECK errors block; study warm-up per run. |
| D-366 | Parallel reviewed safe before building (shared read-only model, engine-owned state); seed + duration constructor. |
| D-367 | Judgement calls below. |

## Why parallel is safe (D-366)

Checked before writing any batch code: `SimModel` and everything under it (`NavRoutes`, `NavWorld`, flow fields, `SimDevice`,
`SimLift`, `SimTrain`, `SimJourney`, zones) has its only setters (`SimProfile.DeviceBonusS`, `NavSink.AvoidCells`,
`SimEmitter.TrainService`) written once in `SimSetup`; the remaining-cost tables are built eagerly in `NavRoutes.Build`; the
Sim and Core projects have no mutable statics (only immutable `Default` / `Instance` / `All` singletons). Everything a run
changes lives in its `SimEngine`: `AgentStore`, `DeterministicRandom` streams, spatial hash, queues, lift and train state,
density grid, results counters. So one compiled model is shared read-only and each worker owns one engine.

## What changed

**Sim**
- `Engine/SimEngine.cs`: new constructor `SimEngine(model, seed, durationS, tuning)`; the old one forwards the model's seed
  and end. `Seed` and `DurationS` properties; `IsFinished` and `Reset` read the engine's own values. `SimRunner.IsFinished`
  reads the engine's duration.
- `Batch/` (new folder, namespace `Throng.Sim.Batch`):
  - `BatchOptions` (base seed, runs, duration, parallelism; `DefaultParallelism` = cores − 1 capped at runs, `SeedOf(k)`
    wraps like the engine's seed cast; Min/Max/Default runs 2/20/5).
  - `BatchRunner`: compiles nothing, takes the one model; `Run(cancel)` blocks (call from a worker) and runs
    `Parallel.For` with `EffectiveParallelism`; each run is a fresh engine stepped until `IsFinished` (cancel + progress
    every 64 steps), keeps `CopyResults()` and a copy of its four cumulative maps, then drops the engine. Maps are folded
    in seed order (early finishers wait) so averages never depend on threads. `Progress` (done, running, total, fraction)
    is safe from any thread.
  - `BatchAverager.Combine`: pure; tables as `BatchStat` (mean, min, max, runs), series as `BatchSeries`, the overlay rows,
    and `Mean` (a `RunResults` with counts rounded) for anything that reads one run.
  - `BatchResults`, `BatchRun`, `BatchStat`, `BatchSeries`, `BatchProgress`, `BatchDensity`, `DensityFold` (internal),
    `DeviceBatchResult`, `ZoneBatchResult`, `JourneyBatchResult`, `LiftBatchResult`.

**Analysis**
- `BatchCsv.cs` (new): `summary\` with devices / zones / journeys / lifts / timeseries .csv (every stat X as X, XMin, XMax)
  and `runs.csv` (run, seed, folder, measured, served, finished, gave up, stuck), plus `run-01-seed-42\` … with the usual
  six single-run files (`ResultsCsv.Export`, predicted shares from the batch model).

**App**
- `ViewModels/MainViewModel.Batch.cs` (new): BATCH command, dialog, CHECK + compile once (errors open CHECK, D-365),
  `BatchRunner` on a worker, progress in the status bar (`BATCH ▸ 2 OF 5 DONE · 3 RUNNING (7 AT ONCE) · 46%`), CANCEL
  (the RESET button reads CANCEL), SOURCE chips, `ShowLastRun` on PLAY, `ClearBatch` on new / open.
- `Views/BatchDialog.xaml(.cs)` (new) + `Services/BatchRequest.cs`, `IDialogService.AskBatch`: runs 2–20, minutes per run
  only for 'until stopped', live line "SEEDS 42–46 · 10 MIN EACH · 7 AT ONCE (8 CORES)".
- `ViewModels/StatCell.cs` (new): a table cell that is a single value or "mean (min–max)"; formats through the column's
  StringFormat and sorts by the mean, so the RESULTS DataGrid XAML is unchanged. `DeviceResultRow`, `ZoneResultRow`,
  `JourneyResultRow`, `LiftResultRow` numbers are `StatCell`s, each with an `Update(…BatchResult)`.
- `MainViewModel.Results.cs`: batch path (`ShowBatchResults`, `ClearResultsView`, `BatchSummary`, `BatchGraph` with
  bands, `ExportBatch`); `LosBands` split out of `DensityGraph`.
- `Views/GraphLine.cs` (`Low`, `High`) and `TimeSeriesGraph.DrawBand` (faint fill in the line's colour, under all lines;
  the value axis scales to the band's top).
- `MainViewModel.Heatmap.cs`: `FetchHeat` paints the batch's maps while SOURCE is BATCH (`RefreshBatchDensity`,
  `InvalidateHeat`); legend note "AVERAGE OF 5 RUNS · 10:00 EACH" / "HIGHEST OF 5 RUNS"; live chips say "LIVE MAPS NEED
  PLAY". The legend shows with a batch on show even when no run is active.
- `MainViewModel.JourneyFlow.cs` / `JourneyViewModels.cs`: `OverlaySource` (batch or runner) replaces `OverlayRunner`;
  strip reads BATCH (N); the node editor's RUN rows follow (same `ActualFlow`).
- `MainViewModel.Run.cs`: status line, PLAY / STEP blocked while batching, RESET cancels a batch, BATCH command
  enablement. `MainWindow.xaml`: BATCH… under the seed in STUDY ▸ RUN, SOURCE chips above TABLE, RESET label binding,
  tooltips.

**Tests** (`Round021Tests`, +8): batch run k = PLAY on seed base+k (study saved with that seed, compiled on its own);
same summary and maps with 1 or 4 threads; peak map = highest run, mean map = average; averager rules (counts over every
run, times over runs with someone, missing rows, uneven series, rounded `Mean`); cancel throws; parallelism and seed wrap;
engine seed constructor = model seed; batch CSV layout.

## How to test (numbered)

1. Build, run all tests: **370** pass (Round021Tests runs a few short corridor batches, a few seconds).
2. New from Demo. STUDY ▸ RUN ▸ **BATCH…**: the dialog shows Runs 5, "SEEDS 42–46 · … MIN EACH · N AT ONCE (M CORES)"
   (your seed). Type 1 or 21: START greys out with a message.
3. START. Editing locks, the viewport stays empty, the status bar shows the progress line, RESET reads **CANCEL**.
   Task Manager shows several cores busy.
4. When done: RESULTS opens with **SOURCE ▸ LAST RUN / BATCH (5)**, BATCH lit; cells read e.g. `12.3 (10.1–14.0)`;
   the summary line names the seeds. Click a column header: it sorts by the mean.
5. GRAPH chips: each line has a faint band (lowest–highest run). QUEUES: tick rows, bands follow.
6. MAP ▸ RUN ▸ MEAN / TRAIL / E+ TIME / MAX: the averaged maps show with no run active; legend note "AVERAGE OF 5 RUNS"
   ("HIGHEST" on MAX). DENSITY / LOS (live): nothing, note "LIVE MAPS NEED PLAY".
7. LIBRARY ▸ JOURNEYS ▸ a journey ▸ SHOW GRAPH: ACTUAL strip says **BATCH (5)**, wires show mean counts and shares;
   SELECTION ▸ node ▸ RUN rows too.
8. EXPORT CSV: pick a folder → `summary\` (devices, zones, journeys, lifts, timeseries with X / XMin / XMax columns,
   runs.csv) and `run-01-seed-42\` … `run-05-seed-46\` with the usual files.
9. Determinism: note one run's numbers in `run-03-seed-44\devices.csv`; set the study seed to 44, PLAY to the end at MAX,
   EXPORT CSV: identical devices.csv.
10. PLAY: SOURCE flips to LAST RUN (live results); click BATCH (5): batch tables, graph, maps, overlay come back while the
    run stays in the viewport. RESET: the batch stays.
11. Set End to Indefinite, BATCH…: the dialog asks minutes per run; 2 runs of 2 minutes complete.
12. BATCH… then CANCEL mid-way: editing unlocks at once, ticker "BATCH CANCELLED", any previous batch is still on SOURCE.
13. Break the study (delete the exit zone), BATCH…: CHECK opens with the errors; nothing runs.
14. Open another study / New: SOURCE chips disappear (session only, per study).

## Judgement calls

- **One switch** (D-363): SOURCE drives the tables, graph, RUN maps and the journey overlay together, so they never mix
  sources. PLAY switches to LAST RUN; a finished batch switches to BATCH.
- **Cells**: "mean (min–max)", only the mean when every run agrees (avoids "0 (0–0)"). Counts show one decimal when the
  mean has one (e.g. served 102.4).
- **What a mean covers**: counts (served, entries, finished, gave up…) average over every run, a row a run didn't have
  counting as 0; times and shares (waits, journey times, within %, long wait %) only over runs that had someone to time.
- **Journey rows**: union of every run's rows; the first run's order, later-only rows after their profile's rows.
- **Series**: the longest run's sample times; a sample counts the runs that reached it (runs end together for a fixed
  duration, so this only matters if something differs).
- **Overlay (D-364)**: counts are means rounded to whole people (wire shares follow the rounded counts); node waits are
  means over runs that served someone. Node stats show as means, not min–max (the overlay has no room for ranges).
- **Batch CSV (D-362)**: a `summary` folder (one file per table, X / XMin / XMax) plus `runs.csv`, and a folder per run,
  rather than one mixed file: opens cleanly in Excel and per-run files are exactly PLAY's.
- **Live maps** have no batch value (a batch's "now" means nothing): live chips show no map, with a note.
- **CANCEL** keeps the previous batch (if any) and unlocks editing immediately; workers stop at their next 64-step check.
- **'N completed journeys'**: the engine has no such end and the app doesn't offer it, so a study set that way (old file)
  asks a duration like 'until stopped' (D-361's spirit: every run must end).
- **Prediction source**: after a batch the PREDICTED overlay says "FROM BATCH" (same compiled model).

## Accepted defaults

- Spread wording: min–max (round-021 queries' non-blocking question; easy to change in `StatCell`).
- Default minutes per run for 'until stopped': 60.

## Known limitations

- **Unbuilt in the sandbox**: expect a few compile errors to fix first next round.
- Batch results don't follow later study edits: they're the batch's numbers until the next batch or a new / opened study.
- Node stats in the summary CSV: per-run `nodes.csv` only (no across-runs nodes summary).
- Memory at stadium scale: each running engine holds its own agents and density grid (up to cores − 1 at once), and every
  run keeps its `RunResults` (time series × devices). Fine at demo / tower size; watch with the stadium pass.
- The progress line updates at 4 Hz with run fractions; very short runs may jump straight to done.

## Watch list

- First build: `BatchAverager` (target-typed conditionals in lambdas, `?? []`), `StatCell` formatting in the DataGrids,
  `BatchDialog` resources.
- Determinism test `BatchRun_IsPlayOnItsSeed` is the guard for "run k = PLAY on seed k": if it fails, something in the
  model is mutated during a run.

## Open questions (next round)

- Across-runs nodes summary CSV (journey nodes with min–max)? Recommend: only if you use nodes.csv a lot.
- Next from the backlog: replay scrubbing (M8), stadium-scale performance pass + stadium demo, one-page PDF report (could
  take the batch summary), group movement.

## Next

Gavin builds 021 and reports errors; fix them first, confirm with the checklist above, then the next round's queries.
