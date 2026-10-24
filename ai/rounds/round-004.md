# Round 004 — Vertical circulation: connectors, shafts, Copy to Levels

**Status:** Query phase complete (answered 2026-10-24 in the previous chat). Build phase waiting for Gavin's
go-ahead in the next chat (`ai/PROTOCOL.md`). Base: r003, which built and ran with no issues.

## Query record (answers → decisions)

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

## Build scope (once approved)

1. **Model:** `Connector` (preset key, kind, bottom/top level ids, bottom landing point, direction angle, width,
   flights/landings derived, switchback side, clearance, linked pair id) and `Shaft` (outline, base/top level ids),
   both on `Study`. File format v2 + migration (D-064), plus tests.
2. **Geometry (Core):** connector layout from the preset (riser/going or slope, max risers per flight → mid-landing,
   switchback return), producing per-flight sloped surfaces, footprints per level (lower/upper halves) and the
   opening polygon. `WalkableArea` subtracts connector openings and spanned shafts. Unit tests.
3. **Editor:** a CONNECT tool (preset picker built from the library VERTICAL tab, target level, click + aim, Tab flips,
   typed dims), pair placement, move/rotate grip, SELECTION panel. A SHAFT tool (draw an outline; base/top in the panel).
4. **Copy to Levels… dialog** (D-060).
5. **Render:** plan symbols (D-062) and shaft hatch; 3D sloped flights, landings and the translucent shaft box (D-067).
6. **Validation** (D-066) and the `WorldCompiler` emitting connector surfaces (portals stay M2).
7. **Demo** vertical set (D-061).

## Not decided yet (ask in the next chat's Query phase if they matter)

- Library preset values to use for stairs (riser/going, max risers per flight) if `BuiltInLibrary` doesn't define
  them all yet. Check `ConnectorTemplate` first.
- Whether the rotate grip snaps to 15° steps (suggested default) or only to ortho and snap points.

## Gavin's notes for later

- Wants to see a **3D lift car with people moving in it**. This is already planned for M4 (BRIEF §7: shaft volume,
  cars as moving boxes on an accelerate/cruise/decelerate profile, agents held at slots in car-local space so they
  visibly ride). Round 004's shaft element (D-056) is the volume the car will run in, so keep its data model
  lift-ready (an optional `LiftBankId` link can come in M4).
- Wants a **1- or 2-door entry/exit typology per served level** for lifts. Logged as D-068 for M4 (front/rear doors,
  each set to entry/exit/both/closed per level, each with its own landing zone). Shafts in round 004 should allow
  a door side on either face, e.g. keep the outline's edge order stable so "front" and "rear" can map to edges later.
