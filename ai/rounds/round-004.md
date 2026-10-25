# Round 004 — Vertical circulation: connectors, shafts, Copy to Levels

**Status:** Built 2026-10-25, **unbuilt in the sandbox** (no .NET SDK or NuGet access again). Everything was written
and statically reviewed by hand, so expect a few compile fixes on the first Visual Studio build. Base: r003, which
built and ran with no issues.

## Query record (answers → decisions)

Answered 2026-10-24 (previous chat):

| Topic | Answer | Decision |
|---|---|---|
| Scope | 004 = stairs/escalators/ramps, shafts, Copy to Levels, demo; 005 = underlays, columns/cores, library placement | D-053 |
| Placing connectors | Preset + target level; click the bottom landing, aim; length from rise and slope | D-054 |
| Floor openings | Auto, linked opening on the upper level | D-055 |
| Shafts/voids | One element with base + top level | D-056 |
| Stair shapes | Straight (auto mid-landing) + switchback | D-057 |
| Switchback placement | Click + aim, Tab flips side | D-058 |
| Escalators | Pair preset = two linked connectors, each direction flippable | D-059 |
| Copy to levels | Copy to Levels… dialog, Ctrl+Shift+V, one undo step | D-060 |
| Demo | Full vertical set, auto openings replace voids, lift shaft element | D-061 |
| Plan look | Orange, treads, UP arrow, belt line, half cut at mid-rise | D-062 |
| Editing | Move, rotate grip, SELECTION-tab properties | D-063 |
| File format | v2 + migration; old builds refuse v2 | D-064 |
| Clearance | 0.3 m, editable per connector | D-065 |
| CHECK | Landing off walkable (E), overlap (E), too steep/shallow (W), no vertical link (W) | D-066 |
| Shaft look | Hatched opening in plan, translucent box in 3D | D-067 |

Answered 2026-10-25 (this chat, all on the recommended option):

| Topic | Answer | Decision |
|---|---|---|
| Stair risers | 0.17 / 0.28, max 18 risers per flight | D-069 |
| Switchbacks | Shape property on any stair/ramp + a "Stair switchback 1.5 m" preset; 0.2 m gap | D-070 |
| Escalator geometry | 30°, 1.2 m flats each end, pairs 0.6 m apart | D-071 |
| Ramps | 1.2 m landing every 9 m; straight or switchback | D-072 |
| Rotate snap *(accepted default)* | 15° steps, Shift free | D-073 |
| Travelators *(accepted default)* | Not in the CONNECT picker this round | D-074 |

---

## What changed

### Model and file format (D-056, D-064)
- New `Connector` (`Core/Model/Connector.cs`): bottom/top level, bottom landing centre, run angle, width, direction,
  shape + return side, clearance, pair id, and geometry values copied from the preset (riser, going, max risers,
  landing, gap, gradient, max ramp run, incline, flats, pair gap). Preset edits never move placed connectors.
- New `Shaft` (`Core/Model/Shaft.cs`): outline + base/top level + tag. The outline's vertex order is kept as drawn,
  ready for front/rear lift doors (D-068).
- `Study` gains `Connectors` and `Shafts` plus helpers (`FindConnector`, `FindPartner`, `ConnectorsTouching`,
  `ShaftSpans`, `ShaftsSpanning`, `LevelsAscending`).
- `.throng` is now **format v2**. The v1 → v2 migration makes sure both lists exist; v1 files open unchanged.
  r003 builds already refuse files newer than v1 with a clear message (now also names the version they can read).
- `ConnectorTemplate` gains max risers, shape, switchback gap, landing, max ramp run, incline, flats and pair gap,
  plus `ChangesLevel`. The library gains **Stair switchback 1.5 m** (9 connector presets, 46 templates).

### Geometry (Core)
- `ConnectorLayout` (`Core/Geometry/ConnectorLayout.cs`) derives the pieces (flights, landings, ramp runs,
  escalator flats and incline, the switchback half landing), the footprint, the walking centreline and the top
  exit from a connector and the real rise. See the class remarks for the rules (D-057, D-069–D-072).
- `VerticalCuts` (`Core/World/VerticalCuts.cs`) gives what each level loses: the footprint on the bottom level,
  the linked opening on the top level, the footprint + clearance on levels in between, and shafts on every
  spanned level. `WalkableArea` subtracts them, so the green net outline and the m² readout follow.
- `WorldCompiler` emits one `Surface` per connector piece (u up the piece, linear slope) and, with a polygon
  library, adds the cuts to each floor's obstacles. Portals stay in M2.

### Editor (D-054, D-058, D-059, D-063)
- **CONNECT** tool (`T`, DRAW strip): PRESET and TO combos appear on the strip. Before clicking, a live preview
  follows the cursor; click the bottom landing, aim up the run (15° steps, Shift free), click to place. Tab flips
  a switchback; typing gives `x,y` for the landing, then an angle (or `dx,dy`). It stays in CONNECT for the next one.
  Double-clicking a stair/escalator/ramp in **LIBRARY ▸ VERTICAL** picks it and starts CONNECT.
- Escalator pair presets place two linked units; the click is their midpoint, the right unit runs up and the
  left runs down. Pairs move, rotate, copy and delete together; each unit's direction is set separately.
- **SHAFT** tool (`O`): two corners or a drag (`w,h` typed). New shafts span every level until base/top are set.
  Shafts get vertex grips and edge push/pull like regions, on any level they pass through.
- Selection now covers regions, connectors and shafts: click (smaller shapes win), window/crossing boxes, Ctrl+A,
  move with snapping, H/I hide/isolate, delete, copy/cut/paste/duplicate. A selected connector shows a round
  **rotate grip** past its far end.
- **SELECTION tab** panels for connectors (name, preset, bottom/top level, width, direction, shape, return side and
  gap, run angle, opening clearance, derived risers/flights/length and rise, pair note) and shafts (name,
  base/top level, tag, area and level count). Pair-shared values change on both units in one undo step.
- **Edit ▸ Copy to Levels… (Ctrl+Shift+V)** opens a themed level checklist (`Views/LevelPickerDialog`) and pastes
  the selection in place on every ticked level as one undo step (D-060).

### Render (D-062, D-067)
- Plan: connector symbols (orange outline, nosings, dashed escalator belt line, centreline arrow, a break line at
  mid-rise) with the lower level showing the part below mid-rise and the upper level the part above; UP/DN labels
  are drawn by the WPF overlay. The plan ghost includes the level below's symbols, so the whole stair reads.
  Levels a connector passes through show it dashed. Openings get a light orange outline.
- Shafts: dark fill and a diagonal cross on every spanned level in plan; a faint translucent box from base to the
  top level's ceiling in 3D.
- 3D: true sloped connector pieces (walking surface, 0.25 m stringers and underside, edges, nosing lines and
  handrail lines at 0.9 m).
- Floors with openings or shafts are filled with the holes cut out (plan and 3D), so you can see down the stair well.

### Validation (D-066)
- Errors: connector on a missing level or whose layout fails (e.g. top not above bottom); a landing not on walkable
  floor (probed 0.25 m beyond each end edge); overlapping connectors (only when their height ranges overlap);
  shafts on missing levels, upside down or without area.
- Warnings: stair risers outside 115–190 mm or goings outside 250–355 mm; ramps steeper than 1:14 or shallower than
  1:20; escalators steeper than 35° or shallower than 27.3°; a level with walkable area and no connector (only
  when there's more than one level).

### Demo (D-061)
- The hand-drawn "Void" obstacles are gone. New: **Street stair** (3.6 m, concourse → street, 29 risers in two
  flights), **Street escalator (up)/(down)**, **Platform stair** (1.5 m switchback, platform → concourse, the
  deliberately tight one), **Platform escalator (up)/(down)**, and the **Accessible lift shaft** (platform ↔
  concourse). The connector presets are embedded in the study library. The demo still validates with 0 findings.

### Tests
- 116 test methods (85 → 116): `ConnectorTests.cs` (layout rules, openings and clearance, shafts, validation,
  pairs, clone completeness, undo snapshots, level shifting, angle snapping, composite undo) and new IO tests
  (v1 migration, v2 manifest, demo vertical set, connector round trip). The world test now expects connector
  surfaces as well as floors.

---

## How to test (numbered checklist)

1. **Build** the solution; fix anything the static review missed (send me the error list).
2. **Run all tests** — expect 116 passing.
3. **Demo**: Ctrl+Shift+D. The ticker should say 6 connectors, 1 shaft, 0 errors, 0 warnings. Press **F7** — no findings.
4. **Plan, Concourse**: the street stair and escalators show their *lower* halves (treads, belt lines, UP labels,
   break line); the platform switchback and escalators show their *upper* halves (DN labels) inside dark openings
   with light orange outlines. The lift shaft is a dark box with a blue cross. The green net outline goes around
   all of them.
5. **Plan, Street**: the upper halves of the street stair and escalators show, but the openings are only at their
   top ends (x ≈ 18–23 m): lower down there's enough headroom under the slab, so the floor stays. Same on the
   Concourse for the switchback's half landing. **Plan, Platform**: the lower halves of the switchback and
   escalators, and the lift shaft.
6. **3D (F3)**: sloped stairs and escalators climb between levels; the floors have holes at the openings; the
   shaft is a faint blue box. Hide a level with its eye — connectors stay while either end level is visible.
7. **CONNECT** (`T`): on the Platform level pick *Stair 2400* and TO *Concourse*. The preview follows the cursor;
   click, aim (watch 15° steps, Shift for free), click. The ticker/label shows risers × riser height, flights, length.
8. Place *Stair switchback 1.5 m*: press **Tab** while aiming — the return run flips sides. Select it and press
   Tab again — it flips (undoable).
9. Place *Escalator pair*: two units appear. Drag one: both move. Rotate grip: both rotate about their midpoint.
   Change Width in SELECTION: both resize and stay 0.6 m apart. Set one unit's Direction to Up — only it changes.
10. **Rotate grip** on a stair: drag the round handle; readout shows the angle; Shift rotates freely. Undo/redo.
11. **SELECTION** panel: change Shape Straight ↔ Switchback, Return side, Gap, Clearance (opening grows/shrinks
    on the upper level), Top level (to a level two up: intermediate level shows a dashed outline and a cut).
    Try an invalid top level (below the bottom): the ticker refuses it.
12. **Validation**: drag a stair so its bottom landing is off the floor → F7 shows an error. Overlap two stairs →
    error. Delete every connector touching a level → warning. Set a ramp's level pair so the ramp is long, check
    its landings every 9 m.
13. **SHAFT** (`O`): drag a rectangle on the Concourse. SELECTION shows it spanning all 3 levels; set base and
    top; check the floors lose the shaft only on spanned levels. Reshape it with grips/arrows from another level.
14. **Copy to Levels**: select a few regions, a stair and a shaft on one level, Ctrl+Shift+V, tick two levels,
    COPY. One Ctrl+Z removes all the copies. Copying a connector to the top level reports it as skipped.
15. **Ctrl+C / Ctrl+V** a stair from the Platform to the Concourse: it becomes Concourse → Street (same span).
    Ctrl+D duplicates it one grid step away.
16. **Hide/isolate** (`H`/`I`) a connector or shaft: it leaves plan and 3D; Esc brings it back.
17. **Save → reopen** the demo (format v2) — connectors, pairs and the shaft come back. Open an r003 file — it
    loads with no connectors.
18. **Don't regress** (HANDOVER §8): demo clean, tests pass, shortcuts only while the viewport has focus, plan
    ghosts the level below, 3D shows all visible levels, Home re-frames, undo/redo everything, dirty marker,
    save/reopen round trip, offline viewport panel if GL can't start.

---

## Judgement calls (in the spirit of the decisions; easy to change)

1. **Opening extent:** the upper level loses every part of the connector with less than **2.2 m** headroom under
   it (`VerticalCuts.HeadroomM`, includes a slab allowance), plus the clearance on every side except the exit
   edge, which stays floor so people can step off. Short rises cut almost the whole run; tall ones only the top.
2. **Bottom level** loses the connector footprint from its walkable area (you step on at the bottom edge, you
   don't walk through or under it), but the floor fill there isn't drawn as a hole.
3. **Levels in between** (a connector spanning more than one storey) lose the footprint plus clearance and show
   the connector dashed.
4. **Shafts are inclusive**: base and top levels both lose the outline (right for lift shafts; for an atrium, set
   the base to the first level above the atrium floor).
5. **New shafts span every level** (lowest to highest) until base/top are set.
6. **SHAFT draws rectangles**; grips and edge arrows reshape them into any polygon afterwards.
7. **Pairs**: the click is the pair midpoint; the right unit (looking up) runs up. Levels, width, clearance and
   angle are shared; name and direction are per unit. Move/rotate/copy/delete include the partner.
8. **Rotate pivot** is the bottom landing (the pair midpoint for pairs); the grip sits past the far end of the
   footprint (which is the top landing on straight runs).
9. **Keys**: `T` = CONNECT, `O` = SHAFT; Tab also flips a selected switchback.
10. **Switchback split**: the first run takes the extra riser when the count is odd (ceil(N/2)); not editable yet.
11. **Placing** embeds a copy of the preset in the study library (D-016) outside the undo step (idempotent).
12. **CHECK ranges** as listed under Validation (NCC-style riser/going limits; 1:14–1:20 ramps; 27.3°–35° escalators).
13. **Floors with holes** are filled as horizontal strips (one Clipper intersection + difference per strip), only
    on levels that have openings or shafts.
14. **Demo platform stair** is the 1.5 m switchback, so the demo exercises switchbacks and stays "deliberately tight".
15. Old **"Void"-tagged obstacles** still draw and cut walkable as before (no migration), so r003 studies look the same.

## Accepted defaults (cheap to change)

- D-073: 15° rotate/aim steps, Shift free.
- D-074: travelators not offered by CONNECT (double-clicking one says why).

## Known limitations

- **Unbuilt**: no compiler was available; first build may need small fixes.
- Portals between floors and connector pieces (and agents using them) are M2.
- No L-shaped stairs; switchback split, headroom and plan cut height aren't editable.
- UP/DN labels show on the active level only (not on the ghost).
- Picking a connector uses its whole footprint on both levels, even though plan shows half on each.
- The landing check probes one point beyond each end edge, not the full landing width.
- Copy to Levels pastes in place only (no offset); copies keep the source names.
- Undoing a placement keeps the embedded preset copy in the study library (harmless).
- Lift cars, doors and dispatch are M4 (the shaft is ready for them, D-068).

## Watch list

- XAML: the new connector/shaft panels bind level combos with `SelectedValuePath="Id"`; check the selection box
  shows level names with the themed ComboBox template.
- Tab in the viewport: it's consumed only when it flips something, so focus navigation still works otherwise.
- Performance on big studies: connector layouts are recomputed for picking and snapping on every mouse move,
  and floors with holes add Clipper calls per scene rebuild. Fine at demo scale; cache if it ever stutters.
- Line endings: every `.cs` / `.xaml` file is CRLF (per `.editorconfig`); the docs keep their existing LF.

## Open questions (non-blocking, for round 005's Query phase)

1. Is 2.2 m headroom the right opening rule, and should it be editable per connector?
2. Should a connector's bottom-level footprint show as a hole in the floor fill too?
3. New shafts: keep "span every level", or default to the active level and the one above?
4. Should the switchback split (risers in the first run) be editable?

## Next

Round 005 (D-053): image underlays with two-point calibration (D-009), columns/cores spanning levels, library
placement of devices (and travelators). First, fix whatever this round's build reports.
