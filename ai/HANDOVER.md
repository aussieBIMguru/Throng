# Throng — Handover to the next chat

**Written:** 2026-10-24, after round 003 (built and run by Gavin with no issues) and the round 004 Query phase.
**Brief:** v0.10. **Decisions:** D-001–D-068.
**Start here:** read `ai/PROTOCOL.md` (base protocol: queries first, then build), this file, `ai/BRIEF.md`,
`ai/DECISIONS.md`, then `ai/rounds/round-004.md`. That file holds the answered queries and the build scope; the
next step is Gavin's go-ahead, then the build.

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
  - `ai/DECISIONS.md`: append numbered decisions (D-069 onwards) with a "why".
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

## 4. Current state (end of round 003)

**r003 compiles, runs and passes in Gavin's Visual Studio.** It has M0, the M1 core editor and the round 003 polish:

- 6 projects + 3 MSTest projects (85 test methods). Dependency direction `App → Render/Analysis/IO → Sim → Core`.
  Core references Clipper2 (D-044).
- **Packages pinned (D-028):** Clipper2 1.5.4, CommunityToolkit.Mvvm 8.4.2, OpenTK.GLWpfControl 4.3.6,
  OpenTK.Graphics 4.9.4. Test packages still float. `THIRD-PARTY-NOTICES.md` lists every shipped package; the
  copyright lines marked *(verify)* still need copying from each package's licence file.
- **Viewport:** OpenGL 3.3 via GLWpfControl, Plan (F2) / 3D orbit (F3), 2 px lines from a geometry shader with 4× MSAA
  (`Samples = 4` works on 4.3.6). The green outline is the net walkable area (Clipper2 union minus obstacles/walls).
- **Editor (plan only, D-026):** `src/Throng.App/Editing/PlanEditor.cs` (state machine) and `EditorOverlay.cs`
  (WPF immediate-mode overlay):
  - tools: rect, polygon, circle and wall;
  - snaps: grid, endpoint, midpoint and ortho, with Shift forcing ortho;
  - typed dimensions;
  - selection: window/crossing and hover;
  - move, vertex grips, midpoint insert, and edge push/pull arrows (`Core/Geometry/EdgePushPull.cs`);
  - clipboard: Ctrl+C/X/V/D/A and Del;
  - H/I hide/isolate (view-only) and locked levels.
  All model edits go through `MainViewModel.Execute`/`Apply` (undoable).
- **UI:**
  - Themed throughout: no stock controls, `ThrongDialog` replaces MessageBox, keyboard-only focus outline.
  - Left panel: LEVELS rows with eye/padlock toggles, and LIBRARY tabs with cross-category search.
  - Right panel: SELECTION / LEVEL / STUDY tabs, auto-switching to SELECTION.
  - A DRAW strip under the toolbar.
- `.throng` file format v1 (manifest/study/library JSON). Round 004 bumps it to v2 (D-064).
- Demo "Interchange Station": 3 levels, voids hand-drawn as obstacles tagged "Void" (replaced in round 004, D-061).

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

## 6. Next: round 004 (Query done → build on go-ahead)

Scope and every answer: `ai/rounds/round-004.md` (D-053–D-067). In short:
- Connectors: stairs (straight with auto mid-landing, plus switchback), escalators (linked pairs) and ramps. Placed by
  click + aim with auto run length, linked auto openings with 0.3 m clearance, orange plan symbols cut at mid-rise,
  and true sloped 3D.
- Shafts spanning base → top level. Copy to Levels… dialog. Validation additions. File format v2 with a migration.
- The demo gains its full vertical set and a lift shaft.

Round 005 (D-053): image underlays (two-point calibration, D-009), columns/cores spanning levels, and library
placement of devices.

Gavin also wants to **see people riding a 3D lift car** (planned in M4, BRIEF §7.2) and a **1- or 2-door entry/exit
typology per served level** (D-068, M4). Keep shafts lift-ready.

## 7. File map (where things live)

| Area | Path |
|---|---|
| Model, geometry, library, undo, validation, world | `src/Throng.Core/{Model,Geometry,Library,Editing,Validation,World}` |
| Built-in templates | `src/Throng.Core/Library/BuiltInLibrary.cs` |
| File format, demo | `src/Throng.IO/ThrongFile.cs`, `DemoStudyBuilder.cs`, `FormatMigrations.cs` |
| Renderer, cameras, scene | `src/Throng.Render/{Gl,Cameras,Scene}` |
| Theme | `src/Throng.App/Themes/Theme.xaml` |
| Main window / VM | `src/Throng.App/MainWindow.xaml`, `ViewModels/MainViewModel.cs` |
| Viewport host + navigation | `src/Throng.App/Views/ViewportView.xaml(.cs)` |
| Logging, paths, font, dialogs | `src/Throng.App/Services/`, `Views/ThrongDialog.xaml` |
| Plan editor + overlay | `src/Throng.App/Editing/`; Core editing helpers in `src/Throng.Core/Editing/` |
| Shaders | `src/Throng.Render/Gl/Shaders.cs` |
| Clean script | `clean.bat` (repo root) |

## 8. Don't regress

- Demo loads clean (0 errors, 0 warnings) and all tests pass (85 as of r003).
- Editing shortcuts only fire while the viewport has focus; New from Demo is Ctrl+Shift+D (D-043).
- Plan view ghosts the level below; 3D shows all visible levels; Home re-frames.
- Undo/redo for every edit; dirty marker in the title; save → reopen round-trips.
- The viewport shows its offline panel (never crashes the app) if OpenGL can't start.
