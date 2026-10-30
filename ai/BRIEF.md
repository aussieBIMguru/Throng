# Throng — Project Brief (v0.34)

> Conceptual pedestrian simulation for architects. Sketch a space, drop in devices, press play, read the pressure.
> Status: **all decisions locked** (see §18). Ready for M0. Items marked ⏳ are deliberately deferred to later rounds.
>
> **v0.34:** round 016 built (unbuilt in sandbox), M7 validation & profiles: CHECK (F7) runs every rule (static model rules, the full compile with reachability per profile mobility, a static capacity check for devices, lifts and train bursts with the queue to expect and Fruin LoS notes, escalators against all demand, unused devices, walkable gaps narrower than the widest profile) into a new CHECK tab with filter chips and click-to-go rows; every finding names its rule and can be ignored per study (D-271–D-274, D-272). Profile exchange: .throngprofile = a profile and the journeys it's used with, imported through a keep / map / drop reconcile dialog (D-275). Saved named views in STUDY ▸ VIEW ▸ VIEWS (D-276) and a section box outline with six draggable face handles in 3D and ISO (D-277). Format v10 (views and ignored findings).
> **v0.33:** round 015 built (unbuilt in sandbox), M6 views polish: an ISO view (orthographic 2:1 game iso, four quarter-turn presets SW / SE / NE / NW, F4, [ and ] turn it; D-263, D-268), STUDY ▸ VIEW with clip above a level, a section box on six sliders and exploded levels (level meshes lift rigidly, stairs / shafts / cars / riders stretch to bridge the gap, all in the shaders; D-264, D-266), agent LOD (flat discs when people are small on screen, D-267); session only, works while running, no format change (D-265). Lift tails: RESULTS ▸ LIFTS per bank (boarded, landing waits mean / 95th / max, rides, gave up) and lifts.csv (D-260); semi-transparent 3D lift shaft walls with the landing doors left open (D-261). Format stays v9.
> **v0.32:** round 014 built (unbuilt in sandbox), M4 lifts finished: patience at lift landings (after the profile's patience people re-choose with that bank barred until they leave the floor; lift-only people keep waiting; landing waits count toward Long wait; D-253, D-254), destination dispatch (each person given a car grouped by destination, standing beside its door; also in schedule rows; D-255, D-256), 3D centre-opening sliding leaves on car and landing doors timed by the preset (D-258), one switchback stair in the Office tower (D-257), and the 013b re-review fixes (LIFTS click locked while running, a lift dropped off a shaft goes nowhere, doorway neighbours rebuilt only when someone gets off, 3D cars hide with their level; D-252). Format stays v9.
> **v0.31:** 013b built (unbuilt in sandbox), round 013 complete: 3D lift cars (translucent boxes at car height with riders inside, doors lit while open), people who need accessible devices board first at every lift and others see a lift 60 s slower while one waits (D-247, D-248), drawn train shapes per service (DRAW TRAIN, D-249, additive in v9), demo lifts street ↔ concourse and concourse ↔ platform with 2 % wheelchair users, goods shaft removed (D-246), and a second demo, New from Demo ▸ Office tower (D-250). r013a fixes: format test v9, LIFTS library click fills the selected shaft.
> **v0.30:** 013a built (unbuilt in sandbox): lifts in shafts (D-236–D-244). SELECTION ▸ shaft ▸ LIFT from a preset: 1–4 cars along the front edge (picked per lift; rear = the opposite edge), front/rear doors per level (in/out/both/closed) with automatic 2.5 m landings, lobby, Collective / Shuttle / Up-peak and a time-of-day schedule. Sim: lifts in route choice (timed rides, live wait, lift preference), cars with doors and collective stopping, calls assigned to the soonest car, off before on, arrival order with wheelchair priority, car space by body size. Plan symbols and running cars; format v9. Next: 013b 3D cars.
> **v0.29:** round 012 built (unbuilt in sandbox): measurement zone stats (occupancy, peak, mean, entries per minute and per metre of the narrower side; D-228), journey stats per profile × journey with ALL rows (mean / 95th, % within acceptable, % with a single wait over Long wait, gave up, stuck, in progress; people spawned after the warm-up; D-229–D-231), RESULTS table chips DEVICES / ZONES / JOURNEYS and a ZONES graph (D-232), zones.csv + journeys.csv + zone columns in timeseries.csv (D-233), EXPORT PNG at 2× with legend and title block (D-234). Long wait in STUDY ▸ RUN, additive in v8.
> **v0.28:** round 012 queries answered (D-227–D-235): measurement zone stats, per-journey stats (profile × journey) and PNG export with a title block, in one build; Long wait setting additive in v8. Lifts decided for round 013 (D-236–D-240): in shafts, banks of 1–4 cars with front/rear doors, Collective / Shuttle / Up-peak / time-switched dispatch, crowd landings; 013a sim, 013b 3D.
> **v0.27:** 011c (unbuilt in sandbox), round 011 complete: RESULTS tab on the right (D-219) with a sortable device table (served, mean / 95th / max wait, max queue, utilisation, balked, gave up; after the warm-up, D-218), a hand-drawn graph (D-220) of people in model, queues (ticked rows, else the five longest, D-226), peak density over the walkway Fruin bands and flow in / out per minute (D-221), the warm-up shaded, sampled every 5 s; EXPORT CSV writes devices.csv and timeseries.csv (D-222). Clicking a device while running selects it and lights its row. Format stays v8.
> **v0.26:** 011b (unbuilt in sandbox): cumulative run maps (max density on the LoS bands, time at LoS E or worse, mean density, footfall trail; D-216) gathered after the warm-up (D-218) and kept until RESET, picked from a RUN chip group beside the live MAP chips (D-217). Hard accessible-device rule (D-213): presets and placed devices carry "Accessible" (preset default, per-device override, D-223), the wheelchair profile needs accessible devices, others soft-avoid them (+15 s unless they favour them, D-224), CHECK warns where a level has none (D-225). Format v8 (accessible flags only; results aren't saved, D-215). Round 011 is split once: 011c = RESULTS tab, device stats, graph, CSV (D-214, D-219–D-222, D-226).
> **v0.25:** r011a-2: lane centreline steering and sideways-only wall push at gate lanes (D-211), demo gates 5 m further south (D-210), cumulative maps confirmed for 011b (D-212). 011a (unbuilt in sandbox): M5 starts. Live heatmap while running, MAP ▸ NO MAP / DENSITY / LOS (D-201), drawn by the renderer on floors and stair/ramp flights in plan and 3D (D-202); 0.5 m cells, 1 m kernel, ~5 s smoothing (D-203); Fruin table per place: stair on flights, queuing in queue lines/snakes/dwell zones, walkway elsewhere, belts unmapped (D-205); §11.3 values as-is (unverified), legend and peak-LoS ticker (D-204); classic or colour-blind-safe ramp as a STUDY setting (D-206); people drawn smaller and dimmed over a map (D-207); live only, format stays v7 (D-208). Wheelchair gate fixed: people wait to start through a lane until whoever went before is past the gate's middle; walk-through timeout only without progress (D-209). Next: 011b cumulative maps, device stats, time series, CSV.
> **v0.24:** 010b (unbuilt in sandbox): train services over door zones (headway, first arrival, doors-open time, people getting off, places; D-184, D-185), the doors' own emission rules say who gets off, doors take boarders only while a train is in, alight first then board, LEFT BEHIND (D-186, D-187); Board train journey step; format v7 (D-188); demo with southbound and northbound services every 6 min, 3 min apart (D-189); plain train box and lit doors in the plan while running (D-190). Stair landings: deep carry on the outside of the turn, early turn on the inside (D-192). Stress from the first second held up (D-193) and from density above tolerance (D-194).
> **v0.23:** 010a (unbuilt in sandbox): crowd-aware choice. Stairs and ramps measured like belts (D-181); live waits only for connectors and exits within each profile's sight radius (default 30 m, D-179); re-choosing every 3 s while the chosen option is in sight (D-180); nearest-sink rules weigh exit crowding (D-182); hover shows crowd waits while running (D-183). Stress counts walkers as held up by progress along their route, not instantaneous speed, and queuers always (D-191). Trains next (010b, D-184–D-190).
> **v0.22:** 009c (unbuilt in sandbox): stair effort in route choice, per profile (stairs up feel ×2.0, down ×1.4 by default; commuters milder, luggage/elderly/family stronger), so escalators win unless queued (D-174). 009b fixes: device fields close other gates' lanes (D-172), balk counts the current device's preference (D-173).
> **v0.21:** 009b built (unbuilt in sandbox, format stays v6, D-169): journey editor in SELECTION (LIBRARY ▸ JOURNEYS rows open it; built-ins read-only with DUPLICATE; steps add / remove / reorder, kind, target, needs / lacks / grants, dwell, give-up), give-up per device step (keep waiting / leave / jump to step, D-165), profile device preferences (wheelchair and luggage favour the accessible gate, D-166), DRAW QUEUE as one shared snake feeding every lane with slot dots on the selected device (D-163, D-164), hold points 2 m out beside the lane mouths (D-167), status bar QUEUING / STRESSED / GAVE UP (D-168).
> **v0.20:** 009a built (unbuilt in sandbox): journeys (go to device type, pass zone, dwell, exit) with need / lack / grant tags, mix lines on emission rules, device queues as automatic lines per lane and side with one direction per gate lane, walk + wait device choice with balk, format v6, demo ticket/gate/retail journeys with eight machines (D-159).
> **v0.19:** round 009 queries answered (D-148–D-158): journeys with tags (need / lack / grant) instead of % per step, profile mix lines (profile + journey + share), device queues as automatic lines with drawn overrides, fastest walk + queue device choice, format v6, demo ticket/gate journeys with retail dwell. Built in two parts (009a, 009b). M3 task lists brought forward.
> **v0.18:** round 008 (unbuilt in sandbox): add/remove levels with storey split and undo (D-142, D-143), hover highlight for pick and snap targets plus a LIBRARY drag ghost (D-144), per-agent stress with a PROFILE / STRESS colour toggle (D-145, D-146). Journey templates planned as library steps with chances (D-141).
> **v0.17:** r007p (unbuilt in sandbox): per-profile mechanised preference scales escalator/travelator ride and queue in route choice (D-138); stalled-queue stair wait off by default (D-139). Gaps found: no add/remove level command (M1 scope); hover highlight wanted in the editor. Stretch goal noted: visual node editor for profiles and journeys (§8.2).
> **v0.16:** r007k (unbuilt in sandbox): stair and ramp lanes only with real counterflow (D-132); escalators weighted up (boarding-rate seed 0.8 × cap, stalled stair queues count in stair waits; D-133).
> **v0.15:** round 007 part 2 (r007i, unbuilt in sandbox): boarding spacing at metered belts so crowds approach the step-loading cap (D-128), rate curves on emission rules (presets + editable points, multiplier on the rate, holds the last value; D-129), file format v5 (D-130).
> **v0.14:** round 007 part 1 built (unbuilt in sandbox): escalator-end rules (keep-clear run-offs, walk clear, boarding hold), deferral to nearby options (per-agent belt cost, measured waits, light balk), one-way Up/Down stairs and ramps, floors hide agents in 3D, dead-end piece walls (D-112–D-118).
> **v0.13:** round 006 built (unbuilt in sandbox): M2 movement core. Nav grids per floor and connector piece, portals and a portal graph, cached flow fields, social force with keep-side bias, escalator belts with step-loading caps, emission rules on spawn zones (constant / Poisson), RUN toolbar with edit lock and CHECK gate, status counters, instanced capsule agents, LIBRARY drag-and-drop, staff-only connectors, file format v4 (D-095–D-110).
> **v0.12:** round 005 built (unbuilt in sandbox): multi-level obstacles and walls with Fixed / Full storey heights, excluded levels (shaft "no stop"), stacked per-level switchbacks, devices and travelators placed with PLACE, calibrated image underlays, file format v3 (D-075–D-094).
> **v0.11:** round 004 built (unbuilt in sandbox): connectors (straight/switchback stairs, escalator pairs, ramps) placed with CONNECT, linked auto openings, shafts spanning levels, Copy to Levels, CHECK rules, file format v2, demo vertical set. Remaining preset values answered (D-069–D-074).
> **v0.10:** r003 confirmed building and running. Round 004 queries answered (D-053–D-067): connectors (straight + switchback stairs, escalator pairs, ramps) with linked auto openings, multi-level shafts, Copy to Levels, format v2, demo vertical set. Underlays, cores and library placement move to round 005.
> **v0.9:** round 003 built: right panel tabs, hide/isolate, edge push/pull, 2 px lines + MSAA, packages pinned (D-045–D-052). Rest of M1 plus cross-level elements in round 004.
> **v0.8:** round 002 built (unbuilt in sandbox): UI polish + M1 core editor (sketch tools, snapping, selection, grips, typed dims, Clipper2 walkable union). Decisions D-028–D-044. Rest of M1 in round 003.
> **v0.7:** base working protocol added (`/ai/PROTOCOL.md`, D-027): every round is Query → Build; queries first with recommendations, then only decided scope is built. Round 002 queries answered (D-028–D-042): M1 split over rounds 002 (core editor) and 003 (underlays, stairs/escalators, palette).
> **v0.6:** M0 accepted (compiles and runs). UI polish decisions (library tabs + search, eye toggles, mono input font), run-control placement, user library location and plan-only M1 editing locked (D-021–D-026); `clean.bat` added.
> **v0.5:** renamed WalkAbout → **Throng** (files `.throng` / `.throngprofile` / `.thronglib`); VT323 font; built-in catalogue defined in code; agent profiles stored in `library.json`; M0 delivered (see `/ai/rounds/round-001.md`).
> **v0.4:** built-in demo study (§21) and template library of devices, agent profiles and schedules (§22), both from M0/M1; roadmap and file format updated.
> **v0.3:** remaining decisions locked (D-005 to D-014); licensing section added (§20).
> **v0.2:** WPF confirmed; fresh renderer (no shared code with other projects); 3D-first spatial and navigation model so vertical circulation is native from the start (§3, §7); roadmap reordered (§16); scale target raised for stadium-type studies (§17).
> Lives at `/ai/BRIEF.md` in the project zip.

---

## 1. Purpose and positioning

Throng is a standalone Windows desktop app that lets architects test circulation in early-stage designs. It is **not** a certified egress or crowd-safety tool. The goal is fast, legible, "roughly right" behaviour that exposes bottlenecks, queue pressure and journey-time problems while a design is still cheap to change.

**Design principles**

1. **Setup speed beats fidelity.** If something takes more than a few clicks to model, simplify the concept.
2. **One mental model for devices.** Everything an agent goes to, waits at, or passes through is a *node* with the same core properties (§4).
3. **Explainable results.** Every metric should be traceable to something you can see on screen.
4. **Reproducible runs.** Same file + same seed = same result.
5. **Honest about precision.** The UI labels outputs as indicative, never as compliance evidence.

**Out of scope (v1):** fire/egress certification, BIM round-tripping, networked/multi-user editing, physics-accurate crowd crush modelling, scripting/API.

---

## 2. Platform and stack

| Area | Choice | Notes |
|---|---|---|
| Runtime | .NET 10 (LTS), C# latest, nullable enabled | Framework-dependent build to start |
| UI shell | **WPF** ✅ | `net10.0-windows`, `UseWPF` only (no WinForms, avoids implicit-using clashes) |
| MVVM | CommunityToolkit.Mvvm (MIT) | Source-generated observable properties/commands |
| Rendering | **Fresh OpenGL 3.3+ core renderer** ✅ hosted via **OpenTK + GLWpfControl** ✅ (both MIT) | GLWpfControl renders into a WPF `D3DImage`, so WPF overlays (tool HUDs, tooltips, rubber-band selection, context menus) can sit **over** the viewport, avoiding WPF's `HwndHost` airspace problem. One renderer serves plan, isometric, 3D orbit and cutaway — camera + clip-plane presets. Agents drawn via instancing |
| Charts | ScottPlot (MIT) | Time-series, histograms |
| Geometry | **Clipper2** (Boost SL 1.0) ✅ for booleans and offsetting; own code for ear-clip triangulation and point-in-polygon | Behind an `IPolygonOps` interface in Core; fixed-precision scaling (1 mm) at the boundary |
| Serialization | System.Text.Json + System.IO.Compression (in-box) | No dependency |
| Tests | MSTest (MIT) | Engine and file format get unit tests; UI does not |
| Fonts | **VT323** (SIL OFL) ✅ for titles/toolbar only, falls back to Consolas if absent | Body text uses Segoe UI / system font |

**Dependency rule:** see §20.

### 2.1 Solution layout

```
Throng.sln
├─ src/
│  ├─ Throng.Core        Model types, geometry, IDs, library + built-in catalogue, undo stack, validation (no UI, no GL)
│  ├─ Throng.Sim         Engine: navigation, movement, nodes, queues, lifts, clock, RNG
│  ├─ Throng.Analysis    Metric collectors, heatmaps, LoS classification, report data
│  ├─ Throng.IO          .throng file format, versioning/migration, demo study builder
│  ├─ Throng.Render      OpenGL renderer, cameras, scene building, picking, instanced agents
│  └─ Throng.App         WPF shell, tools, panels, theming
├─ tests/
│  ├─ Throng.Core.Tests
│  ├─ Throng.Sim.Tests       (also covers Throng.Analysis)
│  └─ Throng.IO.Tests
├─ ai/                  Brief, round notes, decision log (AI exchange folder)
├─ README.md
└─ THIRD-PARTY-NOTICES.md
```

Dependency direction is strictly downward: `App → Render/Analysis/IO → Sim → Core`. The engine must run headless (enables batch studies and tests later).

### 2.2 Code conventions

- XML doc headers on all public and internal types/members; inline commentary for non-obvious logic.
- Nullable reference types on; prefer `T?` over sentinel values.
- Explicit types where clearer; `var` where `new()` or the right-hand side makes the type obvious.
- LINQ where it's not in a hot path. **The sim tick is a hot path** — no LINQ/allocations per agent per tick there.
- No exceptions surfaced to the user: operations return result types (`Result<T>` / validation messages); exceptions are caught at the boundary and logged.
- SI units internally (metres, seconds, m/s). Display formatting is a UI concern.

---

## 3. Spatial model (3D-first)

### 3.0 Principle
Everything lives in **one 3D world space** (Z up, metres) from day one. Levels are a convenience for *authoring* (sketch on a plane), not a limit on *simulation*. The engine never assumes agents are on a flat floor; it works with **walkable surfaces** connected by **portals**, and a level floor is just the most common kind of surface.

```
World
 ├─ Level (authoring plane: elevation, height, visibility)
 │   └─ Regions sketched in plan → compiled to Surfaces
 ├─ Surface      walkable area + height function (flat now; sloped/ramp/raked later)
 │                 └─ NavGrid (2D grid in the surface's own UV space)
 ├─ Connector    stair / escalator / travelator / ramp — itself a Surface (see §7)
 ├─ LiftBank     cars + shafts + landing portals
 └─ Portal       an edge/zone where an agent can move from one surface to another
```

- Agent position is always stored as `(SurfaceId, u, v)` with a derived world `Vector3` for rendering and analytics. Moving onto a stair is the same operation as moving between any two surfaces: cross a portal.
- **Navigation is hierarchical from Round 1:** a coarse *portal graph* (surfaces = nodes, portals/lifts = edges with cost) for route choice, and per-surface flow fields for movement. A single-level study is just a graph with one surface. This means stairs and lifts are not retrofitted later.
- **Forward-looking (not v1, but the data model allows it):** sloped surfaces (ramps, raked stadium tiers), split levels/mezzanines at any elevation, and seat-block destinations for stadium filling.

### 3.1 Levels
- A study contains ordered **levels**, each with a name, elevation (m) and floor-to-floor height. Any elevation is allowed (mezzanines, split levels).
- Levels can be shown/hidden/locked; one is "active" for editing. The 3D view shows all levels stacked at true elevation from the first build.

### 3.2 Regions
Sketched as closed polylines (rectangle tool, polygon tool, circle/arc tool approximated as a polygon).

| Region type | Meaning |
|---|---|
| **Walkable** | Floor area agents can occupy. A level's walkable area is the union of these |
| **Obstacle** | Subtracted from walkable (columns, furniture, voids, walls) |
| **Wall** (polyline, not closed) | Thin line obstacle with thickness |
| **Zone** | Non-blocking tag area: queue zone, measurement zone, spawn/sink zone, slow zone (speed multiplier) |

- Snapping: grid, endpoints, midpoints, orthogonal. Typed dimensions while drawing.
- **Underlays:** PNG/JPG images scaled by two-point calibration, in M1 (D-009). ⏳ DXF linework tracing parked.

### 3.3 Navigation representation
- Each **surface** (level floor or connector) is rasterised to its own **navigation grid** in surface-local UV space (default 0.25 m cells, configurable).
- Portals are marked as cell runs on both grids they join, so flow fields chain across surfaces cleanly.
- Rationale: robust with messy hand-sketched geometry, trivially supports density/heatmap sampling (including on stairs), and makes flow fields cheap. A navmesh is more elegant but harder to get robust; revisit only if grid resolution becomes a problem.

---

## 4. The node model (unifying devices)

Every place an agent goes to, waits at, or passes through is a **Node**. Ticket machines, counters, turnstiles, security, gates, and spawn/sink points are all configurations of the same thing.

### 4.1 Node core properties

| Property | Description |
|---|---|
| Type tag(s) | e.g. `TicketMachine`, `Gate`, `Security`, `Exit`. Agents request **types**, not instances, unless pinned |
| Footprint | Position, rotation, size; service position(s) where an agent stands while served |
| Servers / channels | Number of agents served simultaneously (1 for a ticket machine, n lanes for a gate line) |
| Service time | Fixed, or distribution (uniform / triangular / normal-clamped / exponential) |
| Capacity | Max agents committed (in queue + in service) before the node reports "full" |
| Queue | None / attached queue zone / auto-crowd (see §5) |
| Pass-through | If true, agent exits on the far side (turnstile, security); if false, agent turns around (ticket machine) |
| Choice weight | Bias added to this node's attractiveness when agents choose between same-type nodes |
| Availability schedule | Open/closed times; can be closed mid-study |
| Next destination | Optional forced next node/type (e.g. "after security → departures") |

### 4.2 Node choice
When an agent needs a node **type**, it scores every reachable, open node of that type:

```
cost = walkTime + expectedWait + weightPenalty (+ small random noise)
```

where `expectedWait ≈ (queueLength / servers) × meanServiceTime`. The agent takes the lowest cost; re-evaluation happens at a set interval or when a queue it's heading to grows sharply (with a hysteresis threshold so agents don't dither). Noise level is a per-profile "decisiveness" setting.

### 4.3 Special node families
- **Spawn / Sink** nodes and zones (§6) — the same Node with an emitter and/or absorber attached.
- **Barrier-type** nodes (turnstile, security, gate) — just pass-through nodes with short service times and multiple lanes, with library presets. No separate class (D-013).

---

## 5. Queueing

Each node chooses one queue mode:

| Mode | Behaviour |
|---|---|
| **None** | Agents cluster freely near the service point (good for low-demand devices) |
| **Auto-crowd** | No drawn queue. Agents hold at a standoff radius in a loose cluster and are served FIFO by arrival time |
| **Linear queue** | Drawn polyline (straight, L, serpentine). Agents stand at spacing slots along it. Overflow behaviour: extend along last segment / spill as crowd / reject (agent re-chooses) |
| **Shared queue (single-line, multi-server)** | One queue feeds several nodes (bank-style "next available"). Head of queue routes to the first free server |
| **Batch release** | Queue releases N agents every T seconds or when N are waiting (boarding gates, timed entry) |

Queue properties: slot spacing (default 0.5 m), max length, overflow policy, balking/reneging (agent declines a queue, or leaves one, when expected/actual wait exceeds its patience; D-010).

Multiple queues to one node and one queue to multiple nodes are both supported via explicit links drawn in the editor.

---

## 6. Spawns and sinks

- **Point** or **zone**; a single object can be spawn, sink, or both (e.g. a train platform edge).
- **Emission modes:** constant rate (agents/min), Poisson (random arrivals at a mean rate), scheduled batches (e.g. train: 300 agents over 45 s every 6 min), or a timetable table.
- **Absorption:** unlimited, capacity-limited, or scheduled (train only accepts boarding while "present"). Waiting-to-board agents queue at the sink using §5 rules.
- Each emission rule references an **agent profile mix** (e.g. 70% Commuter, 20% Tourist, 10% Mobility-impaired).
- A **train/vehicle event** is a composite: arrival → unload batch → board window → departure. Modelled as a scheduled spawn+sink pair on one object.
- Emission, absorption and availability rules are normally picked from **schedule templates** (§22.4) and then optionally edited.
- **As built (round 006, D-096, D-099):** emission rules live on spawn zones (SELECTION ▸ EMISSION RULES): one
  profile, a rate per minute, constant or Poisson spacing, and either the nearest sink (quickest route, a little noise,
  never the zone itself) or weighted chosen sinks. Sinks absorb without limit. Round 007 part 2 (D-129) adds a rate
  curve per rule: minutes from the run start → multiplier on the rate, straight lines between points, the last value
  held after; presets Flat, AM peak, PM peak and Event surge fill in editable points. Timetables, batches and profile
  mixes follow.

---

## 7. Vertical circulation

### 7.1 Stairs, ramps, escalators, travelators — one "Connector" type
| Property | Stairs | Escalator / travelator |
|---|---|---|
| Direction | Bidirectional (optionally lanes split) | One-way, reversible on schedule |
| Speed | Agent speed × stair factor (≈0.5–0.7 up, ≈0.6–0.8 down) | Belt speed + optional walking-on-belt fraction |
| Capacity | Width-based flow limit (people/m/min) | Fixed throughput (people/min) |
| Entry | Landing zone on each level, auto-queues when saturated | Landing zone + merge/queue |

- Connectors link a landing on level A to a landing on level B (or more, for multi-flight stairs). They are placed in plan with a footprint, flight count, landings and direction arrow, and appear **as true sloped 3D geometry from Round 1**.
- **A connector is a Surface.** Its walkable area is the unrolled flight(s) + landings (width × run length), with a height function mapping run position to world Z. Agents on it use the *same* local movement and density logic as on a floor, with speed scaled by the stair factor and, for escalators, a belt velocity added. Consequences:
  - Crowding, LoS and heatmaps work on stairs with no special case.
  - Agents render at correct height climbing the stair in 3D.
  - Bidirectional stairs get natural lane formation from the movement model; an optional "keep left/right" bias makes it tidier.
- Escalator capacity is enforced at the entry portal (step-loading rate) rather than by physics.
- **As built (round 004):** a `Connector` stores its bottom/top level, bottom landing centre, run angle, width,
  direction, shape (straight / switchback, return side) and geometry values copied from its preset; the flights,
  landings and footprint are derived by `ConnectorLayout` from the real level-to-level rise (D-054). Stairs:
  0.17 m target riser (actual = rise ÷ round(rise ÷ 0.17)), 0.28 m going, max 18 risers per flight, landings as
  long as the width (min 1.2 m) (D-057, D-069). Escalators: 30°, 1.2 m flats each end, pairs 0.6 m apart (D-071).
  Ramps: 1:14 with a 1.2 m landing every 9 m (D-072). The upper level loses every part of the connector with
  less than 2.2 m headroom, plus the clearance (default 0.3 m), except at the exit edge (D-055, D-065); the lower
  level loses the footprint. Shafts (outline + base/top level) are cut from every level in between (D-056).
- **Round 005:** switchback stairs and ramps spanning 2+ storeys default to **one circuit per level** (a stacked
  core at the same landing, each circuit fitted to its storey, D-091–D-092); intermediate levels can be excluded
  (no exit, still cut, D-093). Travelators are one-level belts placed with PLACE (D-090). Shafts can mark levels as
  "no stop" for M4 lifts (D-087).
- **Round 006:** connectors can be **staff only** (D-110): public agents never route on them (staff routes in M3).
  Escalators carry people at the preset belt speed (0.5 m/s default), standers on the keep side and walkers on the
  other, and meter entry at the preset throughput (100/min default). Up/down follows the unit; reversible units run
  up until direction schedules arrive.

### 7.2 Lifts
- **Lift bank** = one or more cars sharing hall-call buttons; each car has capacity (persons), speed (m/s), door open/close time, dwell per stop, served levels.
- **Door typology per served level (D-068):** each car has one or two doors (front and/or rear, i.e. single-entry or through-car). Each door at each served level is set to *entry*, *exit*, *both* or *closed*, with its own landing zone on that side. This models through-cars (enter one side, leave the other), split-level lobbies and stops served from one side only.
- **Landing zones** per served level: agents wait here, press a call (up/down), board when a car with space arrives, then select a destination floor.
- **Dispatch profiles (simple, selectable):**
  - *Collective* (classic up/down collective — stop for calls in direction of travel) — default
  - *Shuttle* (two-stop back-and-forth)
  - *Up-peak* (return to lobby when empty)
  - ⏳ *Destination dispatch* — later round
- Agents treat lift vs stairs as a route choice using the §4.2 cost model, with a per-profile "lift preference" bias (mobility-impaired = lift only).
- **3D representation:** shaft as a vertical volume spanning served levels; each car is a moving box whose world Z is driven by the lift's motion model (accelerate / cruise / decelerate). Agents inside a car are held at slot positions in car-local space, so they visibly ride up and down in 3D and cutaway views.
- In the portal graph a lift bank is an edge between every pair of served landings, with cost = expected wait (from current calls and car positions) + travel time.
- **Built in 013a (D-236–D-244):** a bank lives in a shaft (`Shaft.Lift`), 1–4 cars side by side along a front edge picked per lift, rear doors on the opposite edge, automatic landings (edge × 2.5 m), lobby per lift, Collective / Shuttle / Up-peak with a time-of-day schedule, car space by body size, wheelchair priority at accessible-priority lifts. Route cost = timed ride + half the car interval, plus the live wait (people waiting ÷ places per round trip) in sight. 3D car boxes in 013b.

---

## 8. Agents

### 8.1 Profile properties
| Group | Properties |
|---|---|
| Body | Radius (default 0.25 m), height (visual only) |
| Movement | Desired speed distribution (default normal, mean 1.34 m/s, SD 0.26, clamped 0.6–2.0), stair factors |
| Behaviour | Patience (s), decisiveness (choice noise), personal space preference, lift/stair preference, mechanised preference (escalators, travelators, lifts; D-138), walking-on-escalator probability |
| Tasks | Ordered or unordered task list (see 8.2) |
| Thresholds | Acceptable journey time, acceptable wait per node type — used for "acceptable journey" metrics |
| Display | Colour, label |

### 8.2 Tasks and routes
An agent's itinerary is a list of **steps**:
- `GoTo(nodeType | specific node)` — e.g. GoTo(TicketMachine)
- `Visit(zone)` / `Dwell(zone, time)` — e.g. linger in retail zone 0–120 s
- `PassThrough(waypoint)` — forced routing via a point (for prescribed routes)
- `Exit(sinkType | specific sink)`

**Stretch goal (Gavin, r007p):** a visual node editor for profiles and journey types: steps as nodes, wired in
sequence, with chances on the links (e.g. 30% need a ticket machine). The journey model should stay a plain graph
so such an editor can sit on it later.

Steps may be **conditional** on a tag the agent carries (e.g. "has ticket" skips ticket machine; X% of profile spawns with the tag). This covers "get a ticket before using the gate" without a scripting language.

**As built (009a, D-149–D-155, D-159):** journeys are library templates of steps GoToDevice(type), PassZone(tag or zone), Dwell(tag or zone, time), Exit; each step can need, lack or grant a tag. Emission rules hold mix lines (profile + journey + share + spawn tags). Zone steps match `Region.Tag`, so built-in journeys work in any study. Prescribed waypoints (`PassThrough(waypoint)`) wait for a later round.
**As built (009b, D-161–D-169):** clicking a journey in LIBRARY ▸ JOURNEYS shows its step grid in SELECTION; built-ins (and the copies a study embeds) are read-only, DUPLICATE makes an editable copy in the study library. Device steps carry "if patience runs out": keep waiting (stress only), leave the study (counted as gave up) or jump to step N; patience is the profile's and counts from first stopping at the device. Profiles carry device preferences (preset key or type tag → seconds off the choice cost). A drawn queue is one shared snake feeding every service-side lane (bank style).

### 8.3 Movement model
- **Global path:** the agent plans on the **portal graph** (§3.0) to get a sequence of surfaces/portals, then follows per-target **flow fields** (Dijkstra / fast-marching on each surface's nav grid) toward the next portal or final target. Flow fields are computed per target per surface and cached.
- **Local movement:** simplified **social force** (Helbing & Molnár 1995): attraction to the flow-field direction at desired speed, repulsion from nearby agents and walls, plus a mild tangential "sidestep" to break head-on deadlocks.
- **Spatial hashing** for neighbour queries.
- **Fixed timestep** (default 0.1 s sim time), decoupled from render rate. Sim speed ×0.5 to ×max (as fast as possible).
- **As built (round 006):** `Throng.Sim.Nav` rasterises each level's net walkable floor and each connector piece to
  0.25 m grids; piece joints and floor ends become portal segments and search links. Per served floor end a field on
  its floor leads to it and a field over its connector leads off at it; per sink a field leads into it (reverse
  Dijkstra, nominal seconds with stair, ramp and belt factors). A Bellman–Ford pass gives the remaining time from
  every portal to every sink, per mobility class (step-free profiles skip stairs and escalators). Route choice
  (D-100) picks the fastest option with decisiveness noise and per-profile stair corrections, at spawn and on
  stepping onto a served level. `Throng.Sim.Engine` moves structure-of-arrays agents with social force
  (A 2.1, B 0.3, anisotropy 0.35, walls 3 / 0.2), a keep-side push when meeting oncoming people (D-105), escalator
  standers and walkers on the belt with a token-bucket entry cap (D-107), and the stuck rule (flag 30 s, remove
  120 s, D-106). `SimRunner` runs it on its own thread with double-buffered snapshots.

### 8.4 Frustration and status
A 0–1 frustration value that rises when:
- actual speed < X% of desired speed (crowding/obstruction),
- waiting in queue beyond expected wait,
- re-routing or rejected from a full node,

and decays slowly in free flow. Drawn as a colour ramp (cool → hot). Agents fully **stuck** (no progress for N seconds) get a highlight ring/flash and are logged.

### 8.5 Visual
Simple instanced person: cylinder/capsule body + sphere head, body coloured by profile or frustration (toggle), head slightly lighter. Direction indicated by a small nose/shoulder offset. LOD to dots when zoomed out.

### 8.6 Profile import/export and reconciliation
- Profiles export/import as standalone JSON (`*.throngprofile`).
- On import, any referenced **node type / tag / zone type** not present in the study triggers a **reconcile dialog**: map to an existing type, create the type, or drop the step. Missing properties take documented defaults and are flagged.

---

## 9. Simulation clock and study modes

- **Clock:** sim seconds since start + user-set **start time of day** → displayed as `07:42:15 AM` (with optional day counter).
- **Study end conditions** (one or combined):
  - Fixed duration (e.g. 07:00–09:00)
  - Until N agents have completed journeys (reports time taken)
  - Indefinite (run until stopped; steady-state observation)
- **Warm-up period** excluded from statistics (optional).
- **Seed:** every study has an RNG seed. ⏳ "Run N replications with different seeds and average" in a later round, cheap once the engine is headless.

---

## 10. Validation ("is this study irrational?")

Run on demand and automatically before Play. Results listed as **Error / Warning / Info**, each clickable to zoom to the offending object.

Examples:
- **Errors:** destination unreachable from a spawn; agent task requires a node type that doesn't exist; connector landing outside walkable area; lift with < 2 served levels; queue zone not linked to any node; overlapping/self-intersecting regions; zero-capacity sink with agents routed to it.
- **Warnings:** sustained spawn rate exceeds the theoretical throughput of the only route (capacity check: `Σ servers / meanServiceTime` vs arrival rate); node never used by any profile; escalator direction opposes all demand; walkable gap narrower than agent diameter; levels with no vertical connection.
- **Info:** expected Fruin LoS from a static capacity estimate.

The static throughput check is valuable on its own: "your 2 gates can process 40 p/min, your train delivers 300 in 45 s — expect a ~7 min queue."

---

## 11. Results and analytics

### 11.1 Live overlays (toggle during run)
- Density heatmap (persons/m², smoothed over the nav grid)
- Level of Service map (Fruin colour bands)
- Speed ratio map (actual/desired)
- Frustration overlay (agents)
- Queue length labels on nodes

### 11.2 Cumulative maps (post-run, also live-updating)
- **Max density** and **mean density** per cell
- **Time at LoS E or worse** per cell (the most useful "pressure" map)
- **Trail map** (cumulative footfall / usage)
- Built in 011b (D-216–D-218): gathered from the 5 s smoothed live map at every 0.5 s sample once `WarmUpS` has
  passed, kept until RESET, not saved in the file (D-215). MAP ▸ RUN ▸ MAX / E+ TIME / MEAN / TRAIL; max uses the
  LoS bands, mean the density ramp, E+ time and trail a ramp scaled to the map's top value (shared over levels).

### 11.3 Level of Service (Fruin) bands
Area per pedestrian (m²/ped). Values to be verified against source before shipping.

| LoS | Walkways | Queuing areas | Stairs |
|---|---|---|---|
| A | > 3.25 | > 1.21 | > 1.86 |
| B | 2.32–3.25 | 0.93–1.21 | 1.39–1.86 |
| C | 1.39–2.32 | 0.65–0.93 | 0.93–1.39 |
| D | 0.93–1.39 | 0.28–0.65 | 0.65–0.93 |
| E | 0.46–0.93 | 0.19–0.28 | 0.37–0.65 |
| F | < 0.46 | < 0.19 | < 0.37 |

Each cell uses the table appropriate to what it is (walkable / queue zone / connector). Fruin letters are the default; an abstract Low/Medium/High/Critical toggle serves non-specialist audiences (D-012).

### 11.4 Statistics
- **Per node:** agents served, mean/95th/max wait, max queue length, utilisation %, balk/renege count. Built in
  011c (RESULTS tab, after the warm-up).
- **Per journey:** journey time distribution by profile, % within acceptable time, % with any wait over threshold, stuck events.
  Built in 012: profile × journey rows plus ALL per profile, people spawned after the warm-up (D-229–D-231).
- **Per measurement zone:** occupancy over time, peak occupancy, flow through gate lines (p/min/m). Built in 012: entries
  per minute and per metre of the zone's narrower side (D-228).
- **Time-series graph:** agents in system, throughput, mean frustration, queue lengths — scrub-linked to the replay timeline.
  Built in 011c without replay: people in model, queues, peak density over Fruin bands, flow in / out, every 5 s.

### 11.5 Export
- CSV of all statistics and time series (011c: devices.csv + timeseries.csv; 012: + zones.csv, journeys.csv, zone columns)
- PNG of current view / heatmaps (with legend and study title block). Built in 012 at 2× the viewport (D-234)
- ⏳ One-page PDF summary report — later round

### 11.6 Replay
Agent positions recorded at a coarser interval (e.g. 0.5 s) into the results block so a run can be scrubbed without re-simulating. Stored in the file optionally (can be large).

---

## 12. Views and interaction

### 12.1 Views
| View | Use |
|---|---|
| **2D plan** | Primary editing view, one level at a time, ghosted adjacent levels. Technically an orthographic top-down camera in the same 3D renderer, not a separate 2D canvas |
| **Isometric** | Fixed-angle (true iso or 2:1 "game" iso), stacked levels with adjustable vertical exaggeration/exploded spacing. **As built (015):** orthographic 2:1 game iso, ISO button / F4, presets SW / SE / NE / NW turned with [ and ] (D-263, D-268) |
| **3D orbit** | Free camera |
| **Cutaway** | Any 3D/iso view with a horizontal clip plane per level and/or a vertical section box. **As built (015):** STUDY ▸ VIEW: clip above a chosen level, a section box set with six sliders over the study's extents, and an explode slider (0–30 m between levels); session only, works while running (D-264–D-266) |

Split view (2D + 3D side by side) supported. All views editable where practical; 2D is the authoritative editing surface.

### 12.2 Tools
Select, move, rotate, scale, copy/paste, duplicate, array (linear), delete, undo/redo (unbounded command stack), group, lock, hide, measure, align. Property panel for the selected object(s) with multi-edit. Object browser tree (levels → regions → nodes → connectors). Library palette of preset devices (ticket machine, 4-lane gate line, security lane, lift bank, escalator pair…).

### 12.3 Run controls
A **RUN** group in the toolbar, next to CHECK (D-024): PLAY/PAUSE, STEP, RESET and speed chips ×0.5 ×1 ×2 ×4 ×8 MAX; the status-bar clock ticks live. **As built (round 006):** PLAY checks and compiles the study first (errors block, D-106) and locks editing until RESET (D-097); the status bar shows speed, agents, emitted, arrived, mean journey, queuing, stressed, gave up and stuck (D-104, D-168); the STUDY tab's RUN section sets start time, end (fixed duration or until stopped), seed with RE-ROLL and keep side (D-103, D-105). Later: jump to time, follow-agent camera, click agent for inspector (profile, current task, wait so far, frustration history). A bottom scrubber bar can join in M8 with replay.

---

## 13. Look and feel

- **Dark base**, near-black blue-grey backgrounds, slightly desaturated UI chrome.
- **Bright primaries** for objects and focus: cyan selection, yellow nodes, magenta queues, green spawns, red sinks/alerts, orange connectors (final palette in Round 1).
- **SimX nostalgia, not pastiche:** chunky iconography, a retro display font for titles/toolbar labels, subtle scanline/dither option on the splash and toolbar, toolbar of square tool buttons with tooltip "flavour text". Clean modern panels for data.
- A small status ticker ("Peak density at Gate Line A — LoS E") in the SimCity news-ticker spirit.
- All colours as theme resources; colour-blind-safe alternative heatmap ramp.
- **No stock Windows chrome inside the app:** every control (inputs, toggles, tabs, combo boxes, scrollbars, menus) is restyled. Text inputs and data values use a monospace face (`Cascadia Mono, Consolas`); VT323 stays for headers, buttons and the ticker (D-023). Boolean toggles such as level visibility/lock are glyph toggle buttons (eye / eye-slash, padlock), cyan when on (D-022).

---

## 14. File format

### 14.1 Extension
Studies use **`.throng`**, profiles **`.throngprofile`** (D-008). Chosen over `.pedfile`: short, on-brand, and reads well aloud.

### 14.2 Container
Zip archive:

```
study.throng (zip)
├─ manifest.json        format version, app version, created/modified, seed, units
├─ study.json           levels, regions, connectors, shafts (format v2); nodes, lifts, spawns later; settings
├─ library.json         embedded copies of every template the study uses, including agent profiles (§22.6)
├─ underlays/           calibrated images (+ calibration json)
├─ results/ (optional)
│  ├─ summary.json
│  ├─ timeseries.bin
│  ├─ heatmaps.bin
│  └─ replay.bin
└─ thumbnail.png
```

- Every object has a stable GUID; references by ID.
- **v4 (round 006, D-099, D-105, D-110)** adds `emissionRules` on regions, `keepSide` in settings and `staffOnly`
  on connectors; v3 files load with no rules, keep left and public connectors.
- **v3 (round 005, D-086)** adds region spans and height modes, exclusions, per-level stairs, travelators,
  `devices`, `underlays` and their images in `underlays/`.
- **Format version** in the manifest + migration steps in `Throng.IO` so old files always open. v1 = levels and
  regions; **v2 (round 004, D-064)** adds `connectors` and `shafts` (v1 files load with both empty). Builds that
  only know v1 refuse v2 files with a "saved by a newer version" message.
- Save writes to a temp file then swaps (no corruption on crash). Autosave + recovery.
- Results can be stripped ("Save without results") to keep files small.

---

## 15. Development workflow

- **Base protocol: `/ai/PROTOCOL.md`** (D-027). Every round is **Query → Build**: queries first, as staged
  multiple-choice batches with the recommended option first and labelled; answers are logged in `DECISIONS.md`;
  then only the decided scope is built on the zipped project.
- Exchange is always the **zipped project**. Each round I return the whole project with:
  - `/ai/BRIEF.md` — this document, kept current
  - `/ai/DECISIONS.md` — numbered decision log (what, why, date)
  - `/ai/rounds/round-NNN.md` — what changed, what to test, known issues, next steps
  - Root `README.md` and `THIRD-PARTY-NOTICES.md` updated
- You build and test in Visual Studio and report back errors/feedback; I fix in the next round.
- The project must build clean with zero warnings treated as goal (not enforced initially).

---

## 16. Roadmap

| Round(s) | Milestone | Done when |
|---|---|---|
| **M0** | Skeleton + 3D viewport | Solution builds; dark-themed shell; GL viewport with **plan (ortho) and 3D orbit cameras** switchable; stacked level planes drawn at true elevation; new/open/save `.throng` round-trips; undo stack framework; World/Surface/Portal model types in Core; **File → New from Demo** opens the demo study's levels and regions (§21); template library types and built-in catalogue loader (§22) |
| **M1** | Editor | Levels, walkable/obstacle/zone sketching, snapping, select/move/copy/delete/undo, property panel, image underlay; **place stairs/escalators** (footprint, flights, direction) shown as sloped 3D geometry; **library palette** places built-in device/connector presets; demo gains its stairs and escalators |
| **M2** | Movement across levels | Surface compilation + nav grids; portal graph; flow fields; social force; spawn → sink **including via stairs/escalators**; play/pause/speed/clock; instanced agents rendered in 3D at correct heights; built-in agent profiles and schedule templates live; **demo runs end to end** |
| **M3** | Nodes & queues | Node model, choice logic, all queue modes, task lists with tags; library editor (duplicate/edit/create custom templates, `.thronglib` import/export) |
| **M4** | Lifts | Lift banks, car motion, landing queues, dispatch profiles, lift-vs-stair route choice |
| **M5** | Analytics v1 | Density/LoS heatmaps (floors and connectors), node stats, time-series graph, CSV export |
| **M6** | Views polish | Isometric presets, cutaway clip planes/section box, exploded level spacing, agent LOD, frustration colouring. **Round 015:** ISO presets, clip above a level, section box, explode and agent LOD built (D-262–D-268); frustration colouring is the STRESS colouring from round 008 |
| **M7** | Validation & profiles | Rule engine, static capacity check, profile import/export + reconcile. **Round 016:** built (D-270–D-279) |
| **M8** | Scale & replay | Performance pass toward stadium scale (§17), replay scrubbing, headless batch runs, presets library, PNG export, retro theme pass |

The demo study grows with every milestone and is the acceptance test for each round: if a round's feature isn't visible in the demo, the round isn't done.

Rationale: the 3D world and portal graph exist from M0, so vertical circulation is part of the movement core (M2), not a later bolt-on. Plan view remains the main authoring surface, but it is just one camera on the same scene.

---

## 17. Performance targets

- **v1 target:** smooth real-time (≥ 30 fps render, sim ×1) at **2,000–5,000 concurrent agents**.
- **Design-for target:** stadium filling / emptying, i.e. **tens of thousands** of agents. Not required to run in real time, but the architecture must not prevent it. Built in from M0:
  - Agents stored as **struct-of-arrays** (positions, velocities, states in contiguous arrays), not one class instance per agent.
  - Zero allocations per tick in the engine; pooled buffers.
  - Uniform-grid **spatial hash** per surface for neighbour queries.
  - Engine runs on its own thread, publishing snapshots to the renderer (double-buffered), so a slow sim never freezes the UI.
  - **Headless faster-than-real-time** runs for big studies, watched via replay afterwards.
  - Per-agent RNG streams so the update can later be parallelised (`Parallel.For` over spatial buckets) without losing determinism.
- Rendering: one instanced draw call per agent mesh; dots/billboards LOD when zoomed out.
- Flow-field recompute only when geometry changes or nodes open/close.

---

## 18. Decisions

### 18.1 Locked
| # | Decision | Date |
|---|---|---|
| D-001 | UI framework is WPF (`net10.0-windows`) | 2026-10-24 |
| D-002 | Fresh OpenGL renderer; no code shared with other personal projects | 2026-10-24 |
| D-003 | Agent target: a few thousand real-time; architecture designed for stadium-scale (tens of thousands) | 2026-10-24 |
| D-004 | 3D-first world model: surfaces + portals + portal graph from M0; connectors are surfaces; lifts are graph edges with 3D cars | 2026-10-24 |
| D-005 | Throng's own code licensed **MIT** (see §20) | 2026-10-24 |
| D-006 | GL hosting via **OpenTK + GLWpfControl** (WPF overlays over viewport) | 2026-10-24 |
| D-007 | **Clipper2** for polygon booleans/offsetting from M1, wrapped behind a `Throng.Core` geometry interface; triangulation hand-rolled | 2026-10-24 |
| D-008 | File extensions **`.throng`** / **`.throngprofile`** | 2026-10-24 |
| D-009 | Image underlays (two-point calibration) in M1; DXF tracing parked | 2026-10-24 |
| D-010 | Balking/reneging in M3, driven by per-profile patience | 2026-10-24 |
| D-011 | Metric units only | 2026-10-24 |
| D-012 | Fruin LoS letters by default, abstract Low→Critical labels as a toggle | 2026-10-24 |
| D-013 | Turnstiles/security/gates are configured pass-through nodes with library presets, no separate class | 2026-10-24 |
| D-014 | Bundle one SIL OFL retro display font for titles/toolbar only; body text uses system fonts | 2026-10-24 |
| D-015 | Built-in demo study generated in code, available from M0 and extended each milestone; doubles as the round acceptance test | 2026-10-24 |
| D-016 | Template library: read-only built-ins + editable user library; studies embed copies of templates they use; `.thronglib` for library exchange | 2026-10-24 |
| D-017 | Product renamed **Throng** (from WalkAbout, to avoid borrowing a culturally loaded term); extensions `.throng` / `.throngprofile` / `.thronglib` | 2026-10-24 |
| D-018 | Retro font is VT323 (SIL OFL), Consolas fallback | 2026-10-24 |
| D-019 | Built-in template catalogue defined in C# (compile-checked) rather than embedded JSON | 2026-10-24 |
| D-020 | Agent profiles live in `library.json` with other templates; no separate `profiles.json` | 2026-10-24 |
| D-021 | Library browser is a tab strip by category plus a search box, replacing expanders | 2026-10-24 |
| D-022 | Level visibility and lock use eye / padlock glyph toggles, not checkboxes | 2026-10-24 |
| D-023 | Text inputs and data values use `Cascadia Mono, Consolas`; VT323 for headers/buttons/ticker only | 2026-10-24 |
| D-024 | Run controls are a toolbar RUN group next to CHECK (play/pause, step, reset, speed chips) | 2026-10-24 |
| D-025 | User template library lives in `%AppData%\Throng\Library` | 2026-10-24 |
| D-026 | M1 editing is plan view only; 3D stays view-only until M6 | 2026-10-24 |

### 18.2 Open
None. New questions get added here as they come up.

---

## 19. Dependencies and acknowledgements (seed for THIRD-PARTY-NOTICES)

**Dependencies** (versions and transitive packages confirmed by the M0 licence audit)
- OpenTK and OpenTK.GLWpfControl (MIT) — GL bindings and WPF hosting
- CommunityToolkit.Mvvm (MIT) — MVVM source generators
- ScottPlot (MIT) — charts
- Clipper2, Angus Johnson (Boost SL 1.0) — polygon clipping and offsetting
- Retro display font, to be chosen in M0 (SIL OFL 1.1)

**Acknowledgements** (inspiration only, no code or assets used)
- Fruin, J. J. — *Pedestrian Planning and Design* (1971) — Level of Service concept and thresholds
- Helbing, D. & Molnár, P. — "Social force model for pedestrian dynamics" (1995)
- Weidmann, U. — *Transporttechnik der Fussgänger* (1993) — walking speed distributions
- Treuille, Cooper, Popović — "Continuum Crowds" (2006) — flow-field navigation inspiration
- Barney, G. — *Elevator Traffic Handbook* — collective control and up-peak concepts
- Maxis' SimCity / SimCopter / SimTower era — visual and UX inspiration only, no assets used

---

## 20. Licensing

### 20.1 Outbound
- Throng's own code is **MIT** (D-005). `LICENSE` at the repo root carries the copyright line (`Copyright (c) 2026 Gavin Nicholls`); anyone reusing the code must keep that notice.

### 20.2 Inbound allow-list
| Licence | Allowed | Obligation when distributing |
|---|---|---|
| MIT / MIT-0 / BSD-2/3 | ✅ | Keep copyright + licence text (MIT-0: none) |
| Apache-2.0 | ✅ | Keep licence + any NOTICE file; note modifications |
| Boost SL 1.0 | ✅ | Licence text in source copies; nothing required for binaries |
| zlib | ✅ | Keep notice in source copies; nothing required for binaries |
| SIL OFL 1.1 (fonts only) | ✅ | Ship licence with the font; don't sell the font on its own; modified versions can't use reserved font names |
| GPL / LGPL / MPL / any copyleft, non-commercial or source-available | ❌ | Not used |

### 20.3 Rules
1. Every shipped dependency, **including transitive packages and native binaries** (e.g. anything OpenTK pulls in), is listed in `THIRD-PARTY-NOTICES.md` with name, version, licence, URL and full licence text.
2. `THIRD-PARTY-NOTICES.md` ships next to `Throng.exe` in every build output and release, alongside the font's OFL text.
3. A licence audit (`dotnet list package --include-transitive`) runs in any round that adds or bumps a package, and is recorded in that round's notes.
4. Papers, algorithms and design inspirations go in an "Acknowledgements" section of the same file. They carry no legal obligation; it's courtesy.
5. Test-only packages (MSTest) never ship and need no notice, but are listed for completeness.

---

## 21. Demo study

A ready-made study available from **File → New from Demo** (and the start screen), so every feature can be tested without hand-modelling first. Hand-rolled studies remain the normal path.

### 21.1 "Demo: Interchange Station"
A compact two-to-three-level transit station that exercises every system:

| Level | Content |
|---|---|
| **Street (0.0 m)** | Two street entrances (spawn/sink zones), taxi/bus stop (scheduled spawn), retail dwell zone, columns and kiosk obstacles |
| **Concourse (−5.0 m)** | Ticket machines (×4, auto-crowd), staffed ticket counter (×2, shared serpentine queue), gate line (5 lanes + 1 wide accessible gate), info desk, seating wait zone, measurement zone across the gate line |
| **Platform (−11.0 m)** | Island platform, two train-door spawn/sink zones on 6-minute alternating schedules (unload then board), platform-edge obstacle strip |
| **Vertical** | Street ↔ concourse: 3.6 m stair + escalator pair. Concourse ↔ platform: tight 1.5 m switchback stair, escalator pair (AM/PM reversal arrives with schedules), and the shaft of a 2-stop accessible lift (car in M4). Openings are cut automatically (D-061) |

- **Agent mix:** Commuter, Infrequent traveller, Luggage traveller, Mobility-impaired (lift-only), Staff.
- **Default study:** 07:30–08:30 AM peak, seed fixed, warm-up 5 min.
- **Deliberate pressure points** so results are interesting: the gate line is slightly under capacity for the train arrival batch, and the platform stair is narrower than it should be.

### 21.2 How it's built
- Generated **in code** by a `DemoStudyBuilder` (in `Throng.IO`), not stored as a binary asset, so it always matches the current file format and model types.
- Uses only built-in library templates (§22), so the demo also proves the library works.
- Can be saved as a normal `.throng` and edited like any other study.
- The same builder feeds headless **engine tests**: the demo loads, validates clean (apart from its intentional warnings) and runs N minutes deterministically.

### 21.3 Later demos (not committed)
- ⏳ Office lobby (lift up-peak, security gates)
- ⏳ Stadium concourse segment (event ingress/egress ramps, raked tier once sloped surfaces land)

---

## 22. Template library

Everything an architect places or configures can start from a **template**: pick one, drop it in, optionally tweak. Values are **indicative defaults** for conceptual studies, editable everywhere, and documented in the library browser.

### 22.1 Device (node) presets
| Preset | Key defaults |
|---|---|
| Ticket machine | 1 server, service ~20–60 s, auto-crowd queue, turn-around |
| Staffed ticket counter | 1 server per window, service ~45–120 s, shared linear queue |
| Ticket gate line | 4 lanes, ~2 s pass-through, auto-crowd |
| Accessible wide gate | 1 lane, ~4 s, accessible (D-213): wheelchair users use it, luggage prefers it, others avoid it |
| Security lane | 1 lane, ~20–40 s, linear queue, pass-through |
| Information desk | 1 server, ~30–90 s, auto-crowd |
| Retail / dwell zone | Zone, dwell 0–180 s, optional visit probability |
| Seating / waiting zone | Zone, dwell until a trigger (e.g. train arrival) |
| Toilet block | Capacity-limited zone, dwell ~60–180 s |
| Entrance / exit | Spawn+sink zone, unlimited absorption |
| Train / vehicle door zone | Spawn+sink driven by a vehicle schedule |
| Boarding gate | Batch-release queue node |

### 22.2 Vertical circulation presets
| Preset | Key defaults |
|---|---|
| Stair 1.5 m / 2.4 m / 3.6 m wide | Bidirectional, stair factors per §7.1, width-based flow limit |
| Escalator up / down | 1.0 m step width, ~0.5 m/s, fixed throughput, walking fraction per profile |
| Escalator pair | Up + down linked, reversible by schedule |
| Travelator | Horizontal, ~0.65 m/s, one-way |
| Ramp 1:14 | Sloped surface connector, speed factor applied |
| Passenger lift 13p / 21p | Capacity 13 / 21, ~1.6 m/s, door + dwell times, collective dispatch |
| Accessible / goods lift | Larger car, slower doors, priority for mobility-impaired |
| Shuttle lift (2-stop) | Shuttle dispatch |

### 22.3 Agent profile presets
| Profile | Character |
|---|---|
| Commuter | Fast, decisive, low patience, walks on escalators, avoids lifts |
| Infrequent traveller / tourist | Slower, less decisive, often needs ticket + info desk |
| Luggage traveller | Larger radius, slower, prefers escalator/lift over stair |
| Mobility-impaired (wheelchair) | Large radius, lift and ramps only; needs accessible devices (only accessible ones where a level has one, D-213) |
| Elderly | Slower, higher stair penalty, more patient |
| Family / group | Slower, larger radius (true group movement ⏳ later) |
| Staff | Pinned routes, bypasses public queues |
| Event fan | Arrives in surges, high density tolerance, low patience at exits |

### 22.4 Schedule templates
| Template | Use |
|---|---|
| Always open / always on | Default availability |
| Business hours | Open 07:00–19:00 |
| AM peak / PM peak profile | Emission rate curve rising and falling over ~2 h |
| Constant rate / Poisson random | Steady background demand |
| Train every N min | Arrival → unload batch → board window → depart, alternating directions optional |
| Bus every N min | Smaller batches, single door |
| Event ingress ramp | Gates open T−90 min, demand builds to peak near start time |
| Event egress dump | Most of the crowd released within a few minutes |
| Escalator AM up / PM down | Direction reversal at a set time |
| Lift up-peak AM / collective off-peak | Dispatch profile switch by time |
| Closure window | Node or connector unavailable for a period (maintenance, incident testing) |

### 22.5 Custom templates
- Built-in templates are **read-only**. **Duplicate** one to make a custom version, or create from scratch.
- Any placed object can be **saved back as a template** ("Save as preset").
- A **Library browser** panel uses a **tab strip** (AGENTS / DEVICES / VERTICAL / LIFTS / SCHEDULES) over a single filtered list with a **search box** (D-021), showing preview swatch, summary, description and source (built-in / user / imported). No expanders.
- The user library lives in `%AppData%\Throng\Library\`. Libraries exchange as **`.thronglib`** files (zip of JSON, same container style as `.throng`); `.throngprofile` remains for single agent profiles.
- Importing a library runs the same **reconcile** flow as §8.6 when it references types the study or library doesn't have.

### 22.6 Templates and studies
- Placing a template **copies** its values into the object and records `SourceTemplateId` + version. The study embeds a copy of every template it uses in `library.json`, so a `.throng` opens identically on another machine without the user's library.
- Per-object actions: **Reset to template**, **Update from library** (when the library version is newer), **Detach** (stop tracking the source).
- Built-in catalogue is defined in code (`Throng.Core.Library.BuiltInLibrary`) with deterministic IDs derived from each template's key, versioned with the app; new app versions can add templates without touching existing studies. It serialises to the same JSON as any library.
