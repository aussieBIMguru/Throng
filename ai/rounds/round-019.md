# Round 019: the run overlay on the journey canvas

**Status:** Query phase done 2026-10-31 (D-330–D-338). **Built 2026-10-31 (unbuilt in sandbox: no .NET SDK; statically
reviewed, brace / XAML checks, CRLF).** Gavin's 018b build: compiles, all tests pass after the 018b fix; "node editor
is amazing". Format stays v11 (results only, nothing saved in the study). Judgement calls: D-339.

## Decided (D-330–D-338)

| # | What |
|---|---|
| D-330 | Round 019 = the run overlay (D-310), one build; docs + zip at the end in one step. |
| D-331 | Live while running, and kept after pause / end until RESET or the next run. |
| D-332 | Wires: "124 · 38%" label, thickness by share. |
| D-333 | Everyone on the journey by default; a profile filter on the canvas. |
| D-334 | PREDICTED / ACTUAL toggle: predicted shares from the capacity model beside what happened. |
| D-335 | Node stats: reached / finished; devices mean / 95th wait, gave up, balked; dwell nodes mean dwell time. |
| D-336 | Per node on the canvas; SELECTION lists each candidate device / zone with its share and wait. |
| D-337 | nodes.csv in the CSV export (one row per node and wire). |
| D-338 | People spawned after the warm-up only (as D-230). |

## Build plan (for the next chat)

**Engine (`Throng.Sim`)**
- Per compiled journey keep a map back to the model: `SimJourney.NodeIds` (compiled node index → `JourneyNode.Id`,
  reachable nodes in breadth-first order, `SimSetup.CompileJourney`). A wire is (from node id, port).
- Counters per (profile, journey, compiled node, port): increment on every hop in `SimEngine.GoToNode` (the port taken
  out of a Chance / Tag check / Set tag) and in `AdvanceStep` (done = port 0, gave up = port 1), and Spawn → Start.
  Only agents spawned after the warm-up (`SpawnTime >= WarmUpS`). Arrays sized at model build; no allocation in the
  hot path (LINQ never in the sim, HANDOVER §3).
- Node stats per (profile, journey, node): reached (entered `GoToNode` at it), finished (`CompleteStep`, or arrival
  at a sink), device waits (join → service start, reuse the `NoteWait` path; keep a small per-node list for the 95th
  as device results do), gave up (`TryGiveUp`), balked (`TryBalk` switched), dwell time (Dwell phase start → end).
  Per candidate: count and waits per device / zone target, for SELECTION (D-336).
- Copy out with `RunResults` (new `JourneyFlowResult[]`: journey index, profile index or −1 for all, node id, port,
  count, plus node rows), built in `CopyResults` like journey results; the app reads them through
  `SimRunner.LatestResults`.

**Prediction (D-334)**: `CapacityCheck.NodeShares` gives per-node shares; add per-wire shares (the share leaving each
port) and expose a helper that runs it per mix line of the compiled model, weighted by each line's share of its
emitter's rate, filtered by profile. Compute when the model is compiled (PLAY or CHECK) and cache it in the app.

**App**
- `IJourneyCanvasHost` gains overlay data: per wire (from id, port) → label text + share 0–1; per node → a short
  stats line ("312 in · 18 s mean / 41 s p95 · 6 gave up"). `JourneyCanvas` draws wire thickness 2–8 px by share,
  the label at the bezier midpoint (with a dark backing), the node line under the header.
- A canvas strip (top right): ACTUAL / PREDICTED chips, profile filter dropdown ("All profiles" + profiles on the
  journey's mix lines), and "no run yet" / "RUNNING" / "after the run" state text.
- `JourneyViewModel` refreshes overlay text on `MainViewModel.Results` updates (the RESULTS tick); stays after the
  run ends; clears on RESET.
- SELECTION node editor: a RUN section listing each candidate device / zone with its count, share and mean wait.
- `ResultsCsv.NodesCsv` → nodes.csv: Journey, Profile (or ALL), Node, Kind, Port, To, Count, SharePct,
  PredictedPct, MeanWaitS, P95WaitS, GaveUp, Balked, MeanDwellS.

**Tests**: counts add up at merges and splits (Chance 30 / 70 ≈ 30 / 70; a Tag check after Set tag = all one
way); warm-up people aren't counted; predicted shares match `NodeShares`; nodes.csv header and a row; profile filter.

## Watch list for the build

- The hop counters must not shift any random draws (results must stay identical to 018b for the same seed).
- Memory: (profiles × journeys × nodes × ports) longs; fine at demo size, check stadium-scale studies.
- The canvas redraws on every results tick while running; keep it to an invalidate, no layout.

## Open (ask at the top of the build if it matters)

- None. One build, no split (D-330).

## As built (019)

**Engine (`Throng.Sim`)**
- `SimJourney.JourneyId`, `NodeIds` (compiled node → `JourneyNode.Id`, reachable order), `SpawnId`, `NodeIdAt`
  (`SimSetup.CompileJourney`). `SimNode.PickPort` (Pick uses it: same single draw).
- `Engine/SimEngine.JourneyFlow.cs`: counters per journey row (profile × journey, the journey results' rows): people
  out of every port (Spawn's output in slot 0), and per node reached, finished, device waits (sum + list for the
  95th), gave up, balked, dwell (count + sum); per candidate: headed for, served, wait sum. Sized once in
  `BuildJourneyFlow` (after `BuildJourneyRows`), cleared by `ResetJourneyFlow` (from `ResetJourneyResults`).
  `FlowRow(i)` = −1 for no journey or warm-up spawns (D-338).
- Hooks (`SimEngine.Journeys.cs`): `StartJourney` (Spawn wire), `GoToNode` (reached; Chance / Tag check / Set tag
  port; a step nobody can reach counts on port 0; the chosen device / zone / door as a candidate), `AdvanceStep`
  (done = port 0, gave up = port 1), `CompleteStep` (finished), `BeginExit` (sink zone candidate),
  `UpdateQueue` (service start: node + candidate waits), `TryGiveUp`, `TryBalk` (the candidate count moves to the
  device switched to), Dwell (start in `JoinTime`, counted on completion); `RecordJourneyEnd` counts a sink's
  finished. Nothing draws a random number.
- Results: `JourneyFlowResult` (journey index / id / name, profile index or −1 = ALL, `Wires`, `Nodes`, `Started`,
  `CountOf`, `NodeOf`), `JourneyFlowWire`, `JourneyNodeStats` (with `Kind`), `JourneyCandidateStats`;
  `RunResults.Flows` (per journey: ALL, then each profile) and `FlowFor(journeyId, profile)`.

**Prediction (`Throng.Analysis`)**: `CapacityCheck.FlowShares` (node + per-port shares; `NodeShares` now wraps it),
`PredictedShares(model, journey, profile)` (mix lines on the journey weighted by rate × line share; null when none),
`JourneyShares` record. `StudyCheck.RunWithModel` returns the compiled model with the findings.

**App**
- `IJourneyCanvasHost`: `Overlay`, `SetOverlayPredicted`, `OverlayProfileOptions`, `ChooseOverlayProfile`;
  `Views/JourneyOverlay.cs`, `Views/JourneyWireLabel.cs`.
- `JourneyCanvas`: wires 2–8 px by share (13 frozen pens), labels at the bezier midpoint on a dark backing (unwired
  outputs: "→ count · %" right of the port), a stats line row (16 px) under every header while there are node
  lines, the strip top right (state text, ACTUAL / PREDICTED, profile ▾ menu). Redraws on `Revision` only.
- `JourneyViewModel`: builds the overlay from `MainViewModel.OverlayResults` (actual) or `PredictionModel`
  (predicted), rebuilt only when results version / run state / mode / filter / model change (`RefreshOverlay`, from
  `RefreshRunStatus`); node lines by kind (`NodeLine`).
- `MainViewModel.JourneyFlow.cs`: `PredictionModel` (PLAY's model, or CHECK's when not running; dropped on any
  study edit or a new study), `OverlayRunState` (NO RUN YET / RUNNING / PAUSED / AFTER THE RUN).
- SELECTION ▸ node ▸ RUN (`JourneyNodeEditorViewModel.RunSummary/RunRows`, `JourneyRunRow`): outside the
  read-only settings panel so it stays readable while running.
- LIBRARY ▸ JOURNEYS stays clickable while running (`LibraryBrowserViewModel.IsJourneysTabSelected`, style trigger
  on the list); double-click does nothing while running.
- `ResultsCsv.NodesCsv` / nodes.csv (`NodesHeader`), with PredictedPct from the run's model.

**Tests**: `Throng.Sim.Tests/Round019Tests.cs` (6): chance wires ≈ 30 / 70 and add up to arrivals at the sinks, sink
finished = per-sink journey row; Set tag → Tag check all one way; warm-up spawns not counted and the count equals
finished + stuck + in progress; profile rows sum to ALL; predicted shares = `NodeShares` with 30 / 70 wires;
nodes.csv header, column count, Spawn and Chance rows, PredictedPct. **346 test methods.**

## How to test (019)

1. Build; all tests pass (346). Demo loads with 0 errors.
2. Demo: LIBRARY ▸ JOURNEYS ▸ Ticket then gates (the canvas opens). The strip top right says NO RUN YET with ACTUAL
   lit and PREDICTED dim; click PREDICTED: the ticker asks for CHECK or PLAY.
3. CHECK, then PREDICTED: wires show shares ("100%", the Has ticket? split), nodes "NN% reach", the strip says
   PREDICTED · FROM CHECK. Move a node (an edit): the prediction drops until the next CHECK / PLAY.
4. PLAY with the canvas open: ACTUAL counts tick up live ("124 · 38%"), busy wires thicken; each node has a green
   stats line (devices "N in · S s mean / P s p95 · gave up · balked", dwell "s mean dwell", sinks "N left").
5. While running, switch LIBRARY to JOURNEYS and click another journey: it opens read-only with its own overlay.
   Double-clicking does nothing.
6. Profile ▾: pick a profile: counts and shares are that profile's; All profiles goes back.
7. Select a device node: SELECTION ▸ RUN lists each candidate device with people / share / wait, readable while
   running. A Spawn node shows "N started".
8. PAUSE and let the run end: the counts stay (PAUSED / AFTER THE RUN). RESET: counts clear (NO RUN YET).
9. RESULTS ▸ EXPORT CSV: nodes.csv is written with the other five files; one row per node and per output, ALL rows
   then per profile, PredictedPct filled.
10. With a warm-up (STUDY ▸ RUN), counts start at zero until the warm-up ends.
11. Same seed, same results as 018b (RESULTS journey and device tables unchanged).
12. Don't-regress list (HANDOVER §8).

## Judgement calls (D-339)

- A step nobody can reach (passed by, as before) counts on its first output; its candidates don't count.
- Predicted device "gave up" outputs are 0 (the capacity model has no patience); predicted labels are shares only.
- Wire shares are of everyone who started the journey (Spawn's output = 100 %); candidate shares in SELECTION are of
  the node's candidate counts.
- A balk moves the person's candidate count to the device they switched to (count = where people headed last).
- The prediction is dropped by any study edit (it no longer matches the graph) and on a new study; CHECK or PLAY
  recomputes. A CHECK while running doesn't replace the run's model.
- LIBRARY ▸ JOURNEYS stays clickable while running so a journey's graph (and its overlay) can be opened live;
  every other tab stays locked, double-click and drag still refuse.
- The stats line adds one 16 px row under every node's header while there are node lines, so ports and chips move
  down together (hit tests use the same geometry).
- Unwired outputs someone took show "→ count · %" next to the port (they leave by the rule's sinks).
- Dwell time is measured from the start of dwelling to moving on (kept in `JoinTime`, which dwellers don't use).
- nodes.csv names repeat-labelled nodes "Gate", "Gate 2"; the Spawn row's Node and Kind are "Spawn"; an unwired
  output's To is "rule's sinks".

## Known limitations (019)

- The node stats line is trimmed with an ellipsis on narrow nodes (full numbers in SELECTION ▸ RUN and nodes.csv).
- Predicted shares follow the capacity model's branch walk (tags independent, no patience, no balking).
- A journey edited after PLAY can't be (editing is locked), but after CHECK the prediction drops on any edit.

## Next

Gavin's backlog (suggested order): cross-study `.thronglib` library (M3 leftover), seeded batch runs with averages
(M8), replay scrubbing (M8), stadium-scale performance pass + stadium demo, one-page PDF report, group movement.
