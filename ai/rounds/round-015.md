# Round 015: M6 views polish + lift tails

**Status:** built 2026-10-30, **unbuilt in sandbox** (no .NET SDK again; statically reviewed: bracket balance, XAML
well-formedness, CRLF, a second read of every shader and renderer change). **297 tests expected** (288 + 9). **Format v9**
(no change). One build, no splits.

Gavin's 014 build: clean, 288 pass. Nothing to fix first.

## Decided (D-259–D-269)

| # | What |
|---|---|
| D-259 | Scope: M6 views polish plus the LIFTS results table and 3D shaft walls, one build. |
| D-260 | RESULTS ▸ LIFTS per bank (boarded, mean / 95th / max landing wait, rides, mean ride, gave up) and lifts.csv. |
| D-261 | Semi-transparent 3D lift shaft walls with the landing door openings left out. |
| D-262 | M6 items this round: iso presets, cutaway (clip above a level + section box), exploded levels, agent LOD. |
| D-263 | ISO: orthographic 2:1 game iso, four quarter-turn presets (SW / SE / NE / NW). |
| D-264 | Exploding stretches spanning objects (height remap in the shaders). |
| D-265 | View settings are session only; no format change. |
| D-266 | Clip, section box and explode on sliders in a VIEW section; no 3D handles. |
| D-267 | Agent LOD: flat low-poly discs when people are small on screen. |
| D-268 | ISO button beside PLAN / 3D (F4; [ and ] turn it); VIEW section on the STUDY tab, off in plan. |
| D-269 | The judgement calls below. |

## As built

**ISO view (D-263, D-268)**
- `ViewMode.Iso`; `OrbitCamera.Orthographic` (init) with `SetIsoPreset(0–3)`, `IsoPitch` = atan(½), `IsoPresetNames`,
  `ViewHeightM`. The viewport keeps a separate `_isoCamera` (fitted with the orbit camera on Home); any drag pans,
  the wheel zooms, `[` / `]` raise `IsoRotateRequested` → `MainViewModel.RotateIso`. HUD and PNG title read "ISO NE" etc.
- Toolbar ISO button, View ▸ Isometric, F4. `MainViewModel.{IsoViewCommand, IsIsoView, IsoPreset}`.

**Cutaway and explode (D-264–D-266)**
- `Core/Geometry/ExplodeMap` (CPU twin of the shader warp): `Elevations` (distinct, ≤ 64), `LevelShift` (gap × levels
  below), `Warp` (linear within each storey, equal to the level shift at every floor).
- Shaders: every vertex shader has `warpZ` (uWarp 0 none / 1 stretch / 2 rigid shift) and passes its true world
  position; every fragment shader discards outside `uBoxMin`–`uBoxMax` while `uClip` is on (the thick-line geometry
  shader carries the position through). `ShaderProgram.{SetExplode, SetWarp, SetClip}`.
- `SceneRenderer.View` (`Render/Scene/ViewSettings`, a record of shares + gap + clip height, with `ClipBox`):
  `PrepareView` sets the uniforms each frame (plan always gets `ViewSettings.None`); per-level meshes draw with their
  rigid shift (`LevelWarp`), connectors, shafts, heat layers, cars, doors and people with the stretch. People's base
  height is warped and the body added on top, so they keep their shape on stretched stairs.
- STUDY ▸ VIEW (`MainViewModel.View.cs`, top of STUDY, outside the edit lock so it works while running): clip above a
  level (switch + level list), section box (switch + W/E, S/N, LOW/HIGH sliders, percent of the study extents), explode
  slider (0–30 m, 0.5 m steps), CLEAR. Disabled in plan. `MainWindow` pushes `ViewSettings` to the viewport on change and
  on every scene rebuild (so the clip height follows level edits). Changing the explode re-centres the camera height.
- New themed `Slider` style in `Theme.xaml`.

**Agent LOD (D-267)**
- `GpuAgents` holds a second VAO with an 8-sided flat disc (+ pointer, + stuck ring) sharing the instance buffer;
  `Draw(discs)`. The viewport sets `SceneRenderer.AgentPixelsPerMetre` each frame (plan: DPI ÷ metres per DIP; 3D/ISO:
  framebuffer height ÷ view height at the target); below `DiscBelowPixelsPerMetre` (10) discs are drawn.

**LIFTS results (D-260)**
- `Sim/Engine/{LiftResult, SimEngine.LiftResults}.cs`; `AgentStore.LiftRideFrom`; `Board` records the landing wait,
  `TryAlight` the ride, `TryLeaveLift` the give-up, all after the warm-up. `RunResults.Lifts`.
- `ResultsCsv.LiftsCsv` + `lifts.csv` in EXPORT CSV. RESULTS ▸ TABLE ▸ LIFTS chip (`ResultsTable.Lifts`,
  `LiftResultRow`, `MainViewModel.LiftResultRows`, summary line).

**Shaft walls (D-261)**
- `Core/Geometry/{ShaftWalls, WallOpening, WallPanel}.cs` (walls split into panels round openings on their edges),
  `Core/World/LiftOpenings.For(study, shaft)` (one opening per car door at every non-closed landing; Shuttle end stops
  only, as in the nav world). `SceneBuilder` draws lift shafts' walls as panels at `RenderPalette.LiftShaftWallAlpha`
  (0.24); shafts without a lift keep the faint D-067 box.
- The car height rule moved to `LiftLayout.CarHeightFor` and `LiftLayout.DoorHeightShare` (the app and the renderer
  both use it, so the openings line up with the 014 door leaves).

**Tests** (+9): Core `Round015Tests` (explode shifts and warp, distinct elevations, shaft walls with and without a door,
lift openings per car door and landing, car height rule); Sim `Round015Tests` (lift results counts and ordering, warm-up
left out, lifts.csv).

## How to test

1. Build; all tests pass (297). Demo and Office tower load with 0 errors, 0 warnings.
2. Demo: ISO (toolbar or F4): an orthographic 2:1 view from the south-west, HUD "ISO SW". Click the viewport, press `]`
   three times: SE, NE, NW; `[` goes back. Wheel zooms, right- and middle-drag pan, Home frames everything.
3. STUDY ▸ VIEW in plan: greyed out with the "3D and ISO only" note. F3: enabled.
4. Clip above a level: switch on (picks the active level), choose Concourse: the platform level shows, everything above is
   gone (stairs cut just under the concourse floor). Choose the top level: nothing is cut.
5. Section box: switch on; drag W ▸ E sliders in: the west of the station disappears; LOW ▸ HIGH top slider down cuts the
   upper levels. Sliders can't cross. CLEAR resets everything.
6. Explode to ~10 m: levels separate; stairs, escalators and lift shafts stretch to meet each floor. PLAY: people stay on
   the floors and walk up the stretched stairs; lift cars and riders move between the separated floors; landing doors
   still line up with the shaft openings. All of this keeps working while running (VIEW isn't locked).
7. Agent LOD: zoom far out in 3D: people become flat discs with a pointer; zoom in: capsules again. Stuck rings still blink.
8. Office tower, 3D: the lift core has semi-transparent walls with door openings at each floor; cars and riders read
   through. A shaft without a lift is still the faint box.
9. Office tower PLAY, then RESULTS ▸ LIFTS: one row for the bank, 4 cars, boarded / waits / rides / gave up climbing.
   EXPORT CSV writes lifts.csv as well; the ticker lists it.
10. EXPORT PNG in ISO: title block reads "ALL VISIBLE LEVELS · ISO …"; cut and exploded views export as shown.
11. Don't-regress list (HANDOVER §8). Plan view looks and edits exactly as before (no clip, no warp in plan).

## Judgement calls (D-269)

- ISO pans with any drag (right or middle) and ignores orbit drags; its zoom matches the 3D view height, so switching keeps
  a similar scale.
- LOD is chosen for the whole frame (one instanced draw), at 10 px per metre near the view's middle; discs are 8-sided,
  1.25 × body radius, about 7 cm above the floor, with a lighter pointer for heading.
- Clip cuts 5 cm under the next floor up (or 5 cm over the top level's storey height); clip and box use true heights, so
  they behave the same with or without explode.
- Section box sliders are percent of the study's extents plus a 1 m margin, kept at least 2 % apart; the box isn't drawn.
- Explode snaps to 0.5 m, 0–30 m; changing it re-centres the camera's target height on the taller stack.
- Exploded storeys stretch linearly, so spanning walls of a lift shaft, car boxes and door openings get taller with the
  gap; people and level content don't.
- VIEW sits at the top of the STUDY tab, outside the edit lock. A new study clears the clip level (it belonged to the old
  study); other VIEW settings carry over within the session.
- LIFTS: landing wait = reaching the landing → boarding; ride = boarding → stepping off; both after the warm-up, like
  devices. Give-ups after the warm-up only (the live counter on the bank still counts all).
- Shaft walls: alpha 0.24; an opening for every car door at every landing that isn't Closed (Shuttle: end stops only),
  even where the landing has no walkable floor.

## Accepted defaults

- None beyond the judgement calls (all cheap to change).

## Known limitations

- The section box and clip aren't drawn as outlines or caps; cut floors simply end.
- Agent LOD is per frame, not per person: in a deep perspective view near people are discs too until you zoom in.
- Exploding stretches car boxes and shaft openings vertically (the riders don't stretch).
- 3D picking / editing is still not available (D-026); ISO is view-only like 3D.
- View settings aren't saved (D-265); there are no named views yet.

## Watch list

- Shaders now loop over up to 64 level heights per vertex while exploded; fine for current studies, check at stadium
  scale (M8).
- Translucent shaft walls draw without depth writes after the floors; check the order reads right with the heatmap on.
- Carried over: crowd stress at 2.5 p/m² (D-194), hold points in front of adjacent lane mouths, gate throughput ("no lane
  clear"), the 013b no-exit landing and same-step overlap, destination with one car, doorway blocking in dense lobbies.

## Open questions (for round 016)

- Next milestone: M7 validation & profiles (rule engine, static capacity check, profile import/export + reconcile), M8
  scale & replay, or the watch-list tuning first?
- Saved named views (camera + cutaway + explode, in the study, format v10)?
- Section box drawn as a wireframe and/or 3D drag handles (first 3D editing)?
- Per-person LOD (two draws) instead of per frame?

## Next

Round 016, scope to be queried.
