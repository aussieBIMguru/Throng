# Round 024 — Group movement (024a: model, format v12, simulation · 024b: editor, library, results, tethers)

**024b status:** built on r024a-fix, **unbuilt in the sandbox** (no .NET SDK; reviewed by reading only). 400 test methods
(+10). Decisions D-394–D-397. Section **024b as built** is at the end, with its own checklist.

**024a status:** built and **confirmed by Gavin** (r024a-fix: compiled clean, 390 tests pass, checklist OK).
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

---

# 024b as built

**Decisions:** D-394 (member rows like mix lines), D-395 (no group colour), D-396 (round 025 = stadium pass + PDF
report in one round), D-397 (judgement calls). Format stays **v12** (groups were already in it); `.thronglib` stays
version 1 (groups are an extra list older builds skip).

## What changed

**Core**
- `StudyDefinitions.CreateGroupCopy` (DUPLICATE), `CreateNewGroup` (NEW: two commuters), `GroupUseCount` (mix lines that
  spawn it), `GroupsWithMember` (the study's groups that list a profile).
- `MyLibrary` holds groups (D-388): `Holds`, `Put`, `Remove`, `KindName` ("Group"), `Rekey` ("group.mine.…").
  `PlanSave` of a group brings its member profiles (unless unchanged built-ins), their default journeys and the
  group's journey; `BuildExport` takes a ticked group's members and journey; `FindImportClashes` / `ApplyImport` list
  and import groups last, and a kept-both group follows member profiles and a journey that came in as copies;
  `EmbedReferenced` embeds a group line's group and its members, and members / journeys of groups the study holds;
  `Embed` of a group from your library brings its journey too.
- `GroupTemplate.Summarise(profileName)`: LIBRARY names a study's own member profiles.
- IO: `UserLibraryStore.Normalise` null-guards group rows (used by `.thronglib` loads too).

**Sim**
- `Engine/GroupJourneyResult.cs`, `Engine/SimEngine.GroupResults.cs` (D-390): one row per group name × journey from the
  emitters' group lines. A group spawned after the warm-up is timed from spawn until its last member leaves
  (`NoteGroupLeave` before `LeaveGroup` in the removal loop); stuck if any member was removed as stuck, gave up / long wait
  if any member did; within acceptable against the lead's time; spread = steps where any two members are more than
  5 m apart (3D, `MeasureGroupSpread` after the step's second `IndexGroups`). No random draws.
- `RunResults.Groups`; batch: `Batch/GroupJourneyBatchResult.cs`, `BatchAverager.Groups`, `BatchResults.Groups`, and
  the mean results carry them.
- Group tags (D-397): `SimEngine.GroupTag(i)` (1–65535, numbered from the run's start, mates share it) replaces the
  stored hue; snapshot float 11 carries it; `PackedAgent.GroupTag` (ushort, replay 24 B per person).

**Analysis**
- journeys.csv (single run and batch summary): group rows after the profile rows, named "Family (group)", counts in
  groups; four trailing columns `GroupSize, People, SpreadS, SpreadPct` (batch: mean / min / max each), blank on profile
  rows.

**Render / App**
- `GroupColours.HueOfTag` / `OfTag`: COLOUR ▸ GROUP turns the tag into the hue (golden-ratio steps, as before).
- LIBRARY ▸ **GROUPS** tab (after AGENTS): the study's groups, then yours (MINE), then built-ins; NEW; rows open
  SELECTION ▸ **GROUP** (`ViewModels/GroupDefinitionViewModel.cs`): DUPLICATE, RESET TO BUILT-IN, DELETE (own, unused),
  SAVE TO MY LIBRARY, UPDATE FROM MY LIBRARY; name, description, member rows (profile drop-down, min – max, ▲, ×, LEAD
  tag on the first), ADD MEMBER, size note, default journey, usage. Every edit is one undo step through
  `MainViewModel.EditGroupDefinition` (built-ins as overrides under their id, D-287 / D-388).
- MY LIBRARY EXPORT… lists groups; SAVE TO MY LIBRARY says what goes with an item.
- RESULTS ▸ JOURNEYS: group rows last, with SIZE, PEOPLE and SPREAD % columns (blank on profile rows), live and batch.
- Tethers (`Views/TetherLayer.cs`, `Views/ViewportView.Tethers.cs`): hovering a person in a group draws thin yellow lines
  to their mates with a "GROUP OF n" tag; a left click on one keeps that group tethered in cyan (following the person)
  until a click elsewhere. Plan, 3D and ISO, live and replay.
- Deleting a profile that a group in the study lists is refused with the group names.

**Tests (+10):** `Throng.Core.Tests/Round024bTests` (5), `Throng.IO.Tests/Round024bFileTests` (1),
`Throng.Sim.Tests/Round024bTests` (4). Updated: `Round012Tests.Csv_ZonesJourneysAndZoneColumns` (four group columns).

## How to test (024b)

1. Build; run all tests (400).
2. New from demo (Interchange). LIBRARY ▸ **GROUPS**: six built-ins, summaries name their members.
3. Click **Family**: SELECTION ▸ GROUP shows two rows (adult 1–2 LEAD, child 1–3), "2–5 people (about 3.5 on average)",
   its journey, "Used by … mix lines". Change the child row's max to 2: the row note in LIBRARY says MODIFIED; RESET TO
   BUILT-IN puts it back; Ctrl+Z / Ctrl+Y step through both.
4. DUPLICATE Family → "Family (copy)"; ADD MEMBER, pick Elderly 1–1, ▲ it to the top (LEAD moves). Pick the copy as the
   "who" of a spawn zone's mix line. DELETE on the copy is refused while that line uses it; after changing the line
   back, DELETE works (and undoes).
5. LIBRARY ▸ GROUPS ▸ NEW: "New group" with two commuters. Try to DELETE the Elderly profile while the copied group lists
   it: refused, naming the group.
6. SAVE TO MY LIBRARY on the copied group (after duplicating a profile into it): the ticker says the profile went with
   it. New study: the group shows as MINE in GROUPS; use it on a mix line, save, reopen on a machine (or folder) without
   your library: it still runs (embedded).
7. MY LIBRARY ▸ EXPORT…: tick the group only → the .thronglib also carries its own member profile. IMPORT it back:
   keep both → "(imported)" copies, and the imported group uses the imported profile.
8. PLAY the demo to past the warm-up. RESULTS ▸ JOURNEYS: **Family (group)** rows at the bottom with DONE (groups), MEAN
   / 95 % S, SIZE (≈ 3.5), PEOPLE, SPREAD %; member rows stay under Infrequent traveller and Child.
9. Hover a family member (plan): yellow lines to the rest with "GROUP OF n". Click one: the group stays tethered in
   cyan as they walk; click empty floor: gone. Same in 3D; scrub the replay: tethers follow there too.
10. EXPORT CSV: journeys.csv ends with the group rows and the four group columns.
11. BATCH… (3 runs): group rows show mean (min–max); the summary journeys.csv has them with mean / min / max columns.
12. COLOUR ▸ GROUP: still one colour per group (now from the tag); PNG legend unchanged.
13. Don't-regress: a study without groups gives the same numbers as before for the same seed; save → reopen; undo.

## Judgement calls (D-397)

- Group time ends with the **last** member out; a group with anyone removed as stuck counts as stuck (no time).
- Gave up and long wait are "any member"; acceptable time is the lead's (first row).
- Spread uses 3D distance, so mates on different levels (one riding a lift car ahead, say) count as apart.
- Group rows are keyed by group **name** and journey (two templates with the same name would share a row).
- The group tag replaced the stored hue, so the hue now comes from the tag in the app (same look); the snapshot stays
  12 floats; replay grows 1 byte per person (24 B).
- Tethers: hover plus click-to-keep, rather than a separate person selection; in plan only people on the active level are
  in the buffer, so mates on other levels show in 3D.
- Double-clicking a profile or group in LIBRARY opens it (it used to say "later milestones").
- Deleting a profile used by a group in the study is refused (it would leave a row nobody can fill).

## Known limitations (024b)

- Unbuilt in the sandbox: tests and behaviour unverified until Gavin builds.
- Tags wrap after 65,535 groups in one run (two groups far apart in time could share a tag; tethers only look at people
  in view).
- Group rows have no per-sink split and no ALL row (one row per group × journey).
- The journey overlay (round 019) still counts people, not groups.
- Tethers don't show while the GL viewport is down (the offline panel).

## Next: round 025 (D-396)

Stadium pass **and** the one-page PDF report, one round, no split. Already decided: D-379 (30,000 people headless faster
than real time, 5,000 live at ≥ 30 fps at ×1), D-380 (PERF overlay with step ms by phase + render ms; an `[Explicit]`
bench on the stadium demo), D-381 (stadium demo = one concourse sector, copied to scale). Queries to ask first: the
PDF's contents and layout (batch summary, a replay still, heatmap, CHECK findings?), how the PDF is made (a permissive
library such as PDFsharp (MIT) vs. WPF's own XPS/print path), the bench's pass marks, and sector count / seat numbers for
the demo.

