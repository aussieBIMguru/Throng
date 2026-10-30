# Throng — Handover to the next chat

**Written:** 2026-10-30, end of the round 016 build. **Brief:** v0.34. **Decisions:** D-001–D-279.
**State:** 015 confirmed by Gavin (no issues). **Round 016 built, unbuilt in sandbox**: M7: CHECK tab with named rules,
per-study ignore, full compile + static capacity + escalator / unused device / narrow gap rules (D-271–D-274);
.throngprofile export/import with reconcile (D-275); saved named views (D-276); section box outline + 3D face handles
(D-277). **Format v10; 309 tests expected.** If Gavin reports 016 build errors, fix those first (shaders still only
compile at run time).
**Start here (next chat):** `ai/PROTOCOL.md` (AFK / manual-approval rule: ask permissions before the build, none
during it; agreed splits aren't split again), this file, then `ai/rounds/round-016.md`.
**After 016 (same chat, D-280–D-284):** three build fixes (Vector clash, unused devices judged from the study,
capacity check now per-minute queues with tag shares) and the lift courtesy system (step aside while riders get off,
3 s squeeze / 8 s force jam breaker, overflow band behind full landings); see `round-016.md` ▸ "016 fixes". Gavin
confirmed CHECK on the demo: 0 errors. **Unconfirmed:** the courtesy fix in a real run (ask Gavin first thing).
**Next: round 017, scope not yet queried.** Ask: (1) did the lift courtesy fix the jam; (2) the demo's staffed ticket
counters and information desk are flagged unused (correct: no demo journey has a TicketCounter or InfoDesk step) —
add journeys that use them, or remove them from the demo?; then round 016's open questions (M8 or tuning, auto
re-CHECK, capacity Info trimming) at the end of `round-016.md`.
Open watch items: crowd stress (D-194) barely shows at 2.5 p/m² with 1 m counting; hold points can sit in front of an
adjacent device's lane mouth; gate throughput with D-209/D-211 (diagnostics row "no lane clear").
**Quota:** Gavin's weekly quota ran to ~98% in round 010. Keep reads to the files a change touches, batch edits,
short replies, and sync docs with `project_write` from `local_path`.
**Tuning loop that works (use it, don't guess):** `Throng.Sim.Tests/Round007Tests.Diagnostics_Round007Rules` runs
the demo and an escalator pair with each `SimTuning` switch off in turn and prints arrived / stuck / where. Ask Gavin
to paste its output, change what it points at, add a switch for any new rule.
**Quota:** Gavin's daily quota is tight. Read only files a change touches, no Python replicas unless asked,
targeted doc edits, sync docs to the project with `project_write` from `local_path`.
**Gavin's wishes on record:** visual node editor for profiles/journeys (stretch, BRIEF §8.2); see people ride a 3D
lift car (M4, D-068); density heatmap (M5).

---

## 1. What Throng is (30-second version)

A standalone WPF / .NET 10 desktop app for **conceptual pedestrian simulation** aimed at architects: sketch spaces
on levels, place devices (ticket machines, gates, stairs, lifts…), run agents, read heatmaps and wait times.
"Roughly right", fast to set up, explicitly not a compliance tool. Dark theme with SimCity-era retro flavour.
Previously named WalkAbout; renamed **Throng** (D-017). Files: `.throng` (study), `.throngprofile`, `.thronglib`.
MIT licence, © 2026 Gavin Nicholls.

## 2. How we work (keep doing this)

**Canonical version: `ai/PROTOCOL.md`.** In short: every round is **Query → Build**. Ask queries first (staged
multiple-choice batches, recommended option first and labelled), log the answers as decisions, then build only what
was decided on the zipped project. Summary:

- **Exchange is always the whole zipped project.** Gavin attaches a zip; you return the full updated zip.
- Every round updates, at minimum:
  - `ai/rounds/round-NNN.md`: what changed, how to test (numbered checklist), known limitations, watch list, open questions, next.
  - `ai/BRIEF.md`: kept current, version bumped, change line at the top.
  - `ai/DECISIONS.md`: append numbered decisions (D-095 onwards) with a "why".
  - `README.md` and `THIRD-PARTY-NOTICES.md` when relevant.
- **Gavin builds and tests in his own Visual Studio** and reports errors back; fix them in the next round.
- Before handing back he runs **`clean.bat`** (repo root) to strip bin/obj/TestResults. Zips you produce should also
  exclude bin/obj/TestResults/.vs.
- **Ask questions in small staged batches** (multiple choice, recommended option first and labelled), before starting
  work that depends on the answers. Non-urgent questions go at the end of the round notes instead.
- Dependencies: permissive licences only (BRIEF §20 allow-list). Every shipped package, including transitive ones, goes
  in `THIRD-PARTY-NOTICES.md`; inspirations go in its Acknowledgements.

## 3. Code conventions (Gavin's preferences)

- Readable, best-practice, domain-suited, robust over hacks, well organised.
- XML doc headers on types and members, plus comments for non-obvious logic.
- Nullable enabled and used where it helps; file-scoped namespaces; `_camelCase` private fields.
- Explicit types where clearer; `var` when the type is obvious from the right-hand side (`new()`, casts).
- LINQ where performance allows, **never in the sim hot path**.
- **No exceptions surfaced to the user:** return `Result` / `Result<T>`; catch at boundaries and log to
  `%LocalAppData%\Throng\Logs\throng.log`.
- Every model edit goes through the undo stack (`MainViewModel.Apply<T>` → `PropertyChangeAction<T>`).

## 4. Current state

Round 009b (see `ai/rounds/round-009.md` ▸ "009b as built"): SELECTION ▸ JOURNEY editor opened from LIBRARY ▸
JOURNEYS (`App/ViewModels/JourneyViewModels.cs`, `MainViewModel.Journeys.cs`; built-ins read-only, DUPLICATE into the
study library), `JourneyStep.OnGiveUp` / `GiveUpStep` with `SimEngine.TryGiveUp` and `GaveUp`, profile
`DevicePreferences` used in `ChooseDevice`, `Core/Geometry/QueueLines.cs` shared by sim and editor, drawn queue as a
shared snake (`SimDevice.SnakeLine`, `PromoteSnakeHead`), DRAW QUEUE tool (`App/Editing/PlanEditor.Queue.cs`) and
slot dots, lane hold points (`SimDevice.HoldPoints`, `WaitForLane`), status bar counts.
Round 009a: journeys (`Core/Library/JourneyTemplate.cs`, four built-ins), tags
(need / lack / grant, ≤ 64 per run), mix lines on emission rules (`EmissionRule.Mix`; the old `ProfileId` is
`LegacyProfileId`, read only by the v5 → v6 migration), devices as route targets with automatic queue lines per
lane and side (`Sim/SimJourneys.cs`, `SimSetup.BuildDevices`), the journey/queue engine in
`Sim/Engine/SimEngine.Journeys.cs` (agent `AgentPhase`), format v6, demo per D-155/D-159, SELECTION mix grid.
Rounds 007–008 (see `ai/rounds/round-007.md`, `round-008.md`): connector-end crowd rules and diagnostics,
rate curves (format v5), mechanised preference per profile, add/remove levels, hover highlight, stress per agent
with PROFILE / STRESS colouring (snapshot 11 floats). Earlier state below.

### Round 006 (M2 movement core)

Round 006 adds the **M2 movement core** on top of r005 (which builds and runs):

- **Core:** `EmissionRule`/`SinkWeight` on `Region.EmissionRules`; `StudySettings.KeepSide`; `Connector.StaffOnly`
  (D-110); `World/ConnectorPortals` (piece edges, joints, floor ends, one-way rules); `WorldCompiler` emits portals;
  `Library/StudyTemplates` (profile and preset lookup); demand rules in `StudyValidator`.
- **IO:** format **v4** (`UpgradeV3ToV4`); demo emission rules (D-109) and a staff-only back-of-house stair.
- **Sim:** `Nav/` (grids, raster, builder, costs, Dijkstra fields, portal graph, route planner), `Engine/` (SoA agents,
  spatial hash, social force engine, stats), `SimSetup`, `SimModel`, `SimRunner` (own thread, snapshots).
- **Render:** `Gl/GpuAgents` + agent shaders; `SceneRenderer.SetAgents`.
- **App:** `ViewModels/MainViewModel.Run.cs` (RUN group, edit lock, CHECK gate, status, STUDY ▸ RUN),
  `ViewModels/EmissionRuleViewModels.cs`, `PlanEditor.IsLocked` / `DropAt`, viewport `AgentFeed` + drop target,
  LIBRARY drag start in `MainWindow.xaml.cs`.
- **Tests:** 163 methods (136 in r005). Throng.Sim.Tests now references Throng.IO (for the demo).
- Packages unchanged (no new dependencies).

r005 state, still true: spanning regions (`Region.TopLevelId`, `HeightMode`, `ExcludedLevelIds`), shaft "no stop"
levels, per-level stair circuits, devices and travelators (PLACE), underlays, format v3 features.
r004: connectors and shafts, `ConnectorLayout`, `VerticalCuts`, `WalkableArea`, connector symbols, CHECK rules.
r003: 6 projects + 3 MSTest projects; `App → Render/Analysis/IO → Sim → Core`; Core references Clipper2 (D-044);
packages pinned (D-028); OpenGL 3.3 via GLWpfControl with 4× MSAA; themed UI; LEVELS + LIBRARY left, SELECTION /
LEVEL / STUDY right.

## 5. Lessons learned (avoid repeating)

- The sandbox has had **no .NET SDK and no NuGet access** in every round so far, so code was hand-written and statically reviewed only.
  Check for `dotnet` first; if it's missing, review carefully and flag in the round notes that the round is unbuilt.
- **WPF projects don't get `System.IO` in implicit usings** (to avoid `System.Windows.Shapes.Path` clashes). Any
  Throng.App file using `Path`, `File`, `Directory`, `Stream`, `IOException` needs `using System.IO;` explicitly.
- **C# 14 first-class spans:** `array.Reverse()` binds to the in-place `MemoryExtensions.Reverse(Span<T>)` (void).
  Use `Enumerable.Reverse(array)`.
- GLWpfControl API worked as assumed: `Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 3,
  RenderContinuously = true })`, `Render` event `Action<TimeSpan>`.
- **Clipper2 C# API:** the single-set `Clipper.Union(PathsD, FillRule)` has no precision parameter. Use the
  subject + clip overload with an empty clip (`ClipperPolygonOps.UnionD`). `Difference`, `Intersect` and
  `InflatePaths` take `precision` as written.
- Use `CultureInfo.InvariantCulture` for any formatted number that ends up in names, files or tests.
- Rounds 004 and 005 compiled first time from static review. Keep doing: read every file you touch, keep edits
  in one place per concern, run a brace/XAML well-formedness check before zipping.
- Avoid `[]` collection expressions for `Dictionary<,>` (use `new()`); keep `.cs`/`.xaml` files CRLF.
- Count carefully in tests: N levels = N − 1 storeys (circuits, rises).
- New public types go in their own file. Scripted inserts must assert their anchor was found (r008: an enum
  meant for file scope landed inside an `internal` class because the anchor search failed).
- Flow-field gradients at a grid edge need extrapolation, or agents stall at piece joints (fixed in r006 review).
- `EmissionRule` is an immutable record: never assign its init-only properties outside an initializer; use `with`.
  Same for `MixLine`, `SpawnTag` and `JourneyStep`.
- r009a: no NuGet or .NET SDK in the sandbox again; the round was statically reviewed (brace check, CRLF) only.
- r016: CHECK findings need a rule id (`ValidationRules`) and, if they're about a place rather than an object, `LevelId`
  + `At`; new rule ids are stored in studies' ignore lists, so never rename one.
- r015: every shader shares `Version + Warp` / `Version + Clip` const chunks; a new vertex shader must pass `vWorld`
  (true position) and a new fragment shader must call `clipped`, and a level's own meshes draw with `LevelWarp`,
  everything spanning levels with the stretch, or exploded views tear.
- r014: re-review before building found two editing-lock gaps in a rushed round (013b): any new edit path (library
  click, drop) must check `_isRunActive` itself, because `MainViewModel.Apply` doesn't.
- r009b: same (pip and npm registries were blocked too, so no syntax tools). r010a: same. Python text edits read CRLF files as LF;
  re-normalise every touched `.cs` / `.xaml` / `.md` to CRLF before zipping.

## 6. Round history and next

1. Done in round 007 part 1 (D-112–D-126): escalator ends, deferral, one-way stairs, floors hide agents, dead-end
   walls, stair lanes, closed footprint gaps, rule switches + diagnostics.
2. Round 007 part 2 r007i (D-127–D-130): comb spacing at metered belts, rate curves (`Core/Model/RateCurves.cs`,
   rule editor in SELECTION), format v5.
3. r007p (D-138, D-139): mechanised preference per profile. Round 008 (D-140–D-147): levels, hover, stress.
4. Round 009 (D-148–D-176; 009c stair effort and standing rest, D-174, D-175; wheelchair gate parked, D-176): 009a built and confirmed (journeys + tags + mix lines + automatic device queues +
   format v6 + demo; 210 tests). 009b built (unbuilt in sandbox): journey editor in SELECTION with DUPLICATE,
   give-up pathway per device step, profile device preferences, drawn shared-snake queues with slot dots, lane-wait
   hold points, status bar stressed / queuing / gave up. **Next: trains** (timetables, batches, door flows).
5. *(history)* Round 008 asks: vehicle timetables and batches, profile mixes, train door flows, demo end to end
   (the plan), plus Gavin's r007p asks: **add/remove levels** (M1 gap, no command exists), **journey templates**
   (ticket machine → gate → train; BRIEF §8.2, M3 task lists), **crowd visuals** (frustration/stuck colouring,
   density heatmap; BRIEF §8.4, M5/M6), **hover highlight** in the editor. Stretch: visual node editor for
   profiles/journeys (BRIEF §8.2). Round 005's parked questions stay parked until after M2 (D-108).

Gavin also wants to **see people riding a 3D lift car** (M4, BRIEF §7.2) with a **1- or 2-door typology per
served level** (D-068). Shafts already carry "no stop" levels (D-087) for that.

## 7. File map (where things live)

| Area | Path |
|---|---|
| Model, geometry, library, undo, validation, world | `src/Throng.Core/{Model,Geometry,Library,Editing,Validation,World}` |
| Built-in templates | `src/Throng.Core/Library/BuiltInLibrary.cs` |
| Rate curves | `Core/Model/{EmissionRule,RateCurves}.cs`, `App/ViewModels/EmissionRuleViewModels.cs`, `SimEngine.CountArrivals` |
| File format, demo | `src/Throng.IO/ThrongFile.cs`, `DemoStudyBuilder.cs`, `FormatMigrations.cs` |
| Renderer, cameras, scene | `src/Throng.Render/{Gl,Cameras,Scene}` |
| Theme | `src/Throng.App/Themes/Theme.xaml` |
| Main window / VM | `src/Throng.App/MainWindow.xaml`, `ViewModels/MainViewModel.cs` |
| Viewport host + navigation | `src/Throng.App/Views/ViewportView.xaml(.cs)` |
| Logging, paths, font, dialogs | `src/Throng.App/Services/`, `Views/ThrongDialog.xaml` |
| Plan editor + overlay | `src/Throng.App/Editing/` (`PlanEditor.cs`, `PlanEditor.Vertical.cs`, `EditorOverlay.cs`); Core editing helpers in `src/Throng.Core/Editing/` |
| Connectors and shafts | `Core/Model/{Connector,Shaft}.cs`, `Core/Geometry/ConnectorLayout.cs`, `Core/World/VerticalCuts.cs`, `Core/Editing/{ConnectorFactory,StateActions}.cs`, `Render/Scene/ConnectorSymbols.cs`, `App/ViewModels/ConnectorViewModel.cs` |
| Devices and underlays | `Core/Model/{Device,Underlay}.cs`, `Core/Geometry/DeviceGeometry.cs` (also `UnderlayGeometry`), `App/Editing/PlanEditor.Objects.cs`, `App/ViewModels/ObjectViewModels.cs`, `App/Services/UnderlayImageCache.cs`, `Render/Gl/GpuUnderlay.cs` |
| Shaders | `src/Throng.Render/Gl/Shaders.cs` |
| Simulation | `src/Throng.Sim/Nav/` (grids, fields, routes), `src/Throng.Sim/Engine/` (agents, social force), `SimSetup.cs`, `SimRunner.cs` |
| Run UI | `App/ViewModels/MainViewModel.Run.cs`, `App/ViewModels/EmissionRuleViewModels.cs`, `Render/Gl/GpuAgents.cs` |
| Levels add/delete | `Core/Editing/LevelActions.cs`, `App/ViewModels/MainViewModel.Levels.cs` |
| Hover, drag ghost | `App/Editing/PlanEditor.cs` (`Hover`, `HoverLabel`), `PlanEditor.Objects.cs` (`DragGhost`), `EditorOverlay.cs` |
| Stress, colouring | `Sim/Engine/AgentStore.cs` (`Stress`, `HeldS`), `SimEngine.UpdateStress`, `Render/Gl/AgentColoring.cs`, agent shader |
| Journeys, mixes, queues | `Core/Library/{JourneyTemplate,JourneyZones}.cs`, `Core/Model/{EmissionRule,Device}.cs`, `Sim/SimJourneys.cs`, `Sim/SimSetup.cs` (devices, lines, zones, journeys), `Sim/Engine/SimEngine.Journeys.cs`, `Sim/Engine/AgentPhase.cs`, `App/ViewModels/EmissionRuleViewModels.cs` (`MixLineViewModel`) |
| Journey editor, give-up, preferences | `App/ViewModels/{JourneyViewModels,MainViewModel.Journeys,LibraryViewModels}.cs`, `Core/Library/{JourneyGiveUp,DevicePreference,StudyTemplates}.cs`, `SimEngine.TryGiveUp` |
| Queue layout, snake, hold points | `Core/Geometry/{QueueLines,QueueLineSet}.cs`, `SimSetup.BuildDevices/BuildHoldPoints`, `SimEngine.{TryJoinSnake,PromoteSnakeHead,WaitForLane}`, `App/Editing/PlanEditor.Queue.cs`, `EditorOverlay.DrawQueueDots` |
| Crowd-aware choice, progress stress | `Sim/Engine/SimEngine.Crowd.cs`, `Sim/Nav/RoutePlanner.cs` (`sightRadius`, `InSight`), `SimEngine.UpdateWaits/Review/PickSink/UpdatePace/UpdateStress`, `SimRunner.TryGetCrowdWait`, `PlanEditor.HoverLabel` + `IEditorHost.RunHoverInfo` |
| Trains | `Core/Model/TrainService.cs`, `Sim/SimTrain.cs`, `SimSetup.BuildTrains`, `Sim/Engine/SimEngine.Trains.cs`, `App/ViewModels/{MainViewModel.Trains,TrainServiceViewModels}.cs`, STUDY ▸ TRAINS in `MainWindow.xaml`, `EditorOverlay.DrawTrains` |
| Live and run heatmaps (M5) | `Sim/Engine/{DensityGrid,DensityLayer,DensityCellKind,DensityMeasure,SimEngine.Density}.cs`, `SimRunner.CopyDensity`, `Analysis/{HeatmapPainter,HeatmapMode,HeatmapRamp}.cs`, `Render/Gl/GpuHeatLayer.cs`, `Render/Scene/HeatLayerDraw.cs`, `SceneRenderer.SetHeatmap`, `App/ViewModels/MainViewModel.Heatmap.cs`, MAP chips + legend in `MainWindow.xaml` |
| Accessible devices | `Core/Library/{DeviceTemplate,AgentProfileTemplate,StudyTemplates}.cs` (`Accessible`, `NeedsAccessibleDevices`, `IsAccessible`), `Core/Model/Device.cs` (`AccessibleOverride`), `SimEngine.{AccessAllows,AccessAvoidS}`, `StudyValidator.ValidateAccessibleDevices`, `FormatMigrations.UpgradeV7ToV8`, SELECTION ▸ device switch |
| Results (011c) | `Sim/Engine/{SimEngine.Results,RunResults,DeviceResult}.cs`, `SimRunner.LatestResults`, `Analysis/ResultsCsv.cs`, `App/ViewModels/{MainViewModel.Results,DeviceResultRow,GraphMetric}.cs`, `App/Views/{TimeSeriesGraph,GraphData,GraphLine,GraphBand}.cs`, RESULTS tab in `MainWindow.xaml`, `ResultsGrid` styles in `Theme.xaml` |
| Zone and journey results, PNG (012) | `Core/Geometry/OrientedBounds.cs`, `Sim/SimZone.cs`, `SimSetup.BuildZones`, `Sim/Engine/{SimEngine.ZoneResults,SimEngine.JourneyResults,ZoneResult,JourneyResult}.cs`, `AgentStore.{ZoneBits,MaxWaitS,LeftEarly}`, `App/ViewModels/{ZoneResultRow,JourneyResultRow,ResultsTable,MainViewModel.Png}.cs`, `Render/Gl/OffscreenCapture.cs`, `ViewportView.CaptureAsync`, `App/Services/{PngExport,PngExportInfo,PngLegendRow}.cs` |
| Lifts (013a) | `Core/Model/{Lift,LiftDoorUse,LiftLevelDoors,LiftScheduleEntry,Shaft}.cs`, `Core/Geometry/{LiftLayout,LiftSide,LiftCarLayout}.cs`, `Core/Editing/LiftFactory.cs`, `NavWorldBuilder.AddLift`, `Nav/LiftTiming.cs`, `NavRoutes` (lift options), `Sim/SimLift.cs`, `SimSetup.BuildLifts`, `Sim/Engine/{SimEngine.Lifts,LiftCarStatus}.cs`, `AgentPhase.{LiftWait,LiftRide}`, `SimRunner.CopyLiftCarStates`, `App/ViewModels/{ShaftViewModel.Lift,LiftDoorRowViewModel,LiftScheduleRowViewModel}.cs`, `App/Editing/{LiftSymbolView,RunLiftCarView}.cs`, `PlanEditor.LiftSymbolsOn`, `EditorOverlay.DrawLifts` |
| Lifts 3D, priority, train shapes, tower (013b) | `Render/Scene/{LiftCarDraw,LiftCarMeshes}.cs`, `SceneRenderer.SetLiftCars`, `App/ViewModels/MainViewModel.LiftCars.cs`, `ViewportView.LiftCars`; `RoutePlanner.HasLiftPriority` + `priorityPenaltyS`, `SimEngine.{LiftPriorityPenaltyS,UpdateLiftPriorityPenalty,HasLiftPriority}`, `SimTuning.LiftPriority`; `TrainService.Shape`, `App/Editing/PlanEditor.Train.cs`, `MainViewModel.Trains` (DRAW TRAIN / AUTO); `IO/OfficeTowerBuilder.cs`, `MainViewModel.LoadDemo` |
| Lift finish-off (014) | `SimEngine.{TryLeaveLift,Barred,LiftGaveUp,AssignDestinations,AimBesideCarDoor,ReleaseDestinationWaiters,LiftDoorShare}`, `AgentStore.{LiftBar,LiftBarFloor,LiftGiveUpAt}`, `RoutePlanner.Choose(barredConnector)`, `SimTuning.{LiftPatience,DestinationDispatch}`, `SimRunner.CopyLiftDoorShares`, `Render/Scene/{LiftDoorDraw,LiftSceneDraw}.cs`, `LiftCarMeshes`, `MainViewModel.LiftCars.cs` (landing doors, level visibility), `OfficeTowerBuilder.BuildStair` |
| Views (015) | `Render/Cameras/OrbitCamera` (`Orthographic`, `SetIsoPreset`), `Render/Gl/{Shaders (warpZ, clipped), ShaderProgram (SetExplode/SetWarp/SetClip), SceneRenderer (View, PrepareView, LevelWarp, AgentPixelsPerMetre), GpuAgents (disc LOD)}`, `Render/Scene/ViewSettings`, `Core/Geometry/ExplodeMap`, `App/ViewModels/MainViewModel.View.cs`, `ViewportView` (`_isoCamera`, `View`, `IsoPreset`, `IsoRotateRequested`), STUDY ▸ VIEW in `MainWindow.xaml`, `Slider` style in `Theme.xaml` |
| Lift results, shaft walls (015) | `Sim/Engine/{LiftResult,SimEngine.LiftResults}.cs`, `AgentStore.LiftRideFrom`, `RunResults.Lifts`, `ResultsCsv.LiftsCsv`, `App/ViewModels/LiftResultRow.cs`, RESULTS ▸ LIFTS; `Core/Geometry/{ShaftWalls,WallOpening,WallPanel}.cs`, `Core/World/LiftOpenings.cs`, `LiftLayout.{CarHeightFor,DoorHeightShare}`, `SceneBuilder.BuildVertical` |
| CHECK, rules, capacity (016) | `Core/Validation/{StudyValidator (Tag, ValidationMessage.Rule/At/IgnoreKey), ValidationRule, ValidationRules}.cs`, `Analysis/Checks/{StudyCheck, CapacityCheck, NarrowGapCheck}.cs`, `Study.IgnoredFindings`, `App/ViewModels/{MainViewModel.Check, CheckRowViewModel}.cs`, CHECK tab in `MainWindow.xaml`, `ViewportView.ZoomTo` |
| Profile exchange (016) | `Core/Library/{ProfileBundle, ProfileReconciler, ReconcileKind, ReconcileAction, ReconcileItem, ReconcileChoice}.cs`, `IO/ProfileFile.cs`, `App/ViewModels/MainViewModel.Profiles.cs`, `App/Views/ReconcileDialog.xaml(.cs)`, `LibraryBrowserViewModel.SetProfiles`, LIBRARY ▸ AGENTS buttons |
| Named views, box handles (016) | `Core/Model/SavedView.cs`, `Study.Views`, `FormatMigrations.UpgradeV9ToV10`, `App/ViewModels/{MainViewModel.NamedViews, SavedViewRowViewModel}.cs`, `ViewportView.{WriteCamera, ReadCamera, HandleAt, DragBoxFace}`, `Render/Scene/SectionBoxMesh.cs`, `SceneRenderer.{DrawSectionBox, TryGetBoxHandles}`, `ExplodeMap.Unwarp` |
| Clean script | `clean.bat` (repo root) |

## 8. Don't regress

- Demo loads clean (0 errors, 0 warnings) and all tests pass (309 on 016; the wheelchair test expects > 95 % at the accessible gate, D-213).
- Test helpers live in classes named for what they test, not the file (`VerticalCutTests.TwoLevelsWithStair` is in
  `ConnectorTests.cs`): grep for the class before calling a helper.
- Demo PLAY runs: people cross all three levels; editing locks until RESET.
- Older files open with their old behaviour (v1/v2: single-level fixed regions, single-run switchbacks; v1–v3: no emission rules; v1–v4: flat rates; v1–v5: one profile per rule, no journey, straight to a sink; v1–v6: no train services, doors emit continuously; v1–v7: no accessible flags, except the built-in accessible gate and wheelchair profile, upgraded on load; v1–v8: no lifts; v9 before 013b: automatic train boxes; lifts saved with Destination before 014 never existed, the factory mapped it to Collective; v1–v9: no saved views, nothing ignored).
- Queuers, people being served and dwellers are never removed as stuck; device line membership is per agent (device, line, slot), never agent indices (the store swap-removes).
- Built-in journeys (and their embedded copies, same id) stay read-only; only `StudyTemplates.IsEditableJourney` ones are edited. Steps and step lists are replaced, never mutated.
- Editing shortcuts only fire while the viewport has focus; New from Demo is Ctrl+Shift+D (D-043).
- Plan view ghosts the level below and ignores STUDY ▸ VIEW (no clip, no warp); 3D and ISO show all visible levels; Home re-frames.
- Undo/redo for every edit; dirty marker in the title; save → reopen round-trips.
- The viewport shows its offline panel (never crashes the app) if OpenGL can't start.
