# Throng

**Conceptual pedestrian simulation for architects.** Sketch a space, drop in devices, press play, read the pressure.

Throng is a Windows desktop app (WPF, .NET 10) for testing circulation in early-stage designs: concourses,
stations, lobbies, stadium edges. It favours fast setup and legible, "roughly right" behaviour over scientific
fidelity. Results are indicative only and are **not** compliance evidence.

> Status: **Round 003 — panel tabs, hide/isolate, edge push/pull, 2 px lines + MSAA, pinned packages.**
> Next: round 004 — stairs/escalators/ramps, shafts, Copy to Levels (queries answered: `ai/rounds/round-004.md`).
> Round notes live in `ai/rounds/`, the product brief and roadmap in `ai/BRIEF.md`.
> **How rounds work:** queries first (with recommendations), then build only what's decided. See `ai/PROTOCOL.md`.

---

## Build and run

Requirements: Windows 10/11, Visual Studio with the .NET desktop workload, .NET 10 SDK, a GPU/driver with OpenGL 3.3.

1. Open `Throng.sln`.
2. Build (NuGet restores OpenTK, GLWpfControl, CommunityToolkit.Mvvm and MSTest).
3. Run **Throng.App** (it's the default startup project). The exe is `Throng.exe`.
4. Click **DEMO** (or File ▸ New from Demo, Ctrl+Shift+D) to load the Interchange Station demo.

The VT323 retro font is bundled (`src/Throng.App/Assets/Fonts`, SIL OFL); the UI falls back to Consolas if it can't load.

Tests: Test ▸ Run All Tests in Visual Studio, or `dotnet test` from the repo root.

Clean: close Visual Studio and run `clean.bat` to delete every `bin`, `obj` and `TestResults` folder (keeps `.vs`)
before zipping the project. `clean.bat /q` skips the pause.

---

## Editing (plan view, F2)

| Key / action | What it does |
|---|---|
| `V` `R` `P` `C` `W` | Select, Rectangle, Polygon, Circle, Wall (or the DRAW strip under the toolbar) |
| Click / drag (select) | Click picks; Shift adds, Ctrl toggles; drag left→right = window, right→left = crossing; drag a region to move it |
| Grips | Drag a vertex square; click or drag an edge-midpoint diamond to insert a vertex; Del removes the selected vertex |
| Edge arrows | Drag the arrow outside an edge to push/pull it; neighbouring edges keep their angle |
| `H` / `I` / Esc | Hide the selection / isolate it / show everything again (view-only, not saved) |
| Type a number while drawing | `x,y` first point · `w,h` rectangle · radius · length or `dx,dy` for polygon/wall segments; Enter applies |
| Enter / double-click | Finish a polygon or wall (clicking the first point also closes a polygon) |
| Backspace / Esc | Remove the last point / cancel (again to return to Select) |
| Shift | Force ortho while drawing or moving |
| Ctrl+C / X / V / D / A, Del | Copy, cut, paste in place (active level), duplicate (+1 grid step), select all, delete |

Snaps: 0.5 m grid, endpoint, midpoint and ortho (toggle on the DRAW strip). Snap points come from the active
level and the level below. Locked levels can't be edited but still snap. The green outline is the net walkable
area (walkable union minus obstacles and walls).

---

## Solution layout

```
Throng.sln
├─ src/
│  ├─ Throng.Core        Model, geometry (Clipper2 behind IPolygonOps), library, undo + editing actions, validation, world
│  ├─ Throng.Sim         Engine (clock and deterministic RNG in M0; movement from M2)
│  ├─ Throng.Analysis    Level of Service classification (heatmaps and stats from M5)
│  ├─ Throng.IO          .throng file format, migrations, demo study builder
│  ├─ Throng.Render      OpenGL renderer, cameras, scene building
│  └─ Throng.App         WPF shell (exe: Throng.exe)
├─ tests/                MSTest projects for Core, Sim (+Analysis) and IO
├─ ai/                   Working protocol, brief, decision log, handover and per-round notes (AI exchange folder)
├─ LICENSE               MIT
└─ THIRD-PARTY-NOTICES.md
```

Dependencies only point downward: `App → Render / Analysis / IO → Sim → Core`. Core and Sim have no UI or GL
references, so the engine can run headless.

---

## Files

| Extension | What |
|---|---|
| `.throng` | Study: zip of `manifest.json`, `study.json`, `library.json` |
| `.throngprofile` | Single exported agent profile (M7) |
| `.thronglib` | Template library exchange (M3) |

---

## Conventions

- C# latest, nullable on, file-scoped namespaces, XML doc headers, `_camelCase` private fields.
- No exceptions surfaced to the user: operations return `Result` / `Result<T>`; exceptions are caught at
  boundaries and logged to `%LocalAppData%\Throng\Logs\throng.log`.
- Every model edit goes through the undo stack.
- SI units internally (metres, seconds).
- Only permissively licensed dependencies (BRIEF §20); every one is listed in `THIRD-PARTY-NOTICES.md`.

## Licence

MIT © 2026 Gavin Nicholls. Third-party components: see `THIRD-PARTY-NOTICES.md`.
