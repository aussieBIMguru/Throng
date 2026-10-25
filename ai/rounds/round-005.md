# Round 005 — Multi-level obstacles and walls, image underlays, device placement

**Status:** Done. Built 2026-10-25 (statically reviewed in the sandbox); Gavin compiled and ran it. One demo test
expected 3 circuits on the 3-level back-of-house stair; it has 2 (one per storey). The test was fixed in the zip. Base: r004 (Gavin: "all looks great,
no edits needed").

## Query record (answers → decisions)

| Topic | Answer | Decision |
|---|---|---|
| Scope | Multi-level obstacles/walls (replaces columns/cores), underlays, device placement | D-075 |
| Kinds that span | Obstacles and walls only | D-076 |
| Full-storey height | Up to the next level's floor; topmost uses its own floor-to-floor | D-077 |
| Defaults | Walls Full storey, obstacles Fixed 1.2 m | D-078 |
| Fixed + spanning | Repeats per level; Full storey is one continuous solid | D-079 |
| Span control | Top level combo in SELECTION, default "This level only" | D-080 |
| Upper levels | Normal look, editable from any spanned level | D-081 |
| New shafts | Keep spanning every level | D-082 |
| Underlay storage | Embedded in the .throng (`underlays/`) | D-083 |
| Calibration | Reference distance, then move + rotate grip | D-084 |
| Underlay display | Several per level, plan only, opacity / visible / locked | D-085 |
| File format | v3 | D-086 |
| Excluded levels | Shafts: no stop (still cut); obstacles/walls: absent | D-087 |
| Exclusion UI | Checklist in SELECTION | D-088 |
| Devices | Own footprint object with service side and lanes; queues in M3 | D-089 |
| Device placement | Like CONNECT; travelators too | D-090 |
| Stacked switchbacks | "One circuit per level" toggle, on by default for 2+ storeys | D-091 |
| Mixed storey heights | Each circuit fits its own storey | D-092 |
| Intermediate levels | Exit at each unless excluded (opening still cut) | D-093 |
| Connector exclusions | Same SELECTION checklist | D-094 |

## Build scope (as approved)

1. **Regions:** `TopLevelId` (null = this level only), `HeightMode` (Fixed / FullStorey), `ExcludedLevelIds`;
   `Shaft.ExcludedLevelIds` ("no stop"). Walkable cuts, plan, 3D, picking and editing honour the span.
   SELECTION: height mode, top level, level checklist. Validation for broken spans.
2. **Underlays:** import (PNG/JPG), embed, reference-distance calibration, move/rotate grip, opacity / visible /
   locked, per level, plan only, an UNDERLAYS list on the LEVEL tab.
3. **Devices:** `Device` object from `DeviceTemplate` (footprint, rotation, service side, lanes), placed like
   CONNECT, footprint subtracted from walkable, plan symbol + simple 3D box, SELECTION panel; travelators.
4. **Stacked switchbacks** (D-091–D-094): `Connector.PerLevelCircuits` + `ExcludedLevelIds`; `ConnectorLayout`
   builds one circuit per storey at the same start point; openings, plan symbols (per level) and 3D per circuit;
   CHECK landings on each served level.
5. **Format v3** + migration; demo gains spanning columns/core walls, an excluded level example, and devices
   (ticket machines, gate line, info desk) where the zones already are. Tests.

## Accepted defaults (cheap to change, unless Gavin says otherwise)

- Round 004 open questions: headroom stays 2.2 m (not editable yet); a connector's bottom footprint stays drawn as
  floor; the switchback split stays ceil(N/2).
- Underlay import formats: PNG and JPEG; images over 8192 px on a side are refused with a message.

---

## What changed

### Multi-level obstacles and walls (D-076–D-081)
- `Region` gains `TopLevelId` (null = this level only), `HeightMode` (Fixed / FullStorey) and `ExcludedLevelIds`.
  Only obstacles and walls use them (`Region.CanSpan`).
- `Study` helpers: `RegionPresentOn`, `RegionsPresentOn`, `SpannedLevels`, `RegionExtentOn`, `StoreyHeight`, `LevelAbove`.
  Fixed elements repeat at their height on each spanned level; Full storey runs from each level's floor to the next
  level's floor (the topmost level uses its own height), so a stack reads as one continuous solid.
- Walkable areas, the world compiler, plan/3D drawing, picking, snapping, moving and grips all use
  `RegionsPresentOn`, so a spanning element is selectable and reshapeable from any level it's on.
- New walls default to Full storey; new obstacles stay Fixed 1.2 m. SELECTION: Height mode, Height (fixed only),
  the floor-to-floor readout, Top level ("This level only" + the levels above) and an "On these levels" checklist.
- Copy/paste and Copy to Levels shift a region's base, top and exclusions by the same number of levels; a span
  past the top is clipped to the highest level.

### Excluded levels (D-087, D-088, D-094)
- Shafts: `NoStopLevelIds` ("Lift stops here" checklist). The floor is still cut; plan shows **NO STOP**.
- Obstacles/walls: unticked levels drop the element there.
- Per-level stairs: an "Exits at intermediate levels" checklist; an unticked level is still cut but has no exit
  (plan shows **NO EXIT**; CHECK skips its landings and its vertical-link test).

### Stacked switchbacks (D-091–D-093)
- `Connector.PerLevelCircuits` (new connectors: on; older files: off). A switchback stair or ramp spanning 2+
  storeys builds one circuit per storey at the same bottom landing, each fitted to its own storey's rise
  (`ConnectorLayout.Circuits`, `ConnectorCircuit`). The toggle shows in SELECTION when it applies.
- Each level gets the circuit arriving there (opening) and the one leaving (footprint of its actual pieces, so
  the previous circuit's exit stays floor). Plan symbols are drawn per circuit; CHECK probes every served landing.
- `Study.ConnectorsTouching` now includes a per-level stair's intermediate levels; `ConnectorServes` drives the
  vertical-link warning.

### Devices and travelators (D-089, D-090)
- New `Device` model (template copy, position, service-side angle, width × depth, lanes, pass-through, height,
  colour) and `DeviceGeometry`. Pass-through devices (gates, security, boarding) block only 0.1 m dividers between
  their lanes; the rest block their footprint.
- **PLACE** tool (`D`, DRAW strip with a PRESET combo; or double-click a device or travelator in LIBRARY): click
  the position, aim the service side (15° steps, Shift free), click; typed angle or `dx,dy`. Travelators: click
  the start, click the far end (length = distance, or type it). Rotate grip in front of the service side.
- SELECTION panel: name, preset/type, width, depth, lanes, facing, height. Plan: outline, lane lines, a service
  arrow; 3D: a box (or the dividers). CHECK warns when the service side faces no floor.
- Travelators are connectors with one `Belt` piece, `RunLengthM` long, on one level (OneWay by default).

### Underlays (D-083–D-085)
- New `Underlay` model; images live in `Study.UnderlayImages` and the zip's `underlays/` folder (PNG/JPEG, up to
  8192 px a side). LEVEL tab: an UNDERLAYS list (eye, lock, select) and **ADD IMAGE…**. A new image fits the level's
  drawing until it's calibrated.
- SELECTION: name, **SET SCALE** (click two points on the image, type the real distance, Enter; Shift squares the
  second point), width, rotation, opacity and Locked. Unlocked underlays move by dragging and rotate with a grip;
  locked ones can't be picked. Plan only, drawn just above the floor fill with its opacity.
- Renderer: a textured-quad shader and `GpuUnderlay` (textures reused while the image buffer is unchanged). The
  app decodes images with WPF (`Services/UnderlayImageCache`).

### File format v3 (D-086)
- `.throng` v3: region spans/height modes/exclusions, shaft no-stops, per-level stairs, travelators, `devices`,
  `underlays` plus image entries. v1/v2 files load with the old behaviour (single-level fixed regions, single-run
  switchbacks). r004 builds refuse v3 with the "newer version" message.

### Demo
- Columns are Full storey; **Lift core walls** span platform → concourse; the **Goods lift shaft** runs platform →
  street without stopping at the concourse; a **Back-of-house stair** (1.5 m switchback) climbs all three levels,
  one circuit per level. Devices: 4 ticket machines, 2 staffed counters, the 4-lane gate line and the accessible gate
  in the barrier gap (now closed by a middle barrier), and the information desk (replacing its obstacle).
- The demo should still validate with 0 findings.

### Tests
- 136 test methods (116 → 136): `Round005Tests.cs` (spans, heights, copy shifting, per-level circuits, exclusions,
  travelators, devices, underlay geometry) and IO tests (v3 manifest, v3 round trip incl. image bytes, v2 defaults,
  demo additions).

---

## How to test (numbered checklist)

1. **Build**; send me any errors. **Run all tests**: expect 136 passing.
2. **Demo** (Ctrl+Shift+D): ticker shows 7 connectors, 2 shafts, 9 devices, 0 errors, 0 warnings; F7 is clean.
3. **3D**: columns run floor to floor on every level; the lift core walls rise through two storeys; the
   back-of-house stair stacks three... two circuits (platform → concourse → street) at the same spot; the goods
   shaft box runs from platform to street.
4. **Plan, Concourse**: the back-of-house stair shows the arriving circuit's top half and the next circuit's
   bottom half; the goods shaft shows **NO STOP**; devices show outlines, lanes and service arrows.
5. Draw an obstacle on the Platform and set its Top level to Street: it appears on all three levels. Untick
   Concourse in "On these levels": it disappears there only. Switch Height to FullStorey and back.
6. Draw a **wall** (W): it's Full storey by default and reaches the level above's floor in 3D.
7. Move/reshape a spanning obstacle from an upper level; undo/redo.
8. **Per-level stair**: CONNECT a *Stair switchback 1.5 m* from Platform TO Street: two circuits. Toggle
   "One circuit per level" off → one long run; on again. Untick Concourse in "Exits at intermediate levels": plan
   shows NO EXIT; F7 warns only if that level loses its last link.
9. **Shaft no stop**: select the goods shaft, untick/tick "Lift stops here" levels.
10. **PLACE** (D): pick *Ticket machine*, click, aim, click. Rotate it with the grip; change lanes/size in SELECTION.
    Place a *Ticket gate line*: people-lanes stay walkable (green outline shows the dividers). Place a
    *Travelator*: click the start and the end; change Belt length in SELECTION.
11. **Underlays**: LEVEL tab → ADD IMAGE… (a PNG/JPEG plan). SET SCALE: click two points of a known dimension,
    type it, Enter; drag it into place, rotate with the grip, set opacity, lock it. Hide it with its eye. Save,
    reopen: the image comes back. Delete it and undo: it comes back.
12. **Copy to Levels** with a spanning obstacle, a device and a per-level stair selected: copies keep their spans.
13. **Open an r004 file**: walls stay fixed height, switchbacks stay one long run.
14. **Don't regress** (HANDOVER §8).

---

## Judgement calls (easy to change)

1. **Pass-through devices** (gates, security, boarding gates) block only 0.1 m dividers between and beside their
   lanes, so people can walk through; other devices block their whole footprint.
2. **Zone-type presets** (retail, seating, toilets, entrances, vehicle doors) aren't placed with PLACE; draw them as
   zones (linking zones to presets is M3). Double-clicking one says so.
3. **Device height** defaults to 1.2 m (editable); devices rotate about their footprint centre.
4. **Underlays** aren't copied by copy/paste or Copy to Levels, aren't box-selected or included in Ctrl+A, and are
   only picked when nothing else is under the cursor. Locked ones can't be selected at all.
5. A new underlay is scaled to the width of the level's drawing (or 50 mm/px on an empty level) until calibrated.
6. Underlay images stay in memory after a delete so undo restores them; only images of underlays still in the
   study are saved. Images are stored uncompressed in the zip (PNG/JPEG are already compressed).
7. The first circuit of a per-level stair cuts its rectangle footprint from the bottom level; later circuits cut
   only their pieces, so the exit of the circuit below stays floor.
8. Unticking a spanning element's **base** level is allowed (it's then absent there but editable from its other levels).
9. **Travelators** are one belt piece (no separate comb-plate flats) and default to one-way.
10. **SET SCALE** doesn't snap; Shift squares the second point to the first.
11. Key `D` = PLACE.

## Accepted defaults

- Headroom stays 2.2 m and isn't editable; a connector's bottom footprint is still drawn as floor; the switchback
  split stays ceil(N/2) (round 004 open questions).
- PNG and JPEG only, 8192 px max per side.

## Known limitations

- **Unbuilt**: no compiler was available.
- Devices have no queues or behaviour yet (M3). No device library editing.
- Underlays: no "align to plan" two-pair method; no crop; plan only.
- Full storey ignores slab thickness (D-077).
- Per-level stairs: all circuits share one width and shape; no per-storey overrides.

## Watch list

- `GpuUnderlay` uses `GL.TexImage2D` with BGRA bytes and mipmaps; check large images and older drivers.
- WPF image decoding of CMYK/indexed JPEG/PNG is converted to BGRA32; check an unusual export or two.
- Picking and snapping now include devices; check performance with many devices.

## Open questions (non-blocking, for round 006's Query phase)

1. Should underlays also show in 3D (on the floor plane), perhaps as an option?
2. Should devices snap their back edge to walls when placed?
3. Should per-level stairs allow a different width or shape per storey?
4. Slab thickness for Full storey heights: study-wide or per level, and when?

## Next

Round 006 / M2: movement across levels (nav grids, portals between floors and connector pieces, flow fields,
social force, spawn → sink through stairs and escalators, run controls). Fix whatever this round's build reports first.
