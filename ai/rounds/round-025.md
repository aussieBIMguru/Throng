# Round 025 — SERVICES, PDF report, PERF overlay, stadium demo and bench

**Status:** built on r024b (confirmed by Gavin: builds, no issues), **unbuilt in the sandbox** (no .NET SDK, no NuGet;
reviewed by reading only). 409 test methods (+9; the bench is one of them and is skipped unless asked for). One round,
no split (D-396). **New package: PDFsharp 6.2.4 (MIT)** — the first restore pulls it in; please run the licence audit
below.

**025 fix-up (after Gavin's first test run):** `Round010bFileTests.Validation_CatchesBadTimetablesAndMissingServices`
failed: the reworded headway warning said "leaves no gap"; it reads "…arrivals every N s with the doors open M s leave
no gap…" again (door word from the service's kind). PDF export failed with "No appropriate font found for family
name 'Consolas'": PDFsharp's built-in Windows resolver only knows a few families, so `Services/ReportFontResolver.cs`
now reads Arial and Consolas (all four styles) from the Windows fonts folder, falling back to Courier New / Arial /
Segoe UI; `PdfReport` sets it once as `GlobalFontSettings.FontResolver`.

## Decisions taken

| D | What |
|---|---|
| D-398 | Trains become **SERVICES**: rename + a kind per service (Train, Plane, Bus / coach, Ride, Other) for words, plan label and starting timetable; same engine; additive in format v12 |
| D-399 | SERVICES is its own right-panel tab, after STUDY |
| D-400 | PDF report: title block + headline numbers, a plan heatmap of every level (no 3D), the people graph and CHECK findings; no tables |
| D-401 | PDFsharp (MIT) writes the PDF |
| D-402 | A3 landscape, plans tiled; past six levels, more pages of plans |
| D-403 | EXPORT PDF REPORT asks which map in a dialog |
| D-404 | Stadium demo: six sectors ≈ 30,000 seats, plus a one-sector demo for live viewing |
| D-405 | Stadium bench is report only (fails only when the run breaks) |
| D-406 | Judgement calls (below) |

Already decided for this round: D-379 (30k headless / 5k live at ≥ 30 fps), D-380 (PERF overlay + bench), D-381
(concourse-sector demo copied to scale).

## What changed

**SERVICES (D-398, D-399)**
- Core: `Model/ServiceKind.cs` (`ServiceKind`, `ServiceKinds.{All, Label, Vehicle, Doors, Badge, Defaults}`);
  `TrainService.Kind` (default Train, so older files load as trains), `TrainService.Create(kind, name, doors)`,
  `TrainService.WithKind(kind)` (values still at the old kind's starting values take the new kind's; changed ones stay);
  `Describe()` starts with the kind. The type keeps its name `TrainService` in code and files.
- Starting timetables: Train 6 min / open 45 s / 150 off / 300 places; Plane 60 min / 25 min / 0 off / 180; Bus
  10 min / 30 s / 20 / 60; Ride 90 s / 40 s / 24 / 24; Other as Train.
- App: new right-panel tab **SERVICES** (`PropertiesTabServices = 3`; RESULTS is now 4, CHECK 5), the old STUDY ▸
  TRAINS block moved there with a **Kind** drop-down; field labels follow the kind ("First plane at…", "Boarding
  gates open (s)", "Places per ride car", DRAW PLANE…). While running, the plan labels each vehicle TRAIN / PLANE / BUS /
  RIDE / SERVICE.
- Wording: journey ends read "Board … service", the node editor's tag "Service tag", CHECK says "Service '…'",
  "belongs to two services", "needs a service"; `Round010bFileTests` updated to match.

**PDF report (D-400–D-403)**
- `App/Services/{ReportData, PdfReport}.cs`: A3 landscape. Left column: THRONG, study, what the numbers are (one run
  on seed N, or mean of N runs), "indicative, not a compliance assessment", HEADLINES (finished, mean journey, most
  people in the model, peak density with walkway LoS, longest 95th wait and queue, groups, gave up, stuck), MAP legend,
  PEOPLE IN THE MODEL graph (in the model, queuing, warm-up shaded), CHECK findings (errors first, as many as fit).
  Right: every level's plan with the map, tiled (1, 2, 2 × 2 or 3 × 2), six per page, more pages after; footer.
- `App/Views/ReportDialog.xaml(.cs)`: pick the map (run maps, live maps, or none).
- `App/ViewModels/MainViewModel.Report.cs`: `ExportPdfCommand`; draws each level in plan with the map (active level +
  zoom extents, captured at 1.5×), then puts mode, level, map and camera back; findings from a fresh CHECK minus ignored.
  File ▸ Export PDF report… and RESULTS ▸ PDF REPORT. `IDialogService.{PickPdfSavePath, AskReportMap}`.
- `Throng.App.csproj`: `PDFsharp` 6.2.4. Fonts: `ReportFontResolver` (Arial and Consolas from the Windows fonts
  folder, fix-up below).

**PERF (D-380)**
- `Sim/Engine/SimEngine.Perf.cs`: nine timed phases per step (groups/arrivals, trains/lifts, waits/spawn, review,
  journeys, plan, move, leave, maps/results); `CopyPhaseTicks`, `PerfSteps`. Reading the clock never changes a run.
- `Sim/PerfSample.cs`, `SimRunner.Perf`: one-second windows (ms per step by phase, ×real time, people).
- `App/Views/ViewportView.Perf.cs` + `PerfText` in `ViewportView.xaml`: fps, frame and render ms, then the sim lines;
  **F8** or View ▸ Performance Overlay (F3 was already 3D orbit).

**Stadium (D-379, D-381, D-404, D-405)**
- `IO/StadiumBuilder.cs`, `IO/StadiumScenario.cs`: a 60 m sector (plaza with three 4-lane turnstile lines, two 3.6 m
  stairs and an escalator pair up 5 m, concourse with two food zones and toilets, a bowl wall with three vomitories to
  three seat blocks), copied side by side. Ingress: Event fans 70 % / Fans groups 20 % / Family groups 10 %, 30 % hungry,
  15 % need the toilets, event-surge curve, ≈ 5,000 per sector over the hour through the journey "Turnstile, food,
  toilets, seat". Egress from minute 70 over ten minutes back to the gate plaza. 18:00 start, 90 min, 5 min warm-up.
- New from Demo ▸ **Stadium sector** (≈ 5,000) and **Stadium (6 sectors)** (≈ 30,000).
- Bench: `Round025Tests.Bench_Stadium` (`TestCategory("Bench")`), runs only with `THRONG_BENCH=1`; **`bench.bat`** at
  the repo root runs it in Release and prints, per simulated minute: people, ms per step, × real time and each phase,
  for six sectors emptying (15 min) and one sector emptying (12 min). Fails only on breakage (> 2 % stuck).

**Tests (+9):** `Throng.Core.Tests/Round025Tests` (3: kinds), `Throng.IO.Tests/Round025FileTests` (2: kind round trip,
stadium saves), `Throng.Sim.Tests/Round025Tests` (4: timings don't change runs, stadium demos validate and compile, a
sector's fans get through, the bench). Updated: `Round010bFileTests` (service wording).

## How to test

1. Restore + build; run all tests (409; the bench shows as skipped / inconclusive).
2. **Licence audit** (§20.3): `dotnet list src/Throng.App package --include-transitive` — paste the PDFsharp lines so
   THIRD-PARTY-NOTICES gets the resolved versions.
3. Interchange demo: the right panel has **SERVICES** after STUDY; STUDY no longer lists trains. Both services show
   Kind = Train; in the journey "Gates then north or south train", selecting an end node shows "Service tag".
4. Change Southbound's kind to **Plane**: labels change (First plane at…, Boarding gates open), headway / open time /
   places take the plane's values but anything you'd edited stays; Ctrl+Z puts it back. PLAY: the box in plan says PLANE.
5. File ▸ **Export PDF report…** with no run: dialog note says layout only; pick No map → A3 PDF with three level
   plans, no numbers.
6. PLAY the demo past the warm-up, then RESULTS ▸ **PDF REPORT** with "Run · time at LoS E or worse": headline numbers,
   legend, graph, CHECK findings, three plans with the map. The view (3D or plan, active level, map, camera) comes back
   as it was.
7. BATCH… 3 runs, SOURCE ▸ BATCH, PDF REPORT: the source line says "Mean of 3 runs".
8. Office tower (11 levels): the PDF has two pages (6 plans, then 5).
9. **F8**: PERF overlay top right (fps, render ms, then people / ms per step / phases while running). F8 again hides it.
10. New from Demo ▸ **Stadium sector**: CHECK has no errors; PLAY at ×8 or MAX: fans through the turnstiles, up the
    stairs and escalators, some stopping at food or toilets, into the seat blocks; from 19:10 they pour out.
11. New from Demo ▸ **Stadium (6 sectors)**: loads and runs (MAX); use PERF to see where the time goes.
12. **Run `bench.bat`** and paste its output back: that's what the tuning round works from.
13. Don't-regress: Interchange and Office tower run as before (same seed, same numbers; timings never feed the run);
    save → reopen keeps service kinds; undo.

## Judgement calls (D-406)

- Kinds change words, plan label and starting values only; changing a kind keeps every value you've edited.
- PERF on **F8** (F3 is 3D orbit); windows of one second; the step split into nine phases in step order.
- The stadium is an unrolled straight row of sectors (simple, long concourse) rather than a ring; no step-free route
  (no ramp or lift: 5 m at 1:14 is 70 m), so no wheelchair users in the mix.
- Egress uses straight-to-the-plaza lines (no journey); the bench runs the **egress** scenario because emptying puts the
  most people in the model at once.
- MSTest has no `[Explicit]`, so the bench is skipped (inconclusive) unless `THRONG_BENCH=1`; `bench.bat` sets it.
- Report: plans captured at 1.5× the viewport; findings from a fresh CHECK, without ignored ones; headline "peak
  density" uses the walkway LoS table; groups line only when the study has groups.
- Tabs: SERVICES between STUDY and RESULTS, so RESULTS and CHECK move one place right.

## Known limitations

- Unbuilt in the sandbox: no compile, no tests, no PDF opened. PDFsharp's API was used from its documentation; if a
  call doesn't match 6.2.4, the build error will say which.
- No performance changes yet: this round measures (PERF, bench). Tuning follows from the bench output.
- The PDF captures what the viewport can render: hidden levels come out empty; huge plans (six-sector stadium) are
  small on A3.
- The PDF's map legend is the live legend's rows (same as EXPORT PNG).
- Six right-panel tabs are tight at 340 px; if they wrap or clip, say so and I'll shorten labels.
- Stadium seat blocks are zones, not seats; vomitories are gaps in a wall; no turnstile capacity tuning yet.

## Watch list

- First restore: PDFsharp transitive packages (`Microsoft.Extensions.Logging.Abstractions`,
  `System.Security.Cryptography.Pkcs`) — MIT; record resolved versions.
- Bench numbers: which phase dominates at 20k+ people (likely review / plan / move); replay memory on the 30k demo
  (24 B × people × frames, 1 GB cap) and batch memory (cores − 1 engines).
- Stadium CHECK warnings (capacity at turnstiles during the surge are expected).

## Next

Round 026: tuning from the bench output (the hot phases), plus anything the report or SERVICES needs after use. Backlog:
video / GIF export of a replay; an across-runs nodes summary CSV; boarding-only services / batches / call-to-board time
(the "fuller model" option for SERVICES).
