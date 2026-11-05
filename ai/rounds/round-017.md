# Round 017: definitions (devices, agent profiles) + select / move / place in 3D

**Status:** built 2026-10-31, **unbuilt in sandbox** (no .NET SDK again; statically reviewed: bracket balance,
XAML well-formedness, CRLF). **318 test methods** (309 + 9). **Format stays v10.** One build, no splits (Gavin).

Gavin's 016c build: clean, all tests pass.

## Decided (D-285–D-297)

| # | What |
|---|---|
| D-285 | Scope: definition editing for devices and profiles + 3D / ISO select, move, place. Journey node editor next (018). |
| D-286 | Both: built-ins edited in place for the study, plus DUPLICATE / NEW; all in SELECTION, undoable. |
| D-287 | Override = whole copy under the built-in's id (the embedded copy), MODIFIED when it differs, RESET TO BUILT-IN. |
| D-288 | Placed devices follow their definition; size, lanes, angle per device; MAKE UNIQUE forks one. |
| D-289 | Distributions: type + numbers + a small plot. |
| D-290 | .throngdevice export / import with the reconcile dialog for an unknown type tag. |
| D-291 | 3D / ISO select everything plan selects; move, rotate grip, Delete and SELECT keys. Supersedes D-026's view-only part. |
| D-292 | No drawing in 3D (a drawing tool or its key goes to plan). |
| D-293 | Floor under the cursor; a click makes its level active; a move ending over another floor goes to that level. |
| D-294 | Plan snapping in 3D, drawn on the floor plane. |
| D-295 | CHECK stays on demand, marked OUT OF DATE after edits. |
| D-296 | Capacity Info lines: the 5 busiest per level. |
| D-297 | The judgement calls below. |

## As built

**Definitions (D-286–D-288)** — `Core/Library/StudyDefinitions.cs`
- `AvailableDevices`, `FindDevice`, `IsBuiltIn`, `IsModified` (the study's copy vs the built-in, ids and source aside),
  `SameValues`, `CopyValues` (deep, every writable property but `Id` and `Source`), `EnsureStudyCopy` (embeds the
  built-in's copy, as placing does), `CreateDeviceCopy` / `CreateProfileCopy` (new id, `device.user.*` /
  `agent.user.*` key, "(copy)" names), `CreateNewDevice` (free `Custom` tag) / `CreateNewProfile`, `DevicesUsing`,
  `ProfileUseCount`, `KnownDeviceTypes`.
- App: `MainViewModel.Definitions.cs`. A click on a LIBRARY ▸ DEVICES or ▸ AGENTS row opens SELECTION ▸ DEVICE
  DEFINITION / AGENT PROFILE (one thing in SELECTION at a time, like journeys). Every edit is one `CompositeAction`:
  a `ReplaceStateAction` on the definition (copy in / copy back) plus, for devices, the placed devices' type tag,
  pass-through and colour. DUPLICATE, RESET TO BUILT-IN (when modified), DELETE (the study's own, when unused),
  EXPORT…, PLACE. LIBRARY rows show MODIFIED / YOURS / IMPORTED; NEW / EXPORT… / IMPORT… under DEVICES, NEW under AGENTS.
- `DeviceDefinitionViewModel`, `ProfileDefinitionViewModel` (+ `DevicePreferenceRowViewModel`): looked up by id on
  every read, because the first edit of a built-in swaps in the study's copy.
- SELECTION ▸ device: "Definition" with what it follows and how many share it, EDIT DEFINITION and MAKE UNIQUE.
- PLACE presets and the journey editor's device type list now come from the study's definitions; placing uses the
  study's copy (`PlanEditor.CommitPlace`).

**Distributions (D-289)** — `Core/Library/DistributionShape.cs` (`Range`, `Density`, `Normalised`, `Converted`),
`App/ViewModels/DistributionEditorViewModel.cs`, `App/Views/DistributionPlot.cs`, the `DistributionEditorTemplate`
in `MainWindow.xaml`. Used for service time and walking speed. `Normalised` keeps bounds in order, the mode inside, no
negative times, and the mean in step for Fixed / Uniform / Triangular (the capacity check reads it).

**Device exchange (D-290)** — `Core/Library/{DeviceBundle, DeviceReconciler}.cs`, `ReconcileKind.ImportedDeviceType`,
`IO/DeviceFile.cs` (`.throngdevice`), `IDialogService.{PickDeviceOpenPath, PickDeviceSavePath, ReconcileDevice}`,
`ReconcileDialog.Show(…, isDevice)`.

**3D editing (D-291–D-294)** — `Views/ViewportView.Edit3D.cs`, `Editing/PlanEditor.ThreeD.cs`
- The pointer becomes a camera ray (inverse view-projection); the nearest visible level floor it meets (explode,
  clip and section box honoured; a region or device at the point) is the floor under the cursor. A press there
  activates that level (`IEditorHost.ActivateLevel`) and calls the plan editor's `PointerDown` with the point on its
  floor plane and metres-per-DIP measured there, so picking, moving, the rotate grip, rubber band and snaps are
  the plan editor's. The overlay maps plan points onto the active floor plane in 3D (`EditorOverlay.Is3D`) and
  redraws when the camera moves.
- A move released over another level's floor sets `PlanEditor.MoveToLevelId`: devices, underlays and one-level
  regions change level in the same undo step, and that level becomes active with them still selected.
- Keys: SELECT keys work in 3D; a drawing tool's key (or button) switches to plan. Vertex / edge grips are off.
- LIBRARY drops in 3D: a device is placed at once on the floor under the drop; stairs and travelators carry on in plan.

**CHECK (D-295, D-296)** — `MainViewModel.Check` (`IsCheckStale`, banner in the CHECK tab); `Analysis/Checks/
CapacityFindings.cs` ranks capacity Info lines by people a minute at their peak and keeps 5 per level.

**Tests** (+9): Core `Round017Tests` (override copy, modified / reset, duplicates and new, profile use count,
distribution consistency, device reconcile); Sim `Round017Tests` (an edited definition reaches the compiled devices;
capacity notes ≤ 5 per level on the demo); IO `Round017FileTests` (.throngdevice round trip and refusals; an override
survives save and reopen).

## How to test

1. Build; all tests pass (318 methods). Demo and Office tower load with 0 errors.
2. LIBRARY ▸ DEVICES, click Ticket machine: SELECTION ▸ DEVICE DEFINITION. Change the service time type to Fixed,
   value 10: the plot is a single bar; the row shows MODIFIED; RESET TO BUILT-IN puts it back; Ctrl+Z / Ctrl+Y work.
3. Change its type tag to "Kiosk": the placed ticket machines follow (SELECTION ▸ device shows Kiosk); CHECK warns
   the journeys no longer find a TicketMachine (or similar). Undo.
4. Select a placed ticket machine ▸ MAKE UNIQUE: it gets "<name> type"; edit that definition's service time; the
   other machines are unchanged. EDIT DEFINITION on another machine opens the shared one.
5. DUPLICATE a gate, rename it, set Choice weight 30; place it (PLACE or drag): people prefer it.
6. DEVICES ▸ NEW: "New device" with type Custom; PLACE it; add a journey step to "Custom" (the type list offers it).
7. AGENTS, click Commuter: edit walking speed (Normal mean 1.0), patience, add a device preference; PLAY: slower
   commuters. RESET TO BUILT-IN. AGENTS ▸ NEW makes "New profile"; DELETE it (not used by a mix line).
8. DEVICES ▸ EXPORT… a duplicated device; New study ▸ IMPORT…: an unknown type asks Keep / Use '…'; it appears YOURS
   / IMPORTED in DEVICES and in the PLACE list.
9. 3D (F3): hover a region on the active level (highlight + label); click a device on another level: that level
   becomes active and the device is selected (SELECTION follows); drag it: it moves with snaps; drag it over another
   floor and release: it moves to that level (one undo step). Rotate grip and Delete work; R switches to plan.
10. ISO (F4) with explode and a section box: clicks hit the exploded floors; levels cut away aren't picked.
11. Drag a device from LIBRARY into 3D onto a floor: placed there, still in 3D. A stair dropped in 3D continues in plan.
12. CHECK, then move something: the CHECK tab says OUT OF DATE; IGNORE doesn't. CHECK again clears it.
13. CHECK on the demo: no level has more than 5 capacity notes.
14. Don't-regress list (HANDOVER §8). v9 / v10 files open unchanged; plan editing as before.

## Judgement calls (D-297)

- The device editor shows the fields the run reads: type tag, service time, choice weight, accessible, pass-through,
  colour, plus lanes / width / depth for new placements. Queue mode, dwell, capacity and spawn / sink flags of
  device templates aren't used by the simulation, so they aren't shown. Zone presets (areas) show name, description
  and colour only.
- The first edit of a built-in embeds its copy outside the undo stack (as placing already does); undoing leaves a
  pristine copy, never shown as MODIFIED.
- DUPLICATE keeps the type tag (journeys use the copy too); NEW gets a free `Custom` / `Custom2` tag; MAKE UNIQUE
  names the copy "<device> type".
- Profile ranges: radius 0.1–0.8 m, height 0.5–2.5 m, stair factors 0.1–1.5, efforts 1–10, patience 5–7200 s,
  lift / escalator preference −1..1, sight 1–200 m, acceptable journey 30–14400 s, preference bonuses ±600 s.
- Picking a new distribution type keeps its middle and spread (Fixed → Normal gets ±50 % bounds, sd a quarter).
- `.throngdevice`, JSON format 1; on import a taken id or key gets a new id and "(imported)".
- 3D floor under the cursor: the nearest visible level whose region or device contains the hit point; the clip and
  section box cut levels out. Hover lights the active level only; another level lights on click.
- A drop across levels with stairs, shafts or spanning regions in the selection moves in plan only, with a message.
- CHECK goes out of date on any undoable change except ignoring a finding (saving a named view counts).
- Capacity notes are ranked by people a minute at their peak (stairs: peak flow; lifts / devices: peak demand;
  trains: people per minute of headway).

## Accepted defaults

- None beyond the judgement calls.

## Known limitations

- While dragging over another level's floor, the moving outline is drawn on the active level's plane (at the right
  plan position); it lands on the other level on release.
- Hover and the hover label show on the active level only in 3D.
- Device definitions edit the fields the run uses; a full template library across studies (.thronglib) is still to
  come (M3 / M8).
- The capacity notes that were trimmed aren't summarised anywhere.

## Watch list

- Picking in perspective near the horizon (long rays) and over large voids (no region there, so the level below is hit).
- LIBRARY refresh cost: every model change re-checks MODIFIED for every built-in copy (JSON compare); fine at demo
  size, watch on big libraries.
- Carried over: crowd stress at 2.5 p/m², hold points by adjacent lanes, gate throughput, shader cost when exploded
  at stadium scale, narrow gap false positives round shafts.

## Open questions (for round 018)

- Journey node editor (D-285): replace the step list with a graph (branches with chances or tag conditions, merges),
  or keep steps and add branching on top? Format v11 either way.
- Should profiles also get MAKE UNIQUE-style "use a copy on this mix line"?
- A cross-study user library (.thronglib) for definitions now, or with M8 presets?

## 017b (after Gavin's build: compiles, all tests pass)

**Decided:** D-298 (3D select only), D-299 (right click ▸ VIEW IN PLAN), D-300 (stair end lanes).

- **3D select only (D-298):** `PlanEditor.Is3D` keeps a press pending (no move, rubber band or rotate grip; the grip
  isn't drawn); the 017 cross-level move (`MoveToLevelId`, `CommitMoveToLevel`) is removed. In 3D the viewport passes
  only Esc, H and I to the editor. Device drops from LIBRARY into 3D still place.
- **VIEW IN PLAN (D-299):** a right click without a drag picks the object on the floor under the cursor
  (`PlanEditor.ObjectAt`), selects it and opens a themed context menu; VIEW IN PLAN → `MainViewModel.ViewInPlan`
  (plan view, its level, selected, framed; reuses CHECK's `Locate`). `ContextMenu` style added to `Theme.xaml`.
- **Stair end lanes (D-300):** `SimEngine.StairEndLane`, `SimTuning.StairEndLanes`, diagnostics row "no stair end
  lanes (D-300)". Heading for a two-way stair or ramp end within 8 m and in front of it: aim at the point
  min(2 m, sideways offset, depth) out from the middle of your keep-side half (no keep side: the middle). Stepping off
  a two-way end: straight out until 2 m past the edge (4 s cap, not when stalled). The D-125 lane push still applies.

**How to test 017b**
1. 3D: click a ticket machine: selected, SELECTION shows it. Drag: nothing moves, release keeps it selected.
2. Right-click it (no drag): VIEW IN PLAN → plan on its level, selected and framed. Right-drag still orbits.
3. Del, Ctrl+D, arrows do nothing in 3D; Esc clears; H / I hide / isolate.
4. Demo PLAY, watch the concourse stair foot in 3D: people coming off walk out a couple of metres before turning;
   people going on approach their own half from in front. Run the diagnostics and paste the stair rows, with the new
   "no stair end lanes" row, if the counts move.

**Watch:** narrow concourses where 2 m straight out from a stair hits a wall (the wall force and the stall rule
cover it); stairs whose foot faces a gate line.

## Next

Round 018: the journey node editor, scope to be queried.
