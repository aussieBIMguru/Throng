# Round 016: M7 validation & profiles + named views + box handles

**Status:** built 2026-10-30, **unbuilt in sandbox** (no .NET SDK again; statically reviewed: bracket balance, XAML
well-formedness, CRLF). **309 tests expected** (297 + 9 + 2 from the fixes below). **Format v10** (saved views, ignored
findings). One build, no splits (Gavin).

## 016 fixes (D-280), after Gavin's build

Gavin's build: one compile error, one failing test, and a wrong capacity warning (the demo's 8 ticket machines
"queue ~2849 people" although the run shows no queue).
1. **`Vector` ambiguous in `ViewportView`** (`System.Windows` vs `System.Numerics`): spelled out `System.Windows.Vector`.
2. **`UnusedDevice_IsFlagged` failed:** the compiled model only holds devices some journey asks for by type, so an
   unwanted device was never seen. Unused devices are now judged from `study.Devices`.
3. **Capacity maths:** it took every person through every device step (ignoring "unless already holding a ticket") and
   held the peak rate for the whole run. Now each step gets only the share its needs / lacks tags apply to (spawn tag
   chances, plus earlier steps' grants), demand is a per-minute series through each rule's rate curve (train doors
   through the service curve, from the first train), and each device group, lift and connector is run as a simple
   queue minute by minute. The warning reports the demand peak, the queue's peak and when, and whether it clears by
   the end; a busy-but-fine peak is an Info line. Fruin LoS uses the peak minute.
   New tests: `Queue_FollowsDemandMinuteByMinute`, `StepShare_FollowsTags`; the lift capacity test checks the new
   wording.
4. **Lift courtesy (D-281–D-284), Gavin: "wheelchair users cram the doors and it never moves".** Cause: waiters stood
   anywhere on the 2.5 m landing; a rider needs a clear spot just outside the door, riders go first, so the car held
   its doors for ever. Now (`SimEngine.Lifts`: `ClearExitLanes`, `TryAlight(effort)`, `_carBlockedSince`,
   `LandingFull`): waiters in an open door's exit lane step to its side while riders get off; a blocked rider squeezes
   after 3 s and steps off regardless after 8 s; a full landing sends newcomers to wait just behind it. Test
   `Lifts_AWheelchairCrowdNeverJamsTheDoors` (12 wheelchair users a minute, no car open over 90 s). **309 tests.**

Gavin's 015 build: clean, no changes needed.

## Decided (D-270–D-279)

| # | What |
|---|---|
| D-270 | Scope: M7 + saved named views (v10) + section box outline and 3D face handles, one build. |
| D-271 | CHECK tab with Error / Warning / Info rows and filter chips; click → level, select, zoom; PLAY's blocking errors open it. |
| D-272 | Named rules on every finding; per-study ignore (v10), ignored shown greyed under a toggle. |
| D-273 | Static capacity check: devices, lifts, train bursts; warnings give the queue to expect, Info the Fruin LoS. |
| D-274 | New rules: unreachable destination, unused device, escalator against all demand, narrow gaps. |
| D-275 | .throngprofile = profile + its journeys; reconcile missing device types / tags: map, keep or drop. |
| D-276 | Saved named views in STUDY ▸ VIEW ▸ VIEWS (mode, level, camera, ISO preset, cutaway, explode); undoable; PNG title. |
| D-277 | Section box outline + six face handles; left-drag a face in 3D / ISO (sliders follow). |
| D-278 | Agent LOD stays per frame until M8. |
| D-279 | The judgement calls below. |

## As built

**Rule engine and CHECK (D-271, D-272, D-274)**
- `Core/Validation/{ValidationRule, ValidationRules}.cs`: the rule catalogue (ids are stored in studies; never rename).
  `ValidationMessage` gains `Rule`, `LevelId`, `At` (a place, for findings with no object) and `IgnoreKey`;
  `StudyValidator.Tag` stamps each validator section's findings. `SimSetup`'s reachability findings are `reachability`;
  anything else from the compile is `setup`.
- `Analysis/Checks/StudyCheck.Run`: static rules → `SimSetup.Build` (reachability for every profile mobility, as PLAY
  already did) → when it compiles, `CapacityCheck` → `NarrowGapCheck`; sorted errors first.
- `Study.IgnoredFindings` (v10). IGNORE / UNIGNORE on a row is an undoable study edit (blocked while running).
- App: CHECK is the fifth right-panel tab (`MainViewModel.Check.cs`, `CheckRowViewModel`), outside the edit lock. CHECK /
  F7 / RUN CHECK run it on a worker thread; chips ALL / ERRORS / WARNINGS / INFO and "Show ignored". Clicking a row goes
  to plan view on the object's level (region, device, connector, shaft, underlay, level, train door, or a place), selects
  it when not running and frames it (`ViewportView.ZoomTo`), staying on CHECK. PLAY's blocking errors now land in the
  CHECK tab instead of a dialog.

**Static capacity check (D-273) and demand rules (D-274)** — `Analysis/Checks/CapacityCheck.cs`
- Each emitter's mix lines are walked once with the route planner: spawn → each journey step's cheapest target → sinks
  (weights; nearest sink when the rule says so; board-train steps end the walk), at the rule's peak rate. Train doors
  walk at the mean rate of people getting off and add each train's people as a burst.
- Device groups (all targets of a step): demand vs lanes × 60 ÷ mean service → Warning with the queue and wait to expect
  by the end of the study; ≥ 80 % → Info. Lifts: places per round trip; stairs / escalators / travelators: throughput.
  Stairs, ramps, travelators with demand get an Info line with the expected Fruin LoS.
- Train bursts: each train's people ÷ capacity vs the headway → Warning ("the queue keeps growing") or, over a minute,
  Info ("expect a ~N min queue after each train").
- Escalator against all demand: an escalator no walk uses while walks between its two levels go the other way.
- Unused device: no walked journey step lists it.
- Narrow gaps (`NarrowGapCheck`): each level's walkable area opened (shrunk then grown, mitred) by the widest profile's
  radius; what doesn't come back is a gap they can't pass, reported at its centre with its width.

**Profile exchange (D-275)**
- Core: `ProfileBundle`, `ProfileReconciler` (`BuildBundle`, `Find`, `Apply`), `Reconcile{Kind, Action, Item, Choice}`.
  Missing = device types / preset keys no study device has, zone tags no zone has, train tags no service has, needs /
  lacks tags nothing grants or spawns. Imports unpin zones, drop give-up targets that no longer exist, mark Imported,
  and get new ids / keys / "(imported)" names only when the id is taken.
- IO: `ProfileFile` (`.throngprofile`, JSON). App: EXPORT… / IMPORT… under LIBRARY ▸ AGENTS (click a profile first),
  `ReconcileDialog`, one undoable `CompositeAction` for the profile and journeys; AGENTS now lists imported profiles.

**Saved named views (D-276)**
- `Core/Model/SavedView`, `Study.Views` (v10), `FormatMigrations.UpgradeV9ToV10`, `ThrongFile.CurrentFormatVersion = 10`.
- `MainViewModel.NamedViews.cs`, `SavedViewRowViewModel`: SAVE VIEW, GO, rename in place, DELETE (all undoable).
  `ViewportView.{WriteCamera, ReadCamera}`. Changing the view by hand ends the "active" view; the PNG title block
  starts with `VIEW 'name'` while one is active.

**Section box outline and handles (D-277)**
- `Render/Scene/SectionBoxMesh`, `SceneRenderer.{DrawSectionBox, HoveredBoxFace, TryGetBoxHandles, WarpZ, UnwarpZ}`,
  `ExplodeMap.Unwarp`, `ViewSettings.Extents`. Viewport: hover lights a handle; left-drag moves its face along the
  axis on screen (pixels per metre measured at the handle, through the explode for heights) and raises
  `BoxFaceDragged` → `MainViewModel.DragBoxFace` → the slider.

**Tests** (+9): Core `Round016Tests` (rule ids and ignore keys, unwarp, reconcile find / apply / rehome, bundle
journeys); Sim `Round016Tests` (demo checks clean with rules on everything, busy lift warns, unused device, narrow gap
geometry and placement). Updated: `ThrongFileTests.Save_WritesFormatV10`, `Round013FileTests` (current format).

## How to test

1. Build; all tests pass (306). Demo and Office tower load with 0 errors.
2. CHECK (F7) on the demo: the CHECK tab opens with warnings and notes, no errors; each row shows its rule. ERRORS /
   WARNINGS / INFO chips filter.
3. Click a row about a device or zone: plan view on its level, selected and framed; you stay on CHECK.
4. IGNORE a warning: it greys out and leaves the counts (Show ignored brings it back); Ctrl+Z un-ignores; save, reopen,
   CHECK: still ignored.
5. Office tower, raise the lobby rate a lot, CHECK: a capacity warning on the lift core with the queue to expect.
6. Draw a 0.4 m wide link between two rooms, CHECK: a narrow gap warning; clicking it frames the gap.
7. Flip an escalator against the demand: a direction warning. Place a device no journey uses: an unused device warning.
8. Break something (delete a sink a rule needs), PLAY: the CHECK tab opens with the error instead of a dialog.
9. LIBRARY ▸ AGENTS, click a profile, EXPORT… → a .throngprofile. New study, IMPORT… it: if the journeys need devices the
   study lacks, the dialog lists them with Keep / Drop / Use '…'; the profile appears in AGENTS and its journeys in
   JOURNEYS; Ctrl+Z removes both.
10. STUDY ▸ VIEW: set up an ISO view with a box and explode, SAVE VIEW, rename it, move around, GO: everything comes
    back. Save, reopen: the view is there. A plan view saves its level and zoom.
11. Section box on in 3D: a cyan outline with square handles; hover one (yellow), left-drag: that side moves and its
    slider follows; works while running and with explode on.
12. EXPORT PNG while a saved view is active: the title starts with VIEW 'name'.
13. Don't-regress list (HANDOVER §8). Older (v9) files open with no views and nothing ignored.

## Judgement calls (D-279)

- CHECK runs the full compile (so reachability shows on CHECK, not only on PLAY), then capacity / escalator / unused
  device when it compiles; narrow gaps always.
- Capacity: one walk per mix line along the best route for its profile (no spreading between equal routes, no queues
  feeding back); peak = rate × the largest curve multiplier; queue by the end = (demand − capacity) × duration (60 min
  when indefinite). Stair LoS at 30 m/min, ramps / travelators 72 m/min.
- Narrow gaps: the widest profile in the compiled mix (0.25 m without one); pieces ≥ 0.02 m²; at most 8 per level.
- Ignore key: rule + object id, or rule + message for findings without an object. Ignoring is blocked while running.
- CHECK rows frame at least 8 m round the object; places (gaps) frame 12 m.
- Profile import: zone pins dropped, ids kept unless taken; the reconcile dialog defaults to the only alternative when
  there's exactly one, else Keep.
- Saved views can be saved, renamed and deleted while running (they don't touch the run).
- Box handles: squares a tenth of the box's smallest side (0.5–4 m), picked within 14 px, drawn over everything.

## Accepted defaults

- None beyond the judgement calls.

## Known limitations

- The capacity check is a single nominal route per line; real runs spread between equal options and react to queues.
- CHECK findings don't refresh by themselves after an edit: press CHECK again (the summary says so).
- The right-panel tab strip now has five tabs; check it fits at the default panel width.
- Handles move one face at a time; there's no whole-box move (decided).

## Watch list

- Capacity Info lines on the demo may be many (one per used stair); trim to the busiest if noisy.
- Narrow gap false positives round connector openings and shafts; check the demo and the Office tower.
- Carried over from 015: shader loop cost when exploded at stadium scale, translucent shaft wall order with heatmaps,
  and the older watch items (crowd stress, hold points, gate throughput, lift landing edge cases).

## Open questions (for round 017)

- Next: M8 scale & replay (performance pass toward stadium scale, replay scrubbing, headless batch runs), or the
  watch-list tuning first?
- Should CHECK re-run automatically after edits (debounced), or stay on demand?
- Trim capacity Info to the busiest few, or keep one per connector?

## Next

Round 017, scope to be queried.
