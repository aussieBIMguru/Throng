# Round 020: the cross-study library

**Status:** built 2026-10-31, **unbuilt in the sandbox** (no .NET SDK, no NuGet; statically reviewed: brace / XAML
checks, CRLF, every touched file read). 019 confirmed by Gavin (compiles, all 346 tests pass). Decisions D-340–D-351
(query phase, same day). **362 test methods** (+16). **Format stays v11** (no study format change; your library is a
`TemplateLibrary` JSON, format 1).

## Decided (D-340–D-351)

| # | What |
|---|---|
| D-340 | Round 020 = the cross-study library, one build; docs + zip at the end in one step. |
| D-341 | Contents: agent profiles, device definitions, journeys. |
| D-342 | SAVE TO MY LIBRARY on the definition in SELECTION copies the study's version in. |
| D-343 | LIBRARY tabs list the study's own, then MINE, then built-ins; search covers all. |
| D-344 | Studies keep their copy; DIFFERS FROM MY LIBRARY + UPDATE FROM MY LIBRARY (one undo step). |
| D-345 | Change by re-saving (asks before replacing); RENAME / DELETE from LIBRARY's right-click menu. |
| D-346 | Embedded on first use under the library id (as built-ins). |
| D-347 | EXPORT .THRONGLIB with a tick list of MINE items; IMPORT into the library via the reconcile dialog. |
| D-348 | Confirm replace / delete; previous library.json kept as library.bak.json; no library undo. |
| D-349 | Journeys keep picks; where picks match nothing in a study the node uses its type / tag (save warns, CHECK notes). |
| D-350 | Saving a profile also saves its default journey; journeys save alone. |
| D-351 | Differs = same id, different values (content compare). |

## What changed

**Core**
- `Library/MyLibrary.cs` (new): your library's rules, no IO. `Find`, `Differs` (D-351, reuses `StudyDefinitions.SameValues`),
  `EmbedForUse` (D-346: yours, else the built-in, same id, marked Embedded; a profile from your library brings its default
  journey), `EmbedReferenced` (safety net: mix line profiles / journeys, profiles' default journeys, placed devices'
  definitions that are in your library but not the study), `PlanSave` / `ApplySave` (D-342, D-345, D-349, D-350),
  `Put` / `Remove` / `Rename`, `BuildExport`, `FindImportClashes` / `ApplyImport` (D-347).
- `LibrarySavePlan`, `LibraryImportResult` (new records). `ReconcileKind.LibraryItem`, `ReconcileAction.Replace`.
- Lookups take an optional `TemplateLibrary? mine` (study → MINE → built-in): `StudyTemplates.{FindProfile, FindJourney,
  AvailableProfiles, AvailableJourneys, CanEditJourney}`, `StudyDefinitions.{FindDevice, AvailableDevices}`; shared
  `StudyTemplates.Layered` (one row per id, nearest copy wins). Sim, CHECK and IO never pass it, so they behave as before.
- D-349: `JourneyTargets.{UsesPicks, PicksFallBack}`, `JourneyZones.PickedZones`; `JourneyTargets.Devices` and
  `JourneyZones.Matching` fall back to type / tag when no pick is in the study. CHECK: new rule id
  **`journey-picks-fallback`** (Info, "its picks aren't in this study, so it uses its device type / zone tag instead").
- `SimSetup.BuildDevices` builds by type when picks fall back.

**IO**
- `UserLibraryStore.cs` (new): `library.json` in a folder given to the constructor (app: `%AppData%\Throng\Library`).
  `Load` (missing = empty; unreadable = moved aside as `library.unreadable-<stamp>.json`, empty library; newer format =
  left alone, read-only for the session), `Save` (copies the old file to `library.bak.json`, temp file then swap),
  `Update(change)` (edits a copy, swaps it in only when it saves), `Normalise`. Never throws.
- `LibraryFile.cs` (new): `.thronglib` = zip of `manifest.json` (format `throng-library`, version 1) + `library.json`.

**App**
- `ViewModels/MainViewModel.MyLibrary.cs` (new): loads your library at start-up, `Mine`, `SaveToMyLibrary`,
  `UpdateFromMyLibrary` (through `EditDeviceDefinition` / `EditProfileDefinition` / `EditJourney`: one undo step, placed
  devices follow), `RenameInMyLibrary`, `DeleteFromMyLibrary`, EXPORT / IMPORT commands, `LibraryNote`,
  `MineStatusText`, `EmbedForUse`, `EmbedReferencedFromMine` (called in `OnModelChanged`, before save and before a run).
- LIBRARY: MINE rows after the study's own (D-343), notes MINE / DIFFERS (then MODIFIED / IMPORTED / YOURS as before);
  right-click on a MINE / DIFFERS row: Rename in my library…, Delete from my library…; a MY LIBRARY block with EXPORT… /
  IMPORT… under AGENTS, DEVICES and JOURNEYS.
- SELECTION (device, profile, journey panels): SAVE TO MY LIBRARY, UPDATE FROM MY LIBRARY (when it differs), a yellow
  status line (MINE / DIFFERS), and "From your library" source text for items the study doesn't hold yet.
- Mix line profile / journey choices, profile default journey choices and PLACE presets include your items; picking or
  placing one embeds it (D-346). DELETE on a definition panel only for the study's own (not your library's).
- Dialogs: `Views/TextPromptDialog` (new, RENAME), `LevelPickerDialog` takes an OK label (reused as the EXPORT tick
  list), `ReconcileDialog` library mode (Keep both / Skip it / Replace yours), `IDialogService.{PickItems, AskText,
  PickLibrarySavePath, PickLibraryOpenPath, ReconcileLibrary}`.

**Tests** (+16): Core `Round020Tests` (9: lookup order and shadowing, embed on first use with the default journey,
safety net, differs / update as one undo step with undo, save plans, picks flagged, rename / delete, import keep both /
skip / replace with a profile following its journey copy, D-349 fallback); IO `Round020FileTests` (5: round trip with
backup, unreadable file kept aside, newer format read-only, .thronglib round trip + import, studies and junk refused);
Sim `Round020Tests` (2: picks not in the study compile to the type's devices; one pick present still rules). Sim
`Round018bTests.{Hall, UseDeviceJourney, Heading}` are now `internal` for reuse. All library tests use temp folders.

## How to test (numbered)

1. Build; all tests pass (362). Watch for compile errors first: this round was written without a compiler.
2. Open the demo. LIBRARY ▸ AGENTS ▸ click Commuter ▸ SELECTION ▸ SAVE TO MY LIBRARY. Ticker "MY LIBRARY ▸ saved
   Commuter". `%AppData%\Throng\Library\library.json` exists. The row shows MINE.
3. Change the Commuter's speed in SELECTION. The row shows DIFFERS; SELECTION shows DIFFERS FROM MY LIBRARY and UPDATE
   FROM MY LIBRARY. Click UPDATE: the speed goes back; Ctrl+Z brings your edit back (one step).
4. Edit again, SAVE TO MY LIBRARY: it asks REPLACE first. Confirm. `library.bak.json` now holds the previous version.
5. Duplicate a journey, edit it, PICK IN PLAN a gate, then SAVE TO MY LIBRARY: the dialog warns about picks (D-349).
6. File ▸ New. LIBRARY ▸ JOURNEYS shows your journey marked MINE after the study's (none) and before the built-ins. Draw
   a spawn and a sink and two gates (type Gate); pick your journey on a mix line. CHECK: an Info "its picks aren't in
   this study, so it uses its device type instead" (rule "Picks not in this study"). PLAY: people use the gates.
7. Save the new study, reopen it with your library emptied (rename library.json): it still runs the same (the journey
   was embedded on first use).
8. A profile saved to MY LIBRARY with a changed default journey: both appear as MINE. Pick that profile on a mix line in
   a new study: its default journey is embedded too.
9. Right-click a MINE row ▸ Rename in my library…: the name changes in LIBRARY; a study already using it keeps its own
   name. ▸ Delete from my library…: confirms; the row goes (a built-in saved to your library shows again).
10. LIBRARY ▸ MY LIBRARY ▸ EXPORT…: tick some items, save a .thronglib. Change one of them in your library, then
    IMPORT… the file: the reconcile dialog lists it with Keep both / Skip it / Replace yours. Try each.
11. Put junk in library.json and start the app: ticker says it was kept aside; LIBRARY works with an empty library.
12. While a run is going: SAVE TO MY LIBRARY works (it doesn't touch the study); UPDATE says RUNNING ▸ RESET TO EDIT.
13. Don't-regress (HANDOVER §8): demo loads clean; PLAY crosses all three levels; undo / redo; save → reopen; built-ins
    never change in place; older files open as before.

## Judgement calls

- **Built-ins saved to your library keep the built-in's id** (D-346 "as built-ins"): your version then stands in for the
  built-in in LIBRARY and on first use in new studies; DELETE FROM MY LIBRARY brings the built-in back. Studies that
  already embed the built-in show DIFFERS and can UPDATE. (The alternative, a new id "Commuter (mine)", loses the link to
  the study it was saved from.) See Open question 1.
- **D-349 changes one old behaviour on purpose:** a node whose picks were all deleted was a CHECK error (018b) and is
  now an Info that uses the type / tag (same rule everywhere). The 018b test was updated to match.
- **D-350 detail:** a profile's default journey is saved with it unless it's an unchanged built-in (built-ins are in
  every study already). EXPORT likewise takes a ticked profile's default journey along when it's in your library.
- **Import:** items you hold with the same values are skipped silently; new ids are added; clashes default to **Keep
  both** (non-destructive); a profile replaced or added alongside a journey that came in as a copy follows the copy.
- **Safety net:** besides embedding on first use, anything from your library the study refers to is embedded after
  every model change, before saving and before a run, so studies never depend on your library.
- **Library edits while running:** SAVE TO MY LIBRARY is allowed (it doesn't touch the study); LIBRARY's list (and so
  its right-click and MY LIBRARY buttons) stays locked during runs except on JOURNEYS, as before. UPDATE stays locked.
- **No new `TemplateSource`:** your items are saved as User; studies embed them as Embedded (no format question).
- **Unreadable library files** are moved aside and the library starts empty; a file from a newer Throng (or one that
  can't be opened at all) makes the library read-only for the session so it's never overwritten.

## Accepted defaults

- None beyond the judgement calls.

## Known limitations

- Unbuilt: compile errors are possible (new partial, new dialog, XAML additions).
- The node editor still says "N picked" for a node whose picks fall back; CHECK is where the fallback shows.
- A .throngprofile exported from a profile that's only in your library (not in the study) doesn't include your
  library's journeys (the 016 exchange looks in the study).
- No library undo (D-348): the backup file is one step back.

## Watch list

- `OnModelChanged` now embeds from your library first; cheap, but watch for anything that expects the study library not
  to change during a refresh.
- Your items shadow built-ins with the same id everywhere in the app's lists (not in the sim). If that surprises, see
  Open question 1.

## Open questions (next round)

1. Saving a built-in (or a modified built-in) to your library keeps the built-in's id, so yours replaces the built-in in
   LIBRARY and in new studies. Keep that (recommended: one Commuter, yours), or save built-ins as a separate copy with a
   new id ("Commuter (mine)")?

## Next

Gavin's backlog after 020 (suggested order): seeded batch runs with averages (M8), replay scrubbing (M8), stadium-scale
performance pass + stadium demo, one-page PDF report, group movement.
