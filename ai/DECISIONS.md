# Decision log

Numbered decisions for Throng. New decisions are appended; superseded ones are marked, never deleted.

| # | Decision | Why | Date |
|---|---|---|---|
| D-001 | UI framework is WPF (`net10.0-windows`) | Familiar, Windows-only is fine for a personal app; best fit for custom GL hosting. | 2026-10-24 |
| D-002 | Fresh OpenGL renderer; no code shared with other personal projects | Keeps Throng independent; renderer designed camera-agnostic from day one. | 2026-10-24 |
| D-003 | Agent target: a few thousand real-time; architecture designed for stadium-scale (tens of thousands) | Stadium filling is a target use case, so SoA agent storage, spatial hashing and headless runs are built in early. | 2026-10-24 |
| D-004 | 3D-first world model: surfaces + portals + portal graph from M0; connectors are surfaces; lifts are graph edges with 3D cars | Stairs and lifts are part of the movement core instead of a retrofit; a single level is just a one-surface graph. | 2026-10-24 |
| D-005 | Throng's own code licensed **MIT** (see §20) | Simple, well understood, keeps the author's copyright notice with the code. | 2026-10-24 |
| D-006 | GL hosting via **OpenTK + GLWpfControl** (WPF overlays over viewport) | D3DImage-based hosting lets WPF overlays (HUD, tooltips, rubber bands) draw over the viewport. | 2026-10-24 |
| D-007 | **Clipper2** for polygon booleans/offsetting from M1, wrapped behind a `Throng.Core` geometry interface; triangulation hand-rolled | Robust booleans/offsets on messy hand-sketched input; wrapped behind IPolygonOps so it can be swapped. | 2026-10-24 |
| D-008 | File extensions **`.throng`** / **`.throngprofile`** | Short, on-brand, reads well aloud. | 2026-10-24 |
| D-009 | Image underlays (two-point calibration) in M1; DXF tracing parked | Architects trace plans; images cover most cases without a dependency. | 2026-10-24 |
| D-010 | Balking/reneging in M3, driven by per-profile patience | Cheap to add and makes queue results more realistic. | 2026-10-24 |
| D-011 | Metric units only | Metric is the working unit; imperial can be a display layer later. | 2026-10-24 |
| D-012 | Fruin LoS letters by default, abstract Low→Critical labels as a toggle | Technical default with a client-friendly toggle. | 2026-10-24 |
| D-013 | Turnstiles/security/gates are configured pass-through nodes with library presets, no separate class | One node model keeps the editor, engine and validation small. | 2026-10-24 |
| D-014 | Bundle one SIL OFL retro display font for titles/toolbar only; body text uses system fonts | Retro flavour on titles only; body text stays readable. | 2026-10-24 |
| D-015 | Built-in demo study generated in code, available from M0 and extended each milestone; doubles as the round acceptance test | Every feature is testable without hand-modelling; doubles as the round acceptance test. | 2026-10-24 |
| D-016 | Template library: read-only built-ins + editable user library; studies embed copies of templates they use; `.thronglib` for library exchange | Fast setup from presets, full customisation, and portable study files. | 2026-10-24 |
| D-017 | Product renamed **Throng** (from WalkAbout, to avoid borrowing a culturally loaded term); extensions `.throng` / `.throngprofile` / `.thronglib` | Avoids borrowing a culturally loaded Australian term. | 2026-10-24 |
| D-018 | Retro font is VT323 (SIL OFL), Consolas fallback | CRT-terminal look that stays readable at toolbar sizes. | 2026-10-24 |
| D-019 | Built-in template catalogue defined in C# (compile-checked) rather than embedded JSON | Compile-checked values and stable IDs; still serialises to the same JSON as any library. | 2026-10-24 |
| D-020 | Agent profiles live in `library.json` with other templates; no separate `profiles.json` | Profiles are templates like any other; one library file is simpler. | 2026-10-24 |
| D-021 | Library browser is a tab strip by category plus a search box, replacing expanders | Expanders forced long scrolls; tabs keep one category on screen and search finds anything. | 2026-10-24 |
| D-022 | Level visibility and lock use eye / padlock glyph toggles, not checkboxes | Stock checkboxes read as Windows chrome; glyph toggles fit the theme and are recognisable. | 2026-10-24 |
| D-023 | Text inputs and data values use `Cascadia Mono, Consolas`; VT323 for headers/buttons/ticker only | Techy look while keeping numbers readable at small sizes. | 2026-10-24 |
| D-024 | Run controls are a toolbar RUN group next to CHECK (play/pause, step, reset, speed chips) | Keeps the SimX toolbar idiom; a scrubber bar can follow with replay in M8. | 2026-10-24 |
| D-025 | User template library lives in `%AppData%\Throng\Library` | Per-user and roaming; safe if the app is installed under Program Files. | 2026-10-24 |
| D-026 | M1 editing is plan view only; 3D stays view-only until M6 | Keeps M1 focused on solid sketching tools. | 2026-10-24 |
| D-027 | Every round runs **Query → Build** per `ai/PROTOCOL.md`: queries first (staged multiple-choice, recommendation first), answers logged as decisions, then only decided scope is built | Settles behaviour before code is written, so rounds don't build on guesses and rework drops. | 2026-10-24 |
| D-028 | Floating package versions are pinned from `obj/project.assets.json` (Throng.App, Throng.Render, one test project) included in Gavin's next hand-back | Gives exact resolved versions plus the full transitive list for the licence audit; this sandbox can't reach NuGet. | 2026-10-24 |
| D-029 | Stock `MessageBox` replaced by a themed `ThrongDialog` behind the existing `DialogService`; the startup crash notice stays stock | No stock chrome; callers don't change; the theme may not be loaded when startup fails. | 2026-10-24 |
| D-030 | Properties panel Visible/Locked use a retro on/off switch style; level rows keep the eye/padlock glyph toggles (D-022) | Gavin's choice: a labelled switch reads better in a property list than a bare glyph. | 2026-10-24 |
| D-031 | Library search matches across all categories; each tab shows its match count; the selected tab's list is filtered | Matches in other tabs never go unnoticed, and the tab layout stays. | 2026-10-24 |
| D-032 | Window title bar stays native with dark mode (the one allowed native element) | Keeps Win11 snap layouts, Aero snap and accessibility at no cost. | 2026-10-24 |
| D-033 | Locked levels are visible and snappable but not selectable or editable | Lets you trace against a fixed level without disturbing it. | 2026-10-24 |
| D-034 | Focus visual is a 1 px accent outline shown on keyboard focus only | Keeps keyboard navigation visible without mouse-click noise. | 2026-10-24 |
| D-035 | *(round 003 part superseded by D-052)* M1 split across two rounds: 002 = UI polish + core editor (sketch tools, snapping, select/move/copy/delete, vertex grips, typed dims, property panel, Clipper2); 003 = image underlays, stairs/escalators, palette placement | The sandbox can't compile, so smaller drops are easier to compile-fix. | 2026-10-24 |
| D-036 | Circles are stored as ordinary polygons; segment count from a 0.05 m max chord error, minimum 16 | One shape type everywhere; small and large circles both look right; vertices stay editable. | 2026-10-24 |
| D-037 | M1 includes vertex grips: drag vertex, edge-midpoint grip inserts a vertex, Delete removes the selected vertex; all undoable and snapping | Reshaping without redrawing is basic editor hygiene. | 2026-10-24 |
| D-038 | Typed dimension input while drawing (HUD field: `w,h` for rectangles, length along the cursor direction for segments, radius for circles; Enter applies) | Architects expect exact sizes; it also makes demo and tests precise. | 2026-10-24 |
| D-039 | Regions stay separate objects; per-level walkable area = union(walkable) − union(obstacles + thickened walls), recomputed on edit and drawn as an outline | Non-destructive; the original sketch is always kept. | 2026-10-24 |
| D-040 | A self-intersecting or zero-area polygon blocks finishing, with the reason on the HUD; duplicate and collinear points are cleaned silently | Invalid geometry never enters the model, and no surprise auto-repairs. | 2026-10-24 |
| D-041 | Ctrl+V pastes at the same XY on the active level; Ctrl+D duplicates on the same level offset by one grid step | Copying a core between levels is the common case; duplicates shouldn't land exactly on top. | 2026-10-24 |
| D-042 | Drag selection is window (left→right, fully inside) / crossing (right→left, touched); Shift adds, Ctrl toggles | Matches Revit/AutoCAD muscle memory. | 2026-10-24 |
| D-043 | New from Demo moves to **Ctrl+Shift+D**; Ctrl+D is Duplicate (D-041) | D-041 needs Ctrl+D; the demo is used less often while editing. | 2026-10-24 |
| D-044 | Clipper2 is referenced by Throng.Core (implementation `ClipperPolygonOps` next to `IPolygonOps`) | Core and Sim stay UI/GL-free and headless; Clipper2 is fully managed, so Core taking it doesn't affect that. | 2026-10-24 |
| D-045 | Right panel becomes SELECTION / LEVEL / STUDY tabs (library-tab style); selecting anything switches to SELECTION | Gavin mistook the level Visible switch for an object setting; one context on screen at a time removes that confusion. | 2026-10-24 |
| D-046 | Per-object hiding is temporary and view-only: H hides the selection, I isolates it, Esc / RESET VIEW restores; not saved, no effect on the sim | Gives object-level show/hide without hidden geometry silently simulating. | 2026-10-24 |
| D-047 | Edge push/pull via a separate arrow handle just outside each edge midpoint; drags along the edge normal, offset snaps to the grid step and snap points; the midpoint diamond still inserts a vertex | Fast reshaping without per-vertex edits, keeps insert discoverable. | 2026-10-24 |
| D-048 | Push/pull keeps neighbouring edges' directions (they extend or shorten along their own lines); falls back to a straight normal move when a neighbour is parallel | True push/pull: rectangles stay rectangles, angles are kept. | 2026-10-24 |
| D-049 | Lines render 2 px as screen-space quads in a shader, in plan and 3D, with 4× MSAA | Core-profile GL ignores line width; thicker, anti-aliased lines read cleaner. | 2026-10-24 |
| D-050 | LEVEL tab drops the Visible switch (the row eye is the only visibility control) and keeps the Locked switch | Visibility is clearly per level on the row; Locked changes what can be edited, so it stays prominent. | 2026-10-24 |
| D-051 | Cross-level elements (round 004): stairs/escalators/ramps; shafts/voids spanning levels as one element; copy/paste to several levels at once; columns/cores with base and top levels | Multi-level buildings shouldn't need the same thing drawn per level. | 2026-10-24 |
| D-052 | *(round 004 part superseded by D-053)* Round 003 = round 002 feedback (D-045–D-050) + package pinning; round 004 = image underlays, stairs/escalators, cross-level elements (D-051), library placement | Small, compile-friendly drops while the sandbox can't build. | 2026-10-24 |
| D-053 | Round 004 = stairs/escalators/ramps, shafts/voids, Copy to Levels and the demo's vertical set; round 005 = image underlays, columns/cores spanning levels, library placement of devices | All the multi-level work lands together; drops stay compile-friendly. | 2026-10-24 |
| D-054 | Connector placement: pick a preset and the target level, click the bottom landing centre, aim; run length is computed from the rise and the preset slope (escalator 30°, stair from riser/going, ramp 1:14); width from the preset; typed dims work | Fast, and geometry always matches the real level-to-level rise. | 2026-10-24 |
| D-055 | Connectors cut their own linked opening (upper part of footprint + clearance) from the upper level's walkable area and stay in sync when edited; no separate void object | Nothing to keep in step by hand; openings can't drift from their stairs. | 2026-10-24 |
| D-056 | Shafts/voids are one element: outline + base level + top level; subtract from walkable on every level in range; selectable and editable from any spanned level | Atria, lift shafts and stair wells are drawn once for the whole building. | 2026-10-24 |
| D-057 | Stair shapes: straight (auto mid-landing when risers exceed the preset's max per flight; landing length = width, min 1.2 m) and switchback (half landing). L-shaped later | Covers the common cases; switchbacks wanted now (Gavin's pick). | 2026-10-24 |
| D-058 | Switchback placement uses the same click + aim; the return flight sits alongside, Tab flips left/right; split, gap and half landing from the preset (editable) | One placement idiom for all connectors. | 2026-10-24 |
| D-059 | The escalator-pair preset places two linked connectors (up + down) side by side that move together; each has its own flippable direction | Matches how pairs are designed; AM/PM reversal follows with schedules. | 2026-10-24 |
| D-060 | Edit ▸ Copy to Levels… (Ctrl+Shift+V) opens a themed level checklist and pastes the selection in place on each ticked level as one undo step | Repeating cores and plates up a building in one go. | 2026-10-24 |
| D-061 | Demo gains its vertical set: street↔concourse wide stair + escalator pair, concourse↔platform stair + escalator pair, auto openings replacing the hand-drawn voids, and a lift shaft element (lift itself in M4) | The demo stays the acceptance test (D-015). | 2026-10-24 |
| D-062 | Connectors in plan: orange outline, tread lines, direction arrow with UP label, escalators with a dashed belt line; the lower level shows the bottom half and the upper level the top half, cut at mid-rise | Reads like a drawn architectural plan. | 2026-10-24 |
| D-063 | Connector editing: move (with snapping), a rotate grip at the top landing, and SELECTION-tab properties (preset, levels, width, direction, flip side, flights, clearance); no push/pull or vertex grips | Connectors are parametric, not free polygons. | 2026-10-24 |
| D-064 | Study file format v2: `connectors` and `shafts` arrays, format version bumped, v1→v2 migration (old files load with empty lists); older builds refuse v2 with a clear message | No silent data loss when an old build opens a new file. | 2026-10-24 |
| D-065 | Auto-opening clearance defaults to 0.3 m, stored and editable per connector | Room for a balustrade line. | 2026-10-24 |
| D-066 | CHECK adds: landing off walkable (error), overlapping connectors (error), too steep/shallow stair or ramp (warning), level with walkable area but no vertical link (warning) | Catches the setups that would break M2 movement. | 2026-10-24 |
| D-067 | Shafts draw as a hatched opening (diagonal cross) on every spanned level in plan, and as a faint translucent box from base to top in 3D | Reads as one vertical element. | 2026-10-24 |
| D-068 | Lifts (M4): each car has one or two doors (front/rear, through-car); at every served level each door is set to entry, exit, both or closed, with its own landing zone | Gavin's requirement: real lift lobbies use single-sided, through-car and enter-one-side/leave-the-other arrangements, which change queuing and flow. | 2026-10-24 |

## Round 001 implementation notes (not product decisions)

- Package versions float within their major (`4.*`, `8.*`, `3.*`) until the first successful restore, then get pinned.
- Undo stack lives in Throng.Core (UI-agnostic, unit-tested) rather than Throng.App.
- Analysis tests are hosted in Throng.Sim.Tests to keep the test project count at three.

- `clean.bat` at the repo root removes bin/obj/TestResults (keeps .vs) before zipping a handback.
