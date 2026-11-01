# Round 024a — Group movement: model, format v12, simulation

**Status:** built, **unbuilt in the sandbox** (no .NET SDK; code reviewed by reading only). 390 test methods (+15).
**Decisions:** D-378–D-392 (logged before building; D-393 = judgement calls). Part **a** of two (D-392): 024b = LIBRARY ▸ GROUPS editor, MINE,
`.thronglib`, results rows, hover tethers. No further split.

**024a fix-up (after Gavin's build):** compiled clean; two tests failed, both on test expectations:
`Round009FileTests.Demo_UsesJourneysAndTagsPerD155` still expected commuters at 70 (now 64 + 6 Family groups), and
`Round024Tests.Groups_UseOneMachine_AndWaitPastIt` ran 180 s, shorter than a family of 4–5 needs at one machine
(20–60 s each, waiting for the last member), so 0 had arrived; now 400 s, and it also checks nobody is removed as stuck.

## Decisions taken (summary)

| D | What |
|---|---|
| D-378 | Round 024 = group movement (stadium pass waits) |
| D-379–D-381 | Stadium round decided ahead: 30k headless / 5k live at ≥ 30 fps; PERF overlay + `[Explicit]` bench; concourse-sector demo |
| D-382 | Group templates: member rows (profile + min–max), 2–8 people |
| D-383 | Cohesion term in the social force; slowest member's pace |
| D-384 | Queue together, regroup ~2 m past; lifts / trains all or none; balk / give up as one |
| D-385 | The one-wide-agent "Family / group" profile is removed; its mix lines become Family group lines on load |
| D-386 | A mix line's "who" is a profile or a group; rates and shares count people |
| D-387 | Hard needs of any member apply to all; soft preferences from the lead (first row) |
| D-388 | Groups live like profiles (built-in, study, MINE, .thronglib) — **024b** |
| D-389 | Group default journey; every journey decision once per group; grants per member as served |
| D-390 | Group × journey result rows with spread — **024b** |
| D-391 | COLOUR ▸ GROUP (024a) + hover tethers (024b); replay records the group |
| D-392 | Format v12; 024a / 024b split |

## What changed

**Core**
- `Library/GroupTemplate.cs` (new): `GroupTemplate : TemplateBase` (`Members` rows of `GroupMember(ProfileId, Min, Max)`,
  `DefaultJourneyId`, `MinSize` / `MaxSize`, `Roll` (per-row uniform counts, cut to 8 from the last rows, topped up to 2),
  `MeanSize`).
- `TemplateLibrary.Groups` (+ `All`, `Count`, `Add`). `MixLine.GroupId` (+ `IsGroup`, `MixLine.OfGroup`).
- `StudyTemplates.FindGroup`, `AvailableGroups`, `LineProfiles`; `JourneyIdFor` gives a group line's "(profile default)"
  the group's journey.
- Built-ins: profile **Child** (`agent.child`) added; **Family / group** (`agent.family`) removed; six groups in a new
  "Groups" category: Couple, **Family** (1–2 adults + 1–3 children), Friends (3–5), Wheelchair user + companion, School
  party (1–2 adults + 4–6 children), Fans (2–6). Still 8 profiles; 57 templates.
- `MyLibrary.Embed`: embedding a group brings its member profiles.
- `StudyDefinitions.ProfileUseCount` counts group members (not a group line's leftover profile id).
- Validation: group lines warn for a missing group / no members (single Commuters instead), missing member profiles,
  and minimums over 8; the accessible-device warning looks at every member.

**IO (format v12)**
- `ThrongFile.CurrentFormatVersion = 12`; `FormatMigrations.UpgradeV11ToV12`: mix lines of `agent.family` → the built-in
  Family group (share, journey, tags kept); the study's copy of the old profile is dropped once unused. Null-guards for
  `Groups` on load and in the user library store.
- Demos: Interchange inbound mix 64 commuters / 18 + 10 infrequent / 2 wheelchair / **6 Family groups** (half with
  tickets); Office tower 94 / 3 wheelchair / **3 Family groups**. Both embed Child + Family.

**Sim**
- Compile (`SimSetup`): group lines carry member rows (profile index, min, max), the group name, mean size and a
  **route profile**: a clone of the lead's profile with lift-only / accessible-only if any member has them (D-387).
  `SimModel.RouteProfiles` = `Profiles` then one per group; reachability and step checks use it.
- `SimMixLine`: `RouteProfile`, `Members`, `GroupName`, `MeanSize`, `PickWeight` (share for singles; share ÷ mean size for
  groups), `HasProfile`. `SimEmitter.TotalPickWeight`.
- `AgentStore`: `Route` (choice profile), `Group` (slot or −1), `LastDevice`.
- `Engine/SimEngine.Groups.cs` (new): group slot table (alive count, per-node decisions, hue), per-step member index
  (`IndexGroups` at the start of each step and after spawning), group spawning (all or none, ring 0.5–1.6 m round the
  first member, slowest pace, lead's escalator habit, one tag roll; a roll that doesn't fit is held for the next try),
  shared decisions (`Recall` / `Remember` per journey node and exit key), `FollowGuide` (members copy the route of the
  earliest-serial mate on the same surface heading for the same target), `GroupPull` (centre pull past (N − 1) / 2 m,
  ease off when > 1 m ahead, 1.25× catch-up when > 1.5 m behind; mates within 8 m), regrouping past devices, group balk /
  give-up, lift and train readiness.
- Behaviour lookups (patience, sight, choices, lift priority, stress tolerance) go through `RouteOf(i)`; body, colour,
  stair speed factors and results rows stay on the person's own profile.
- `GoToNode` / `BeginExit`: Chance ports, train doors, devices, zones and sinks recalled per group. Without groups no
  extra random draws happen (seeded runs of studies without groups are unchanged).
- Devices: a lane a mate is lined up in counts one shorter; a member past a device (after walking through, or after
  stepping aside from a machine) waits in the new **`AgentPhase.Regroup`** about 1 m on while a mate still has the
  device to get through (cap 120 s); a balk moves the mates still walking / waiting / queueing for it; nobody balks
  once a mate is being served; a give-up takes the mates heading for or waiting at that device on the same step.
- Lifts: `FindBoarder` boards a group member only when every mate coming to that landing is waiting there and they all
  fit (a group bigger than the car boards one by one); mates of someone already in the car board first; destination
  dispatch lets mates of a person given a car use it. Trains: a member boards only when every mate leaving by that
  service is waiting or within 6 m of their door and the train has room for all of them.
- Snapshot: `FloatsPerAgent` 11 → **12** (float 11 = group hue 1–255, 0 alone). Replay: `PackedAgent.GroupHue` (23 B).

**Render / App**
- `GpuAgents.FloatsPerInstance` 12 (stride only). `AgentColoring.Group` + `GroupColours.Of(hue)`.
- Transport bar COLOUR ▸ **GROUP**: the app writes each group's hue (singles grey) into the copy it uploads, live and in
  replay. PNG export legend "GROUPS".
- Mix line "who" drop-down lists profiles then groups ("Family (group)"); picking a group embeds it with its members;
  "(profile default)" uses the group's journey. The journey overlay's profile filter lists group members' profiles.

**Tests (+15):** `Throng.Core.Tests/Round024Tests` (6), `Throng.IO.Tests/Round024FileTests` (3),
`Throng.Sim.Tests/Round024Tests` (6). Updated: built-in counts (57, 6 groups), demo embeds (6 profiles, 1 group),
format v12, `FloatsPerAgent` 12, 009c stair-effort list without `agent.family`.

## How to test (numbered checklist)

1. Build the solution; run all tests (390). Fix-first list for the next round if anything fails.
2. File ▸ New from demo (Interchange). CHECK: 0 errors, 0 warnings.
3. Select the West entrance ▸ rule 1: the mix shows **Family (group)** at 6 with ticket 50%. Open the "who" drop-down:
   profiles first, then the six groups.
4. PLAY. COLOUR ▸ **GROUP**: small clusters of the same colour walk together from each entrance; everyone else grey.
5. Watch a family at the ticket machines: they line up at the same machine, the first ones served wait just past it,
   then they go on together to the same gate lane and wait past the gates for the rest.
6. Watch a family cross the concourse: they keep within a few metres and walk at the children's pace; the leader eases
   off rather than walking away.
7. A family that takes a lift: they board the same car together (none left on the landing as the doors close).
8. Drag the transport-bar slider back: group colours are kept in replay.
9. EXPORT PNG with COLOUR ▸ GROUP: legend says GROUPS.
10. New from demo ▸ Office tower: PLAY; Family groups ride the lifts together.
11. In a study's mix line pick **Wheelchair user + companion (group)**: CHECK passes; in PLAY the companions take the
    lift and the accessible gate with the wheelchair user.
12. Open an older study that used the Family / group profile (if you have one): the line now reads Family (group).
13. RESULTS ▸ JOURNEYS: members appear under their own profiles (Infrequent traveller, Child); no group rows yet (024b).
14. Batch (STUDY ▸ RUN ▸ BATCH…) on the demo: runs complete; REPLAY a run and scrub with COLOUR ▸ GROUP.
15. Don't-regress (HANDOVER §8): save → reopen round-trips, undo every edit, editing locks while running.

## Judgement calls

- **Who decides**: whichever member reaches a decision first decides for the group (kept per journey node), rather
  than a fixed lead walking ahead; the lead (first row) only supplies the soft preferences (D-387). Same outcome as
  "decided once", without making others wait for the lead.
- **Route following**: members copy the route of the mate with the earliest serial on the same surface going to the same
  target, every step (no own route review while following).
- **Spawn**: group lines are picked by share ÷ mean size so shares stay shares of people (D-386); the rule's pending count
  may dip below zero after a group and later arrivals pay it back. A group that doesn't fit keeps its roll and its turn.
- **Pace**: every member gets the slowest sampled speed; stair / ramp speed factors stay per person (so children slow
  the group on stairs too, and the pull keeps them together). Escalator walk/stand rolled once (lead's profile).
- **Tags**: rolled once per group at spawn; grants still per member as each is served.
- **Regroup**: about 1 m on from where the member finishes (≈ 2 m past the service point), 120 s cap; waits only for mates
  still heading for, queueing at or being served at that same device.
- **Balking**: no balk once a mate is being served at the device (the group has committed).
- **Lifts**: a group bigger than a car's places boards one by one rather than never.
- **Demos**: Family shares 6 % (Interchange inbound) and 3 % (Office tower), taken from commuters.
- **Child** profile added (needed for families): r 0.20 m, 1.25 m, 1.1 m/s, patience 240 s.
- **Snapshot / replay**: one more float per agent and one more byte per replay person (23 B).

## Accepted defaults

None beyond the decisions (all were answered).

## Known limitations

- Unbuilt in the sandbox: tests and behaviour unverified until Gavin builds.
- Destination dispatch can still give mates different cars when they call separately; mates may use the car given to
  any of them, but the dispatcher doesn't plan for groups.
- Trains: a stranger can take the last places between two mates boarding (rare, near capacity).
- Lift landing patience (D-253) is still per person; a group's members time out close together (same lead patience),
  but not as one.
- Group members on different surfaces (one on a stair, others still on the floor) don't pull on each other across the
  height band.
- A journey that visits the same device at two nodes could make a member wait (up to 120 s) for a mate who is at that
  device for the other node.
- No group results, editor or tethers yet (024b).

## Watch list

- Seeded demo numbers move (6 % of inbound people now arrive as families): any test or note relying on exact demo
  counts may need a new expectation.
- Crowded spawn zones with big groups (School party): the held roll waits until 2–8 people fit together.
- `FollowGuide` runs every step for every walking member: cheap (group size), but watch it in the stadium pass.

## Open questions (for 024b's Query phase)

- GROUPS editor layout: rows as profile drop-down + min / max spinners, like the mix lines? (recommend yes)
- Should a group template carry its own colour for COLOUR ▸ PROFILE (lead's colour now)?

## Next

024b (no further split): LIBRARY ▸ GROUPS tab and SELECTION editor (built-ins as overrides with RESET, DUPLICATE, NEW,
DELETE when unused), SAVE TO MY LIBRARY (brings member profiles), `.thronglib` export / import of groups, RESULTS ▸
JOURNEYS group rows (group time until the last member leaves, mean / 95th, size, people, spread) in journeys.csv and
batch, hover / selection tethers in plan and 3D.
