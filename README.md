# Throng

**Conceptual pedestrian simulation for architects.** Sketch a space, drop in devices, press play, read the pressure.

Throng is a Windows desktop app (WPF, .NET 10) for testing circulation in early-stage designs: concourses,
stations, lobbies, stadium edges. It favours fast setup and legible, "roughly right" behaviour over scientific
fidelity. Results are indicative only and are **not** compliance evidence.

> Status: **Round 020 built (unbuilt in sandbox, 362 tests) — your library across studies: SAVE TO MY LIBRARY, MINE rows in LIBRARY, embed on first use, DIFFERS / UPDATE FROM MY LIBRARY, rename / delete with a backup, `.thronglib` export / import.** Round 019 confirmed (builds, 346 tests pass).
> 009c adds stair effort and standing rest so people prefer escalators unless queued. Builds; one test parked (wheelchair gate, D-176). Next: trains (timetables, door flows).
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
| `T` CONNECT | Stair, escalator or ramp: pick a preset and a level above on the DRAW strip (or double-click one in LIBRARY ▸ VERTICAL), click the bottom landing, aim up the run (15° steps, Shift free), click. `Tab` flips a switchback's return side; type an angle for an exact direction |
| `D` PLACE | Device or travelator: pick a preset on the DRAW strip (or double-click one in LIBRARY), click its position, aim the service side (travelator: click the far end), click |
| Spanning | Obstacles and walls: set Top level in SELECTION (default "This level only"), Height = Fixed or FullStorey, untick levels to skip them. Shafts: untick "Lift stops here" |
| Underlays | LEVEL tab ▸ ADD IMAGE…; SELECTION ▸ SET SCALE (click two points, type the distance); drag, rotate grip, opacity, lock |
| `O` SHAFT | Shaft / atrium / void: two corners or a drag. It spans every level until you set base and top in SELECTION |
| Round grip | On a selected connector: drag to rotate (15° steps, Shift free). Escalator pairs move, rotate and resize together |
| Click / drag (select) | Click picks; Shift adds, Ctrl toggles; drag left→right = window, right→left = crossing; drag a region to move it |
| Grips | Drag a vertex square; click or drag an edge-midpoint diamond to insert a vertex; Del removes the selected vertex |
| Edge arrows | Drag the arrow outside an edge to push/pull it; neighbouring edges keep their angle |
| `H` / `I` / Esc | Hide the selection / isolate it / show everything again (view-only, not saved) |
| Type a number while drawing | `x,y` first point · `w,h` rectangle · radius · length or `dx,dy` for polygon/wall segments; Enter applies |
| Enter / double-click | Finish a polygon or wall (clicking the first point also closes a polygon) |
| Backspace / Esc | Remove the last point / cancel (again to return to Select) |
| Shift | Force ortho while drawing or moving |
| Ctrl+C / X / V / D / A, Del | Copy, cut, paste in place (active level), duplicate (+1 grid step), select all, delete |
| Ctrl+Shift+V | Copy to Levels…: paste the selection in place on every ticked level, one undo step. Connectors and shafts keep their span |

Snaps: 0.5 m grid, endpoint, midpoint and ortho (toggle on the DRAW strip). Snap points come from the active
level and the level below. Locked levels can't be edited but still snap. The green outline is the net walkable
area (walkable union minus obstacles, walls, connector footprints and openings, and shafts).

Connectors draw as architectural symbols in plan: the lower level shows the part below mid-rise, the upper
level the part above, with treads, a belt line on escalators and an UP / DN arrow. In 3D they're true sloped
pieces. Each connector cuts its own opening in the upper level (0.3 m clearance, editable).

---

## Views and CHECK (rounds 015–016)

| Key / control | What it does |
|---|---|
| `F2` PLAN · `F3` 3D · `F4` ISO | Plan (where you draw), perspective orbit, or orthographic 2:1 isometric of every visible level |
| `[` `]` (ISO) | Turn the iso view a quarter: SW, SE, NE, NW |
| STUDY ▸ VIEW | Clip above a level, a section box (six sliders, or drag its square handles in 3D), explode levels (0–30 m); 3D and ISO only, works while running |
| STUDY ▸ VIEW ▸ VIEWS | SAVE VIEW stores the mode, level, camera and cutaway in the study; GO restores it |
| `F7` CHECK | Every rule into the CHECK tab: click a row to go there, IGNORE what you've accepted |
| LIBRARY ▸ AGENTS | EXPORT… / IMPORT… a profile and its journeys as a `.throngprofile` |

People are drawn as flat discs when they're small on screen. Lift shafts have semi-transparent walls in 3D with the
landing doors left open.

## Running (round 006)

| Control | What it does |
|---|---|
| PLAY / PAUSE | The first PLAY checks the study (errors block and are listed), builds the navigation and starts; editing is locked until RESET |
| STEP | Pause and advance one 0.1 s step (starts a paused run when none is active) |
| RESET | End the run, clear the agents and unlock editing |
| ×½ ×1 ×2 ×4 ×8 MAX | Run speed |
| SELECTION ▸ EMISSION RULES | On a spawn zone: profile, people per minute, constant or Poisson, nearest sink or weighted chosen sinks |
| STUDY ▸ RUN | Start time, end (fixed duration or until stopped), seed + RE-ROLL, keep side |
| Staff only | On a connector in SELECTION: public agents won't use it |
| Drag from LIBRARY | Drop a device on the plan to place it; drop a stair, escalator, ramp or travelator to start it at the drop point |

The status bar shows speed, agents walking, emitted, arrived, mean journey time and stuck agents (no progress for
30 s: a flashing ring; removed after 120 s; people queueing, being served or dwelling are never counted as stuck). Agents are capsules coloured by profile in 3D and discs in plan.

---

## Solution layout

```
Throng.sln
├─ src/
│  ├─ Throng.Core        Model, geometry (Clipper2 behind IPolygonOps), library, undo + editing actions, validation, world
│  ├─ Throng.Sim         Clock, RNG, Nav (grids, portals, flow fields, route choice), Engine (social force), SimRunner
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
| `.throng` | Study: zip of `manifest.json`, `study.json`, `library.json` (file format v5 since round 007; v1–v4 files still open; underlay images inside `underlays/`) |
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
