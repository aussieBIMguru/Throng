# Round 001 — M0: Skeleton + 3D viewport

**Date:** 2026-10-24 · **Brief:** v0.5 · **Milestone:** M0 (BRIEF §16)

## Summary

First code drop. The solution, all six projects and three test projects exist; the WPF shell runs with a dark
SimX-flavoured theme, an OpenGL viewport with plan and 3D orbit cameras, `.throng` new/open/save, an undo stack, the
built-in template library, and the Interchange Station demo (levels and regions).

> ⚠️ **Not compiled before hand-over.** The build sandbox had no .NET SDK and no NuGet access, so this round was
> written and statically reviewed but never built. Expect a small number of compile fixes on first build, most
> likely around third-party API names (see *Watch list*). Send me the error list and I'll fix them in round 002.

---

## What's in it

**Core** (`Throng.Core`)
- Model: `Study`, `StudySettings`, `Level`, `Region` (walkable / obstacle / wall / zone + 8 zone kinds).
- 3D-first world types: `Surface` (with height function), `Portal`, `WorldModel`; `WorldCompiler` makes one floor
  surface per level for now.
- Geometry: `Point2`, `Point3`, `Bounds2`, `PolygonMath` (area, winding, centroid, containment, ear-clip
  triangulation), `IPolygonOps` interface for Clipper2 in M1.
- Template library (§22): typed templates for agent profiles, devices, connectors, lifts, schedules; `Distribution`;
  `TemplateLibrary` with deep-copy embedding; **45 built-ins** in `BuiltInLibrary` with deterministic IDs.
- `UndoStack` + `PropertyChangeAction<T>` with save-point dirty tracking.
- `StudyValidator` (structural checks; the full rule engine is M7).

**Sim / Analysis**
- `SimClock` (integer ticks, AM/PM display, day counter), `DeterministicRandom` (SplitMix64 streams).
- `FruinLevelOfService` (walkway / queue / stair tables, abstract labels).

**IO** (`Throng.IO`)
- `.throng` = zip of `manifest.json`, `study.json`, `library.json`; atomic temp-file save; created date preserved;
  newer-format files refused politely; migration hook (`FormatMigrations`) ready for format 2.
- `DemoStudyBuilder`: *Demo: Interchange Station*, with Street (0 m, irregular hall), Concourse (−5 m) and
  Platform (−11 m); voids, columns, kiosk, gate-line barriers, a wall, queue/measurement/dwell/wait/slow/spawn-sink
  zones, train door zones; embeds 5 agent profiles and 5 schedules.

**Render** (`Throng.Render`)
- OpenGL 3.3 core renderer (one flat-colour shader), `SceneBuilder` (floors, extruded obstacles, void openings with
  balustrades, walls, translucent zones, outlines, 1 m / 5 m plan grid).
- `PlanCamera` (ortho, zoom-at-cursor) and `OrbitCamera` (perspective, Z up).
- Plan view shows the active level and ghosts the level below; 3D shows every visible level at true elevation,
  floors slightly translucent so stacked levels read through.

**App** (`Throng.App` → `Throng.exe`)
- Menu, square tool buttons with flavour-text tooltips, news-ticker status line, green study clock.
- Levels list (visibility toggles, active level), read-only library browser with colour swatches and tooltips,
  Study and Level property panels (all edits undoable).
- Dark title bar, crash boundary with log at `%LocalAppData%\Throng\Logs\throng.log`, viewport shows an
  "offline" panel instead of crashing if OpenGL can't start.
- **App icon:** original pixel-art crowd of five agents in the theme primaries, one with a red frustration "!",
  on a dark tile with scanlines at large sizes. Multi-size `.ico` (16–256 px) for the exe and window, plus PNGs
  for the toolbar brand mark. Preview: `ai/rounds/round-001-icon-preview.png`.

**Tests**: 52 MSTest methods across geometry, undo, library, validation, clock, RNG, Fruin, file round-trip,
corrupt/missing/newer files, and the demo (validates clean, compiles to 3 surfaces).

---

## How to test

1. Open `Throng.sln`, build, run (Throng.App is the startup project).
2. **Toolbar ▸ DEMO** (Ctrl+D). Expect the ticker to read *DEMO LOADED ▸ 3 levels, 50 regions, 10 templates ▸
   validation: 0 errors, 0 warnings*, and a 3D view of three stacked levels.
3. 3D: right-drag orbits, middle-drag (or Shift+right) pans, wheel zooms, Home re-frames.
4. **PLAN** (F2): the active level (Street by default, the 0 m level) with grid; the concourse below shows as
   faint ghost outlines. Click *Concourse* / *Platform* in LEVELS to switch. Wheel zooms at the cursor.
5. Untick a level's checkbox: it disappears from 3D. Ctrl+Z brings it back. Title shows `•` when dirty.
6. Edit the study name, start time (try `25:00` — rejected with a ticker message), seed, level elevation.
   Raising Platform above Street re-sorts the list. Undo/redo each.
7. **SAVE** to a `.throng`, close, reopen (also try double-click/command-line path). Everything should round-trip.
   Rename a `.throng` to `.zip` and look inside: three JSON files.
8. Expand LIBRARY categories; hover a template for its description.
9. **CHECK** (F7) on a new blank study shows "no walkable area" warning.
10. Test Explorer ▸ Run All.

---

## Known limitations (by design for M0)

- No sketching, selection or object placement yet (M1). Stairs, escalators, devices and the lift are not in the
  demo yet; their voids are.
- Walkable regions on a level are not yet unioned (Clipper2 arrives in M1), so overlapping walkable regions
  would draw on top of each other.
- No thumbnail in `.throng` files yet.
- The clock shows the start time only; it ticks once the engine lands (M2).
- The library browser is read-only; duplicate/edit/save-as-preset and `.thronglib` come in M3.
- Line width is 1 px (core-profile OpenGL limit).

## Watch list (most likely first-build issues)

| Area | Assumption made | If it fails |
|---|---|---|
| `GLWpfControlSettings` | Properties `MajorVersion`, `MinorVersion`, `RenderContinuously`; `Start(settings)`; `Render` event is `Action<TimeSpan>` | Adjust names to the restored package's API |
| OpenTK enums | `OpenTK.Graphics.OpenGL4`: `BlendingFactor`, `DepthFunction.Lequal`, `BufferUsageHint`, `GetProgramParameterName` | Rename to the package's enum names |
| Floating versions | `OpenTK.Graphics 4.*`, `OpenTK.GLWpfControl 4.*`, `CommunityToolkit.Mvvm 8.*`, `MSTest 3.*` | Pin once restored (record versions here) |
| Viewport on RDP/VMs | GLWpfControl needs the `WGL_NV_DX_interop` driver extension | The viewport shows its offline panel; rest of the app works |

## To do next round regardless

- Pin package versions and run the licence audit (`dotnet list package --include-transitive`); fill in the
  *(verify)* lines in `THIRD-PARTY-NOTICES.md`.
- Add VT323 (`src/Throng.App/Assets/Fonts/README.md`) if you want the retro font now.

---

## Open questions (not urgent; answer whenever)

1. **User library location:** `%AppData%\Throng\Library` (roams with your Windows profile) or a portable folder
   next to the exe? Default stays `%AppData%`.
2. **Demo building type:** Interchange Station leads; an office lobby and a stadium segment are noted for later.
   Happy with that order?
3. **M1 sketching:** plan view is the authoring surface. Do you also want basic picking/moving in the 3D view in
   M1, or leave 3D editing until M6?
4. **Default snap:** 0.5 m grid snap with endpoint/midpoint/orthogonal snaps on by default. OK?

## Next: M1 — Editor

Region sketching (rectangle, polygon, circle), snapping, select/move/copy/delete with undo, property panel for
regions, image underlays with two-point calibration, Clipper2 behind `IPolygonOps`, and stair/escalator placement
as sloped 3D surfaces. The demo gains its stairs and escalators.

## Post-hand-over build fixes

- **WPF projects drop `System.IO` from implicit usings** (it would clash with `System.Windows.Shapes.Path`). Added `using System.IO;` to `AppLog.cs`, `AppPaths.cs`, `RetroFont.cs` and `MainViewModel.cs`. Rule going forward: any Throng.App file using `Path`, `File`, `Directory`, `Stream` or `IOException` declares `using System.IO;` explicitly.
