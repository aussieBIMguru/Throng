# Round 003 — Round 002 feedback + package pinning

**Status:** Done. Gavin built and ran it with **no issues** (2026-10-24).

## Feedback from the round 002 build (r002b)

- Builds and runs after the Clipper2 fix; the drawing tools are "generally great".
- Clicking **Visible** in the properties panel hid the whole plan. This is not a bug: that switch belongs to the
  LEVEL section under SELECTION, so it hid the active level. The layout invites the mistake, which D-045 and D-050 fix.
- Wanted: selection kept apart from level/study settings, a drag handle to push/pull edges, crisper and thicker
  lines, and (later) elements that span levels.

## Query record (answers → decisions)

| Topic | Answer | Decision |
|---|---|---|
| Right panel | SELECTION / LEVEL / STUDY tabs; selecting switches to SELECTION | D-045 |
| Object show/hide | Temporary hide (H) / isolate (I) / reset, view-only | D-046 |
| Edge handle | Separate arrow handle outside the midpoint; diamond still inserts | D-047 |
| Push/pull neighbours | Neighbours keep direction; normal move if parallel | D-048 |
| Line weight | 2 px screen-space quads, plan and 3D, 4× MSAA | D-049 |
| LEVEL tab toggles | Drop Visible (row eye only), keep Locked switch | D-050 |
| Cross-level | Stairs/escalators/ramps, shafts/voids, copy to levels, columns/cores — round 004 | D-051 |
| Scope | 003 = feedback + pinning; 004 = underlays, stairs, cross-level, palette | D-052 |

## Build scope (once approved)

1. Right panel tab strip (SELECTION / LEVEL / STUDY), auto-switch on selection (D-045).
2. LEVEL tab: remove the Visible switch, keep Locked (D-050).
3. Temporary hide/isolate: H, I, Esc / RESET VIEW chip; view state only, plan and 3D (D-046).
4. Edge push/pull arrow handles with snapping and true push/pull geometry (D-047, D-048), plus Core tests.
5. 2 px lines via a screen-space quad shader in both views, 4× MSAA on the GL control (D-049).
6. Pin package versions from `project.assets.json` (D-028) and finish the licence audit, if the files are supplied.

## Accepted defaults (cheap to change)

- Selecting switches to SELECTION; clearing the selection leaves the current tab as it is.
- Hidden/isolated state resets on level switch, new or open.
- Push/pull offsets snap to whole grid steps, unless an endpoint or midpoint snap is in range.

## Needs from Gavin (query phase)

- Go-ahead to build.
- `obj/project.assets.json` from `src/Throng.App`, `src/Throng.Render`, `src/Throng.Core` and
  `tests/Throng.Core.Tests`. Without them, pinning moves to round 004.

---

## What changed (build phase)

### Right panel (D-045, D-050)
- The right panel is now a **SELECTION / LEVEL / STUDY** tab strip, styled like the library tabs. Selecting
  anything in the viewport switches to SELECTION; clearing the selection leaves the tab alone. A new study opens on LEVEL.
- The LEVEL tab no longer has a Visible switch (the eye on the level row is the only visibility control). It keeps
  the Locked switch, the level fields and the green net walkable area.

### Temporary hide / isolate (D-046)
- **H** hides the selection; **I** isolates it (hides every other region on every level). They're also on the View
  menu. An orange **RESET VIEW (n HIDDEN)** button appears on the DRAW strip; **Esc** with nothing selected also resets.
- Hidden regions are left out of the plan and 3D scenes, can't be picked or box-selected, and don't give snap
  points. This is view-only: it isn't saved, doesn't create undo steps, and the walkable area and simulation still
  include hidden regions. It resets on level switch, new and open.

### Edge push/pull (D-047, D-048)
- With one region selected, every edge shows a small cyan arrow 16 px outside its midpoint, joined to it by a
  thin stem. Drag the arrow to push/pull that edge along its normal (outward for closed shapes, left side for walls).
- The neighbouring edges keep their direction (`Throng.Core.Geometry.EdgePushPull`), so rectangles stay rectangles
  and chamfers keep their angle. A parallel or missing neighbour (a wall end) moves straight along the normal.
- The offset rounds to whole grid steps; a nearby endpoint or midpoint (on this level or the one below) makes the
  edge pass through it instead. A readout shows the signed distance. One undo step: "Push/pull edge +1.5 m".
- Pulling an end past its neighbour's far vertex is refused while you drag ("EDGE TOO FAR"); crossings are
  rejected on release (D-040). The midpoint diamonds still insert vertices, and vertices still drag.

### Line weight (D-049)
- New `Shaders.LineGeometry`: a geometry shader that turns each GL_LINES segment into a screen-space quad with
  square caps, clipped at the near plane. Scene lines are **2 DIP** (scaled by DPI) in plan and 3D; the grid stays
  1 DIP so it doesn't compete. `ShaderProgram` now takes sources and supports a geometry stage.
- **4× MSAA:** `GLWpfControlSettings.Samples = 4` and `GL.Enable(Multisample)`.

### Packages (D-028)
- Pinned: Clipper2 1.5.4, CommunityToolkit.Mvvm 8.4.2, OpenTK.GLWpfControl 4.3.6, OpenTK.Graphics 4.9.4.
  `THIRD-PARTY-NOTICES.md` lists every resolved shipped package. GLWpfControl pulls in the whole OpenTK 4.9.4
  family and the native GLFW 3.4 DLL (zlib licence). Copyright lines still need copying from each package's
  licence file. MSTest and the test SDK still float (test-only).

### Tests
- `EdgePushPullTests` (5): outward normal for either winding, rectangle push/pull, too-far refusal, 45° chamfer
  neighbour, open-wall end segment.

## How to test (numbered checklist)

1. Run `clean.bat`, then **Rebuild Solution**. Send any errors back (see the watch list).
2. Run all tests: the 80 from round 002 plus 5 new.
3. **Tabs:** DEMO → F2 → click a region: the panel jumps to SELECTION. Click LEVEL and STUDY; both stay put until you
   select again. LEVEL has Locked but no Visible switch.
4. **Hide/isolate:** select two columns → H: they vanish in plan and 3D, and the RESET VIEW chip shows "2 HIDDEN".
   Select a floor → I: only it remains. Esc (nothing selected) brings everything back. Switching level also resets it.
   Box-select over hidden things picks nothing.
5. **Push/pull:** draw a rectangle → select it → drag the top arrow up 1.5 m (the readout shows +1.50). It stays a
   rectangle; Ctrl+Z undoes it in one step. Try the chamfered corner of the street entry hall: the 45° edge keeps its
   angle. Pull an edge past the opposite side: refused. Drag an arrow near a corner of another region: it snaps through it.
6. **Lines:** plan and 3D lines are visibly thicker and anti-aliased; the grid stays fine. Check at 100% and 150%
   display scaling if you can.
7. Don't-regress: the round 002 checklist steps 7–15 still pass (drawing, snapping, grips, clipboard, undo, save/reopen).

## Judgement calls

- Grid lines stay 1 DIP and scene lines go to 2 DIP. A 2 DIP grid would compete with the geometry.
- Isolate hides across every level, not just the active one, so the 3D view isolates too.
- The walkable area ignores hide/isolate, because it's simulation truth, not a view.
- New studies open on the LEVEL tab, since there's nothing to select yet.
- The edge arrow is hit-tested before the midpoint diamond (vertices still come first).

## Watch list

1. `GLWpfControlSettings.Samples`: I'm fairly sure 4.3.x has it. If it doesn't compile, delete `, Samples = 4`; the
   2 px lines still work without MSAA.
2. The geometry shader. If lines vanish or the viewport goes offline, the error text names the shader stage. Send it.
3. `OpenTK.redist.glfw` and the OpenAL/Compute/Input assemblies come in through GLWpfControl's dependency on the
   full OpenTK meta-package. They're harmless, just extra DLLs in the output.

## Next: round 004

Image underlays (two-point calibration), stairs/escalators/ramps as sloped surfaces, shafts/voids and
columns/cores spanning levels, copy to multiple levels, and library placement (D-051, D-052). It starts with a Query phase.
