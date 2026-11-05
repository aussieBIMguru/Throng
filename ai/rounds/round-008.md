# Round 008: editor gaps and crowd visuals

**Unbuilt:** no .NET SDK in the sandbox; statically reviewed only (braces, line endings, XAML well-formedness).
Builds on r007p, which Gavin built: 181/181 expected, diagnostics fine (saturated pair boards at 96% of the cap,
demo all-on stuck 0, counterflow gate worth 421 vs 379 arrivals on the wide stair).

## Decisions

D-140 scope · D-141 journey templates (later) · D-142, D-143 levels · D-144 hover · D-145 stress · D-146 colour chips.

## What changed

**Levels (D-142, D-143)**
- `Core/Editing/LevelActions.cs`: `Plan` (above/below; splits the storey at mid-height when there's a neighbour,
  else one storey of the active level; refuses splits under 1 m each side), `Add`, `Delete` (one undo step:
  regions, devices, underlays on the level, connectors starting there, shafts based there, then the level),
  `ContentsOf` for the confirm text. Fails on the study's only level.
- CHECK: new warning "Connector 'X' passes level 'Y' without stopping" for straight runs through a level they
  don't serve (per-level circuit stairs pick the new level up by themselves). Existing "links a level that doesn't
  exist" errors cover stairs arriving at a deleted level.
- App: `+ ABOVE`, `+ BELOW`, `DELETE` in the LEVELS header (`MainViewModel.Levels.cs`); delete asks first
  (`IDialogService.Confirm`), refuses locked levels; the new level becomes active.

**Hover (D-144)**
- Select: the object a click would pick gets a 2 px cyan outline, a light fill and a tag at the cursor
  ("CONNECTOR KIND · NAME"). Was a 1 px half-transparent yellow line.
- Drawing, CONNECT and PLACE: when the cursor snaps to a vertex or midpoint, the object it belongs to is outlined
  in dashed magenta; grid snaps get a dot.
- LIBRARY drag: a ghost follows the cursor over the plan (device footprint where it would land; connectors show
  the point and "then aim"); cleared on leave, drop or cancel.

**Stress (D-145, D-146)**
- Sim: `AgentStore.Stress` and `HeldS`; `SimEngine.UpdateStress` after each move. Held-up time builds while
  below 50% of desired speed (belt riders excepted) at (1.5 − density tolerance) × dt and drains at 0.5 × dt.
  Stress = 0 until half the profile's patience is spent held up, then linear to 1 at full patience
  (`StressFrom`, `HeldStep`, both public static).
- Snapshot grows to 11 floats (stress last); `GpuAgents` attribute 6; shader stress ramp (blue → yellow → red)
  under `uColorMode`.
- App: COLOUR ▸ PROFILE / STRESS chips after the speed chips; view setting only (not saved, not undoable).
  The stuck ring (red, blinking) was already there from r006 and stays always on.

**Tests:** +12 (8 `LevelActionsTests`, 4 `StressTests`); `Snapshot_HasTenFloatsPerAgent` → `…Eleven…`.
Expect **193**.

## How to test

1. Build; all 193 tests pass.
2. Demo ▸ LEVELS: select Concourse, `+ ABOVE` → "Level 4" at −2.5 m (mid-storey), active. CHECK: Street stair and
   Street escalator warn they pass Level 4. Ctrl+Z removes it; Ctrl+Y brings it back.
3. Select Street, `+ ABOVE` → a level at +5 m. Select Platform, `+ BELOW` → −17 m.
4. Delete Platform: the confirm lists what goes (its floors and walls, plus the Platform stair, Platform
   escalators and back-of-house stair, which all start there). CHECK then has nothing linking to Platform.
   Ctrl+Z restores everything.
5. Delete on a locked level → ticker says unlock it. With only one level → ticker refuses.
6. Plan view, Select tool: hover the floor, a stair, a ticket machine → cyan outline + tag; selected things don't
   double-highlight.
7. Rectangle tool: move over a floor corner → magenta square marker + dashed outline of that floor.
8. Drag a ticket machine from LIBRARY over the plan → ghost footprint follows; drag out → it disappears; drop →
   placed as before.
9. PLAY ×8 on the demo, COLOUR ▸ STRESS: most people stay blue; queues held up for over a minute and a half
   (commuters, 180 s patience) turn yellow → red. Paused, the chips still switch. Stuck people blink a red ring.
10. Don't-regress list (HANDOVER §8).

## Judgement calls

- Delete takes items whose *home* is the level (a stair's bottom, a shaft's base); items only arriving there are
  left for CHECK, as D-142 says.
- Stress drains at half real time in free flow ("decays slowly"); belt riders count as moving, so standers on a
  slow escalator don't get stressed.
- The hover tag shows in Select only; while drawing, the snap target outline is enough and keeps the plan quiet.

## Known limitations

- Unbuilt. The LEVELS header buttons are tight at the panel's 270 px default; if they clip, widen the panel or
  tell me and I'll move them to a row of their own.
- No rename/elevation edit for a new level beyond the existing LEVEL tab.
- Stress has no summary in the status bar or a heatmap yet (M5).

## Watch list

- Hover cost: `Hover` recomputes connector layouts per redraw; fine for the demo, watch on big studies.
- Stress thresholds: if nobody ever goes red in the demo, half-patience may be too late for a visual.

## Open questions (next Query phase)

1. Journey templates: step types (go to device type, pass waypoint, queue, dwell, exit), chances per step,
   where a train door zone fits (D-141).
2. Profile mixes per emission rule (one rule, several profiles by share)? (Recommended: yes.)
3. Vehicle timetables: fixed headway first, or a timetable list? (Recommended: headway + dwell first.)
4. Stress summary: status-bar count of stressed people, and stress in the diagnostics? (Recommended: both.)

## Next

Gavin builds r008 and reports errors; then the Query phase for journeys, mixes and trains.

---

## r008a (fix from Gavin's r008 build)

- CS0246/CS1061 in `SceneRenderer`: the `AgentColoring` enum had been inserted inside `GpuAgents` (an `internal`
  class, so the script's search for `public sealed class GpuAgents` missed and it landed mid-file). Moved to its own
  file, `Render/Gl/AgentColoring.cs`. The Throng.App CS0006 was the knock-on (Render didn't build).
- Nothing else changed; still 193 tests expected.
