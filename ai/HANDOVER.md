# Throng — Handover to the next chat

**Written:** 2026-10-25, round 007 part 2 (r007o). **Brief:** v0.16. **Decisions:** D-001–D-137.
**State:** r007j ran on Gavin's machine (escalators good, wide stairs choked; diagnostics not pasted). r007k adds counterflow-gated stair lanes (D-132) and
higher escalator weighting (D-133: boarding-rate seed 0.8 × cap, stalled stair queues count in stair waits).
r007l (D-134) adds lanes on stair landings (1.5×) and a 6 m approach lane. r007m (D-135) adds a landing squeeze (30% push same-way on landings). r007n (D-136) adds a stall escape; r007o (D-137) carries people 0.6 m into a landing before turning (Gavin's diagnosis of the corner stall). r007k–o unbuilt; 177 tests expected.
**Start here:** `ai/PROTOCOL.md`, this file, then the end of `ai/rounds/round-007.md` (r007j, r007k).
BRIEF and DECISIONS only as needed.
**Next:** check r007k's diagnostics: saturated pair all on above 56% (else revert D-131 to 0.3 / 1.5 m); STAIR run
all on vs "no counterflow gate"; wide-stair test result. Then the Query phase for vehicle timetables and batches,
profile mixes, train door flows and the demo end to end.
**Tuning loop that works (use it, don't guess):** `Throng.Sim.Tests/Round007Tests.Diagnostics_Round007Rules` runs
the demo and an escalator pair with each `SimTuning` switch off in turn and prints arrived / stuck / where. Ask Gavin
to paste its output, change what it points at, add a switch for any new rule.
**Quota:** Gavin's daily quota is tight. Read only files a change touches, no Python replicas unless asked,
targeted doc edits, sync docs to the project with `project_write` from `local_path`.

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

## 4. Current state (end of round 006, unbuilt)

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
- Flow-field gradients at a grid edge need extrapolation, or agents stall at piece joints (fixed in r006 review).
- `EmissionRule` is an immutable record: never assign its init-only properties outside an initializer; use `with`.

## 6. Next: round 007

1. Done in round 007 part 1 (D-112–D-126): escalator ends, deferral, one-way stairs, floors hide agents, dead-end
   walls, stair lanes, closed footprint gaps, rule switches + diagnostics.
2. Round 007 part 2 r007i (D-127–D-130): comb spacing at metered belts, rate curves (`Core/Model/RateCurves.cs`,
   rule editor in SELECTION), format v5.
3. Then: tuning from diagnostics, vehicle timetables and batches, profile mixes, train door flows, demo end to end. Round 005's parked questions
   stay parked until after M2 (D-108).

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
| Clean script | `clean.bat` (repo root) |

## 8. Don't regress

- Demo loads clean (0 errors, 0 warnings) and all tests pass (175/176 on r007i; 177 expected for r007k).
- Test helpers live in classes named for what they test, not the file (`VerticalCutTests.TwoLevelsWithStair` is in
  `ConnectorTests.cs`): grep for the class before calling a helper.
- Demo PLAY runs: people cross all three levels; editing locks until RESET.
- Older files open with their old behaviour (v1/v2: single-level fixed regions, single-run switchbacks; v1–v3: no emission rules; v1–v4: flat rates).
- Editing shortcuts only fire while the viewport has focus; New from Demo is Ctrl+Shift+D (D-043).
- Plan view ghosts the level below; 3D shows all visible levels; Home re-frames.
- Undo/redo for every edit; dirty marker in the title; save → reopen round-trips.
- The viewport shows its offline panel (never crashes the app) if OpenGL can't start.
