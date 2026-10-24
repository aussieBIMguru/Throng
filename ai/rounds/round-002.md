# Round 002 — UI polish + M1 core editor

**Status:** Build phase complete, **unbuilt**. This sandbox has no .NET SDK or NuGet access, so everything was
written and statically reviewed only. Expect a few compile fixes in round 003. Please send build errors back as text.
**Protocol:** Query → Build (`ai/PROTOCOL.md`, D-027). Gavin was away for the build, so the recommended options
were taken where nothing had been answered (see "Accepted defaults").

---

## Query record (answers → decisions)

| Topic | Answer | Decision |
|---|---|---|
| Package pinning | Gavin sends `obj/project.assets.json` (App, Render, one test project) | D-028 |
| MessageBox | Themed `ThrongDialog` behind `DialogService`; stock box only as a fallback | D-029 |
| Properties panel Visible/Locked | Retro on/off switch | D-030 |
| Library search | All tabs, match count per tab, current tab filtered | D-031 |
| Other stock-looking controls | Nothing else spotted yet; revisit if needed | — |
| Title bar | Keep native dark | D-032 |
| Locked level | Visible and snappable, not selectable or editable | D-033 |
| Focus visual | 1 px accent outline, keyboard only | D-034 |
| Round split | 002 = polish + M1 core; 003 = underlays, stairs/escalators, palette | D-035 |
| Circles | Polygon, 0.05 m chord tolerance, min 16 segments | D-036 |
| Vertex editing | Grips: drag, midpoint insert, delete | D-037 |
| Typed dimensions | Yes, HUD input in M1 | D-038 |
| Overlaps | Regions stay separate; walkable union is computed | D-039 |
| Invalid polygons | Block finish with a HUD reason; clean dupes/collinear silently | D-040 |
| Paste / duplicate | Ctrl+V same XY on active level; Ctrl+D +1 grid step | D-041 |
| Drag selection | Window / crossing; Shift adds, Ctrl toggles | D-042 |
| Demo shortcut clash | New from Demo → Ctrl+Shift+D | D-043 |
| Where Clipper2 lives | Throng.Core, next to `IPolygonOps` | D-044 |

## What changed

### A. UI polish
- **Level rows:** eye and padlock glyph toggles (`EyeToggle`, `LockToggle` styles) replace the stock CheckBox.
- **Properties ▸ LEVEL:** Visible and Locked are now retro on/off switches (`SwitchToggle`), and the level's
  net walkable area is shown in green below them.
- **Library:** the expanders are gone. Now there's a search box, then a tab strip (AGENTS / DEVICES / VERTICAL / LIFTS /
  SCHEDULES, each with a count), then one filtered list. Search matches every word against name, summary and
  description across all tabs, and jumps to the first tab with matches if the current one has none.
- **Fonts (D-023):** TextBoxes, ComboBoxes and data readouts (level summaries, library summaries, status counts,
  region size) use `Cascadia Mono, Consolas` (`MonoFont`, `DataText`, `DimDataText`).
- **New themed styles:** ComboBox/ComboBoxItem, CheckBox (fallback, so no stock box can appear), focus visual
  (`ThrongFocusVisual`, which also overrides the system default key), validation error border, tab strip, chip toggle.
- **ThrongDialog** (`Views/ThrongDialog.xaml`) replaces every MessageBox: save prompt (SAVE / DON'T SAVE /
  CANCEL), errors, validation results, About. The unhandled-exception dialog uses it too, with the stock box only as
  a fallback if the themed one fails.
- The Expander style is removed.

### B. M1 core editor (plan view only)
- **DRAW strip** under the toolbar: SELECT / RECT / POLY / CIRCLE / WALL chips, an "AS" combo box for the kind new
  shapes get (Walkable / Obstacle / Zone), snap chips GRID / END / MID / ORTHO, and the grid STEP.
  There's also a **Draw** menu and a set of **Edit** menu items.
- **Tools:** rectangle (two clicks or drag), polygon (click points, then Enter, double-click or the first point
  closes it), circle (centre then radius, stored as a polygon per D-036), wall (open polyline).
  Picking a draw tool switches to plan view.
- **Snapping** (`Throng.Core.Editing.Snapper`): endpoint, then midpoint, then ortho (free coordinate on the grid),
  then grid. Candidates come from the active level plus the nearest visible level below, locked or not. Shift
  forces ortho. Tolerance is 10 screen px at any zoom.
- **Typed dimensions (D-038):** type a digit, `.` or `-` while drawing and a field opens by the cursor.
  First point: `x,y`. Rectangle: `w,h` (or one value for a square), towards the cursor. Circle: radius.
  Polygon/wall: a length along the cursor direction, or `dx,dy`. Enter applies; Esc closes.
- **Selection (D-042):** click (smaller regions and edges win, so a column inside a floor picks the column),
  Shift adds, Ctrl toggles, window/crossing box. Hover highlight in the select tool.
- **Move:** drag a region. The nearest vertex is the grab point, so moves snap vertex-to-vertex. One undo step.
- **Vertex grips (D-037)** on a single selection: drag squares, click or drag the midpoint diamonds to insert, Del
  deletes the selected vertex. Invalid results are rejected with a HUD reason.
- **Clipboard (D-041):** Ctrl+C / X / V (in place, active level) / D (+0.5 m, −0.5 m), Ctrl+A, Del.
- **Validation on finish (D-040):** `ShapeValidator` cleans duplicate, collinear and spike points, then rejects
  `EDGES CROSS`, `NEED 3 POINTS`, `NO AREA` (< 0.01 m²). The reason flashes on the HUD and in the ticker, and the
  points stay so you can fix the shape.
- **Locked levels (D-033):** tools and selection refuse with "LEVEL LOCKED"; geometry still snaps.
- **Region property panel (SELECTION):** name, kind (closed shapes swap between walkable, obstacle and zone; walls
  stay walls), zone purpose and speed factor (zones), height (obstacles and walls), thickness (walls), tag, and an
  area/length and vertex readout. Every field is undoable.
- **Clipper2** (`ClipperPolygonOps : IPolygonOps`, D-007/D-044). Inputs are normalised to CCW before union, so the
  winding of a sketch doesn't matter. **Walkable union (D-039):** `WalkableArea.Compute` = union(walkable) −
  union(obstacles + thickened walls), drawn as a green outline on the active level. `WorldCompiler.Compile` now
  optionally thickens walls into obstacles.
- **Overlay** (`Editing/EditorOverlay`): WPF immediate-mode drawing over the GL view for selection, grips,
  rubber bands, shape previews, snap markers and a dimension readout. The GL scene is only rebuilt on commit.

### Tests
- `ShapeTests` (rectangle, circle chord error, clean/validate, segment intersection),
  `EditingTests` (snap priorities, picking, window vs crossing, add/remove/replace undo, naming, typed input),
  `PolygonOpsTests` (union, mixed winding, holes, wall thickening, walkable area). 28 new test methods.

## How to test (numbered checklist)

1. Run `clean.bat`, open the solution, build. Note any compile errors (expected: a few), and send them back.
2. Run all tests. The 52 existing tests must still pass; the 28 new ones should too.
3. **DEMO** (Ctrl+Shift+D) → CHECK still reports 0 errors, 0 warnings. F2 → the concourse shows a green
   walkable outline inside the cyan sketch, with holes around columns and voids. The LEVEL panel shows m² net walkable.
4. LEVELS: eye toggles visibility; padlock locks. Both are cyan when on and undoable. The properties panel has
   switches showing ON/OFF.
5. LIBRARY: click each tab; type `lift`, then `escal`, then `commuter`. Counts update, and the list jumps to a tab with matches.
6. Tab through the window: each focused control shows a 1 px cyan outline. Clicking with the mouse shows none.
7. NEW → F2 → `R`: draw a 10×6 rectangle by clicking, another by dragging, and another by typing `0,0` Enter `10,6` Enter.
8. `P`: draw an L-shape, closing it on the first point. Try a bow-tie: "EDGES CROSS" flashes and you keep drawing.
   Backspace removes points; Esc cancels.
9. `C`: draw a circle and type a radius of `3`. `W`: draw a 3-segment wall and finish with Enter. Check snapping to the
   rectangle's corners (magenta square), midpoints (triangle) and ortho (dashed line, or hold Shift).
10. `V`: click shapes, Shift-click, Ctrl-click, then drag a box both directions (blue solid = window, green dashed = crossing).
11. Drag a shape to move it (snaps vertex-to-vertex). Use the grips: drag a corner, click a midpoint diamond to insert,
    select a vertex and press Del. Try making the polygon cross itself: the change is rejected.
12. Ctrl+C on a level, select another level, Ctrl+V: it pastes in place. Ctrl+D duplicates offset by 0.5 m. Del deletes.
13. Change kind, zone purpose, height and thickness in SELECTION. Ctrl+Z steps back through each change and every draw/move.
14. Lock the level: drawing and selecting refuse with "LEVEL LOCKED"; drawing on the level above still snaps to it.
15. Save, close, reopen. Everything round-trips, the title dirty marker clears, and the unsaved-changes prompt is the themed dialog.
16. Don't-regress: 3D view still shows all visible levels; Home re-frames; the offline panel still appears if GL fails.

## Judgement calls

- New from Demo moved to Ctrl+Shift+D because Ctrl+D is Duplicate (D-043).
- Clipper2 lives in Throng.Core (D-044); its csproj comment no longer says "no third-party dependencies".
- Editing shortcuts only fire while the viewport has focus, so typing in a field never triggers them. The menus
  run the same commands from anywhere. Draw/snap chips aren't focusable, so clicking them keeps shortcuts live.
- New walls default to 3.0 m high and 0.2 m thick. New regions are named "Floor N" / "Obstacle N" / "Wall N" /
  "Zone N". Copies keep their source name.
- After drawing, the tool stays active (CAD-style) and the new shape is selected so its properties are on screen.
  Esc twice returns to Select.
- The library tab strip is a styled ListBox, not a TabControl (one shared list underneath). A TabControl style
  was deferred until one is actually used.
- File pickers stay as the Windows common dialogs. They follow the system theme and replacing them is out of scope.
- Paste, duplicate and delete apply to regions only. Nothing else is placeable yet.

## Accepted defaults (cheap to change; shout if not)

- Polygon/wall drawing: click adds a point, Enter or double-click finishes, clicking the first point closes a
  polygon, Backspace removes the last point, Esc cancels.
- Snap: 0.5 m grid with endpoint, midpoint and orthogonal on. Shift forces ortho.
- Clipper2 is on a floating `1.*` reference, pinned with the others from `project.assets.json`.
- Duplicate offset is (+step, −step), down-right on screen.

## Known limitations

- **Unbuilt.** Compile errors are likely (WPF XAML resources and the Clipper2 API calls are the riskiest; see watch list).
- Undo restores the model but doesn't restore the selection that existed before the change.
- The walkable outline is shown for the active level only, and only as an outline (no fill from the union yet).
- No snapping to the intersection of two edges, and no perpendicular or nearest snap.
- Moving or drawing never edits the level below; cross-level moves are done with copy/paste.
- The GL scene is rebuilt on every commit (fine at demo scale; revisit with M2 agent counts).

## Watch list (most likely to need a fix)

1. `ClipperPolygonOps`: Clipper2 signatures (`Clipper.Union/Difference/Intersect(PathsD, …, FillRule, precision)`,
   `Clipper.InflatePaths(paths, delta, JoinType, EndType, miterLimit, precision)`) and output winding. The
   `Difference_LeavesAHole` test checks the winding.
2. `Theme.xaml`: the `SystemParameters.FocusVisualStyleKey` override and the ComboBox template.
3. MDL2 glyphs on the chips (E8B3, E739, E70F, EA3A, E76F) and the toggles (E890/ED1A, E72E/E785). If a box
   shows, tell me which.
4. The typed-dimension box takes focus from the viewport. Check that Enter applies and focus returns to the viewport.
5. Line endings were normalised to CRLF for code files (per `.editorconfig`).

## Post-hand-back build fixes (r002b)

- **CS1503 ×12 in `ClipperPolygonOps.cs`:** Clipper2 has no `Union(PathsD, FillRule, int precision)` overload.
  The single-set PathsD union takes no precision argument, so the compiler fell back to the `Paths64` overload.
  All four unions now go through `UnionD(subject)`, which calls the subject + clip overload
  `Clipper.Union(subject, new PathsD(), FillRule.NonZero, Precision)`. Difference, Intersect and InflatePaths
  compiled as written.
- The CS0006 "metadata file … could not be found" errors in every other project were knock-ons from Throng.Core
  failing. They should clear with this fix. If any remain after a full **Rebuild Solution**, send them.

## Before round 003, please include

- `obj/project.assets.json` from `src/Throng.App`, `src/Throng.Render`, `src/Throng.Core` and
  `tests/Throng.Core.Tests` (D-028). After `clean.bat`, build once so `obj` is restored, then zip.
- The build errors and test results from step 1–2, and any UI that still looks stock.

## Open questions (not urgent)

1. Should undo also restore the previous selection?
2. Show the walkable union as a fill (with holes) instead of only the green outline?
3. Add intersection and perpendicular snaps in round 003?

## Next: round 003 (rest of M1)

Image underlays with two-point calibration (D-009), stairs and escalators as sloped surfaces through the demo's voids,
library palette placement, pinned packages and the licence audit. It starts with a Query phase.
