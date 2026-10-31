# Round 018: the journey node editor

**018a status:** built 2026-10-31, **unbuilt in sandbox** (no .NET SDK, and package registries are blocked, so no
SDK could be installed; statically reviewed: bracket balance, XAML well-formedness, CRLF). **331 test methods** (318 +
13). **Format v11.** Agreed split (D-301): 018a now, 018b next; no further splits.

Gavin's 017b build: confirmed "perfect".

## Decided (D-301–D-316)

| # | What |
|---|---|
| D-301 | 018a: graph model, v11 conversion, simulation, CHECK, read-only canvas. 018b: canvas editing, device node settings, run overlay. |
| D-302 | The graph replaces the step list; format v11; older files convert to a chain of nodes, left to right. |
| D-303 | Spawn and Sink are markers; emission rules keep where / when / who. Sinks: rule's destinations, a sink zone, or board a train. Several sinks allowed. |
| D-304 | Profiles get a default journey; mix lines gain "(profile default)". Existing lines keep their journey. |
| D-305 | Branch nodes: Chance (2–6 weighted outputs, rolled per person) and Tag check (has / hasn't). Merges are several wires into one input. Needs / lacks convert to Tag checks. |
| D-306 | No loops (CHECK error). Device nodes carry their queue behaviour; gave up is an output (replaces jump to step N). |
| D-307 | (018b) Device node settings: patience, choose by, balk. |
| D-308 | The canvas takes over the viewport as a JOURNEY view; SELECTION shows the selected node. |
| D-309 | Own WPF canvas, no dependency. |
| D-310 | (018b) Counts on wires and waits on device nodes after a run. |
| D-311 | Capacity check splits flow down branches. |
| D-312 | Journey results: profile × journey rows plus a row per sink. |
| D-313 | Spawn tags stay on mix lines. |
| D-314 | Grants tag stays on device / zone nodes; a Set tag node grants or removes a tag. |
| D-315 | Docs to the project and the zip, in one step at the end. |
| D-316 | The 018a judgement calls below. |

## As built (018a)

**Model (D-302–D-306, D-314)**: `Core/Library/JourneyGraph.cs`
- `JourneyNode` (record): `Kind` (Spawn, Sink, Device, PassZone, Dwell, Chance, TagCheck, SetTag), `X` / `Y` (canvas
  DIPs), `Label`, `TargetTag`, `ZoneId`, `GrantsTag`, `DwellS`, `OnGiveUp`, `SinkKind` (Destinations / Zone /
  BoardTrain), `Weights`, `RemoveTag`; `StepKind`, `Title`, `Describe()`.
- `JourneyWire` (record): `From`, `Port`, `To`. Ports: Spawn 1; Sink 0; Device 2 (done, gave up); Tag check 2 (has,
  hasn't); Chance one per weight; others 1. Every node except Spawn has one input that takes any number of wires.
- `JourneyGraph`: `OutputCount`, `PortLabel`, `Percentages`, `Find`, `Spawn`, `Next` (first valid wire on a port),
  `Reachable` (breadth-first from Spawn), `FindLoopNode` / `HasLoop` (follows every wire), `Sinks`, `Problems`,
  `Summarise`, `Ensure` (converts a legacy step list once, or makes Spawn → Exit), `FromSteps`, `Builder` (for
  built-ins and tests), node factories.
- `JourneyTemplate`: `Nodes`, `Wires`; `Steps` is legacy, read only when loading older files (null afterwards, never
  written). `JourneyGiveUp.Branch` = take the gave-up output; `JumpTo` is for legacy steps only.
- `JourneyZones.Matching(study, node)`: zone nodes as before; a zone Sink matches sink zones (pinned, by tag, or any).
- Built-ins: the four round-009 journeys are converted from their old step lists (so they match what older files
  convert to); new **Gates then north or south train** (Has ticket? → machine → gate merge → Chance 50 / 50 → Board
  Northbound / Southbound).

**Profile default journey (D-304)**: `AgentProfileTemplate.DefaultJourneyId`, `MixLine.UseProfileJourney`,
`StudyTemplates.JourneyFor` / `JourneyIdFor` (used by validation, SimSetup, profile bundles, JOURNEY usage counts).
Built-in defaults: Commuter, Wheelchair, Event fan → Through gates; Infrequent, Luggage, Elderly, Family → Ticket then
gates; Staff → Straight to exit. The profile definition has a **Default journey** combo; mix line journey combos start
with **(profile default)**.

**Format v11 (D-302)**: `ThrongFile.CurrentFormatVersion = 11`; `FormatMigrations.UpgradeV10ToV11` converts step lists
and gives embedded built-in profiles the built-in's default (so they don't show MODIFIED). Load and Save both call
`JourneyGraph.Ensure` on every library journey. `.throngprofile` bundles are format 2 (`ProfileFile.Load` converts
format 1); a bundle carries the profile's default journey first.

**Simulation**: `Sim/SimJourneys.cs`, `SimSetup.CompileJourney`, `Engine/SimEngine.Journeys.cs`
- Each node reachable from Spawn becomes a `SimNode` (Step / Chance / TagCheck / SetTag, with `Next` per output).
  Action nodes also become a `SimStep` (`Node`, `Next`, `GaveUpNext`, `IsSink`), listed in the order they're reached.
  `SimJourney.Start` is the node Spawn is wired to.
- `GoToNode` passes through branch nodes at once (Chance: one seeded roll; Tag check: by the agent's tags; Set tag:
  changes them), then sets the step's target. A step that can't be reached is passed by along its first output.
  Unwired outputs (−1) go to the rule's sinks.
- Zone sinks head for the quickest matching sink zone (rule's sinks if none can be reached); train sinks as before.
- Gave up: Branch with a wire carries on along it (not counted as gave up, like the old jumps); otherwise leave
  (counted).
- **Per-sink results (D-312)**: `JourneyResult.Sink` / `IsSink`; journeys with two or more sinks add a row per sink
  ("Journey ▸ Sink") under their row, plus "rule's sinks" when anyone left without reaching a sink node. journeys.csv
  gains a `Sink` column.

**CHECK**: `StudyValidator.ValidateJourney` + new rule id `journey-graph`. Errors: no Spawn or two, broken wires, two
wires from one output, loops, a Chance with fewer than 2 or more than 6 outputs or bad weights, an empty tag on a Tag
check or Set tag. Warnings: unwired outputs (including an unwired gave-up when patience is set to it) and nodes no one
reaches. Each node reachable from Spawn is checked for its device type, zones, sink zones or train services.
**Capacity (D-311)**: `CapacityCheck.WalkNode` follows paths: Chance splits by share, Tag check by the chance the tag
is held (then held / not held on each side), Set tag and grants set it, and merges just carry on. The cap is 512 paths
per mix line. `CapacityCheck.NodeShares` gives each node's share (tests, and 018b's overlay).

**App (D-308, D-309)**: `App/Views/JourneyCanvas.cs` (own `FrameworkElement` drawn in `OnRender`): nodes with a
coloured kind header, ports with labels (done / gave up, has / hasn't, 40 %), bezier wires, and broken wires dashed
red. Left-drag on empty canvas, middle-drag or right-drag pans; the wheel zooms; Home / F fits; a click selects a node.
`MainViewModel.Journeys`: `IsJourneyView`, toolbar **JOURNEY** (F5), Esc on the canvas, PLAN / 3D / ISO close it;
clicking a journey in LIBRARY opens it. SELECTION ▸ JOURNEY: name / description (yours only), DUPLICATE / DELETE, SHOW
GRAPH, the selected node's settings, the node list (click to select), and "Graph checks" (what CHECK will say).

## How to test (018a)

1. Build; all tests pass (331 methods). Demo and Office tower load with 0 errors; CHECK on the demo has no
   journey-graph findings.
2. LIBRARY ▸ JOURNEYS ▸ Ticket then gates: the JOURNEY view opens over the viewport: Spawn → Has ticket? → (has) Gate,
   (hasn't) TicketMachine one row down → Gate → Exit. Wheel zoom, drag pan, Home fits, Esc returns to the view you
   were in. JOURNEY is lit in the toolbar, PLAN / 3D / ISO aren't.
3. Click a node: it outlines cyan and SELECTION shows its kind, settings and where each output goes; clicking a row in
   the node list selects it on the canvas.
4. Gates then north or south train: Tag check, merge at Gate, Chance 50 % / 50 %, two SINK · TRAIN nodes. Put it on a
   demo inbound mix line (it embeds); PLAY: people board both services. RESULTS ▸ JOURNEYS shows "… ▸ Northbound" and
   "… ▸ Southbound" rows under it.
5. AGENTS ▸ Commuter: the Default journey combo shows Through gates. On a mix line pick "(profile default)" for a
   Commuter line: CHECK / PLAY use Through gates. Change the Commuter default: the line follows it. DELETE on a
   journey that's a profile's default is refused with a message.
6. Open a v10 study (e.g. one saved with 017b): it opens, journeys are graphs, embedded built-in profiles aren't
   MODIFIED, results match 017b's for the same seed (the conversion keeps the old behaviour).
7. Save, reopen: the graph, the profile default and "(profile default)" lines round-trip; the file is format 11.
8. .throngprofile from 016/017 imports (journeys convert); a new export includes the default journey.
9. journeys.csv has the Sink column.
10. Don't-regress list (HANDOVER §8).

## Judgement calls (D-316)

- **Layout of converted journeys**: 230 DIP columns, 120 DIP rows. A node behind a tag check sits one row down, so
  the "around it" wire runs along the top. Built-ins use the same conversion.
- **Backward give-up jumps** (which would loop) become Leave, noted at the end of the journey's description. Forward
  jumps become the gave-up output. Steps after the first exit / board-train step are dropped, as the old compiler did.
- **Gave-up output** isn't counted as "gave up" (like the old jumps). An unwired gave-up with Branch leaves (counted)
  and CHECK warns.
- **Unwired outputs** leave by the rule's sinks (warning, not error); nodes no one reaches warn and aren't checked
  for targets.
- **Two wires from one output** is an error; loop detection follows every wire; the run follows the first.
- **Zone sinks**: the quickest matching sink zone, else the rule's sinks.
- **New mix lines and rules still start with "None"**, so a blank study runs without gates; "(profile default)" is
  picked explicitly. Existing lines keep their journeys (D-304).
- **Built-in profile defaults** as listed above.
- **JOURNEY view**: F5 and a toolbar button; clicking a journey in LIBRARY opens it; Esc on the canvas, PLAN / 3D /
  ISO, CHECK's go-to, or selecting a definition closes it. Read-only apart from name / description.
- **Per-sink rows** only for journeys with two or more sink nodes. Duplicate sink labels are numbered ("Exit 2"). Sink
  rows count the finished only (stuck and in progress stay on the journey row).
- **Capacity**: at most 512 paths per mix line (the rest go straight to the rule's sinks). Tag bits are treated as
  independent, as in D-280.
- **Compiled step order** = breadth-first from Spawn (only diagnostics and labels see it).
- **Profile reconcile, drop**: the dropped node is taken out and its inputs carry on along its first output (a Tag
  check along "hasn't", since no one holds a tag the study doesn't use).
- `JourneyTemplate.Steps` stays as a legacy, read-only-on-load property; Load and Save normalise every journey to a
  graph.

## Accepted defaults

- None beyond the judgement calls.

## Known limitations (018a)

- No graph editing yet (018b): no drag, wire, add or delete. DUPLICATE plus name / description only.
- The heatmap legend can draw over the canvas while a heatmap is shown.
- Device choice and balk settings, and counts / waits on the canvas after a run, come in 018b (D-307, D-310).

## Watch list

- Results on v10 studies should match 017b for the same seed (the conversion is meant to be behaviour-neutral). The
  capacity check's connector loads can shift a little: people who skip a ticket machine now walk from the spawn, not
  from the machine.
- Canvas text uses Segoe UI with the retro font for kind headers; check it at 150 % DPI.
- Carried over: crowd stress at 2.5 p/m², hold points by adjacent lanes, gate throughput, shader cost when exploded,
  narrow-gap false positives round shafts.

## Open questions (for 018b)

- Node palette: drag from a palette strip on the canvas, a right-click "add node" menu, or both?
- Wiring: drag from an output port to a node's input; drop on empty canvas offers an add-node menu?
- Should Chance weights be edited as % (normalised) or raw weights?
- Undo granularity for node drags (one step per drag, recommended).

## 018a fix (after Gavin's build: compiles, 2 tests failed)

- `LibraryTests.BuiltIns_HaveExpectedCounts`: built-in total 50 → 51 (the new journey).
- `ThrongFileTests.Save_WritesFormatV10` → `Save_WritesFormatV11`, expects 11.
- Test-only changes; no source changes.

## Next (018a)

Round 018b: see below.

## 018b: targets, choose-by, editing (D-317–D-329)

**Status:** built 2026-10-31, **unbuilt in sandbox** (Gavin AFK; statically reviewed: brackets, XAML, CRLF).
**340 test methods** (331 + 9). Format stays **v11**: the new node fields are additive with defaults (D-169 pattern).
018a confirmed by Gavin: compiles, and the two count tests are fixed.

| # | What |
|---|---|
| D-317 | 018b scope; the run overlay (D-310) moves to round 019 (a re-scope, not a split). |
| D-318 | Device / zone nodes and zone sinks target a type / tag or a pick list of specific items, each with a weight. |
| D-319 | Choose by: Quickest, Along the way, Nearest, First free, Random by weight. |
| D-320 | Along the way = extra time compared with walking straight to the next node's targets. |
| D-321 | Odds between zones are weights in the node. |
| D-322 | Device nodes: when patience runs out, balk default / on / off, patience override. |
| D-323 | Key settings as dropdown chips on the node; all settings in SELECTION. |
| D-324 | Pick lists: checklist + weights in SELECTION, PICK IN PLAN (Enter / DONE returns). |
| D-325 | Add nodes from a palette strip (drag or click) or a right-click menu; a wire dropped on empty canvas offers the menu. |
| D-326 | Raw weights, % shown. |
| D-327 | Drag port → node to wire; drag a wire's end off to move / remove; click wire + Del; loops refused. |
| D-328 | Built-in journeys edited in place as an override (MODIFIED, RESET TO BUILT-IN). |
| D-329 | The 018b judgement calls below. |

### As built (018b)

- **Model**: `JourneyNode.{Picks (JourneyPick: Id, Weight), ChooseBy, Balk, PatienceS}`, `HasPicks`, `HasCandidates`;
  enums `JourneyChooseBy`, `JourneyBalk` (`Core/Library/JourneyGraph.cs`). `Core/Library/JourneyTargets.cs`
  (`Devices`, `WeightOf`, `MissingPicks`); `JourneyZones.Matching(node)` uses picks first.
  `Core/Library/JourneyEdits.cs`: `Create`, `Add`, `Move`, `Connect` (replaces the wire on that output; refuses
  loops, Spawn, self and bad ports, with a message), `Disconnect`, `Remove`, `Replace` (drops wires from outputs that
  went), `TogglePick`, `TryParseWeights`, `TryParseRange`. `StudyTemplates.CanEditJourney` (built-ins too).
- **Sim**: `SimStep.{ChooseBy, Weights, Balk, PatienceS, WeightAt}`. `SimSetup.BuildDevices` also builds picked
  devices whatever their type. `SimEngine.ChooseDevice(…, rule)` / `ChooseZone` apply the rule: Quickest is the old
  cost (same random draws as before); Along the way adds `OnwardS` (walk from the candidate to the nearest target of
  the next step, looked through branch nodes: Chance by its biggest share, Tag check by the person's tags; the rule's
  sinks when the next is an exit); Nearest is walk only; First free is expected wait (devices) or `ZoneLoad`
  (people heading for or in the zone) × 1000 + walk; Random by weight draws among reachable candidates. `TryBalk`
  follows the node's balk; `TryGiveUp` the node's patience.
- **CHECK**: picks that no longer exist (all gone: error; some: warning), random by weight with every weight zero
  (error). **Capacity**: random by weight splits the flow over reachable candidates by weight (`SplitByWeight`);
  the other rules use the nearest, as before.
- **App**: `Views/IJourneyCanvasHost.cs` (chips, options, edits); `Views/JourneyCanvas.cs` rewritten for editing
  (palette strip, chips, port and wire dragging, wire / node selection, Del, right-click menus, the add-and-wire menu,
  one undo step per drag; refit only when another journey is shown). `ViewModels/JourneyViewModels.cs`
  (`JourneyViewModel` is the host, looks the journey up by id; RESET TO BUILT-IN, MODIFIED / YOURS in LIBRARY);
  `ViewModels/JourneyNodeEditorViewModel.cs` (every node setting, candidate checklist with weights). `MainViewModel.
  Journeys`: `EditJourney` (clone → change → `CompositeAction` / `ReplaceStateAction`; first edit of a built-in embeds
  its copy), `ResetJourney`, PICK IN PLAN (`StartJourneyPick`, `FinishJourneyPick`, `HandleJourneyPick` in
  `OnEditorSelectionChanged`, Enter key binding, DONE button).

### How to test (018b)

1. Build; all tests pass (340). Demo loads with 0 errors.
2. LIBRARY ▸ JOURNEYS ▸ Through gates: the JOURNEY view shows a palette on the left and chips on the Gate node
   ("Use: any Gate ▾", "By: Quickest ▾"). Click "By:" ▸ Along the way: the row shows MODIFIED in LIBRARY, SELECTION
   offers RESET TO BUILT-IN; Ctrl+Z undoes, RESET puts it back.
3. Drag a node: it moves; one Ctrl+Z puts it back. Drag from Gate's "done" port onto empty canvas: the add menu,
   pick CHANCE: a chance node is added and wired. Wire its outputs to two new SINK nodes. Try wiring a sink back into
   Gate: refused (no outputs); wire a chance output back into Gate: refused, the ticker says it would loop.
4. Click a wire: it lights cyan; Del removes it. Drag from a node's input port: its last wire comes off; drop it on
   another node to move it, or on empty canvas to remove it. Right-click a node: DELETE NODE / UNWIRE.
5. Palette: click DWELL: a dwell node appears in the middle; drag PASS ZONE onto the canvas: one appears there.
6. Select the Dwell node: SELECTION lists every zone with a tick and a weight. Tick two Retail zones, weights 3 and 1,
   By: Random by weight; PLAY: about three in four go to the first.
7. PICK IN PLAN on a device node: plan view opens; click gates (any level): each click adds / removes one (ticker
   says how many); Enter (or DONE) returns to the graph. The chip shows "Use: <name> +1".
8. Device node: patience 30 s, if patience runs out: Leave; balk Off; PLAY on the demo with a busy machine: people
   give up after about 30 s and nobody switches machines mid-queue.
9. Zone sink with a pick list and Random by weight: people split between the picked sinks.
10. Save, reopen: picks, weights, choose-by, balk, patience and the overrides round-trip (still format 11).
11. While running: the canvas still pans and selects but doesn't edit; SELECTION is read-only.
12. Don't-regress list (HANDOVER §8).

### Judgement calls (D-329)

- "Default" balk = the run's balk setting (profiles have none). Patience override range 5–7200 s.
- First free: devices by expected wait, zones by people heading for or in them; walk breaks ties (wait / people ×
  1000 s added).
- Along the way looks one step ahead through branch nodes (Chance: its biggest share; Tag check: the person's tags;
  Set tag applied). The candidate's position is the device centre or the zone's middle cell.
- Random by weight ignores decisiveness noise and draws once per arrival at the node; weights of type / tag
  candidates are all 1.
- Balking always looks for the quickest other candidate of the node, whatever its rule.
- Picked devices are used whatever their type tag; picking clears nothing else, and USE TYPE / TAG clears the list.
  Choosing a type / tag on a chip clears the pick list; zone pins from older journeys show as "pinned" until changed.
- New device nodes take the study's first known device type (Gate when there's none); new tag nodes the tag "tag".
- The first edit of a built-in embeds its copy outside the undo stack (as in D-297); undoing leaves a pristine copy.
- PICK IN PLAN keeps the journey in SELECTION; clicks are taken from the plan selection, which then clears; Enter
  is a window key binding that only acts while picking.
- Right-drag pans, a right click without a drag opens the menu.

### Known limitations (018b)

- No multi-select or copy / paste of nodes; no auto-layout button.
- Chips draw one line each; long names are trimmed with an ellipsis.
- The heatmap legend can still draw over the canvas.
- The run overlay (wire counts, waits) is round 019.

### 018b fix (after Gavin's build: one test failed)

- `Round018bTests.Edits_ConnectReplacesAndRefusesLoopsAndSpawn`: moved to 123.46 (123.45 is 123.4499… as a double and
  rounds to 123.4). Test-only change.

### Next

Round 019: the run overlay on the journey canvas (D-310), plus whatever Gavin's 018b build turns up. Queries first.
