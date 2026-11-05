# Round 026: installer and publisher (v1.0.0)

**Status:** built on r025b-fix, **unbuilt in the sandbox** (no .NET SDK, Inno Setup or PowerShell; reviewed by reading
only).
- Gavin confirmed the r025b PDF font fix: the export works.
- Performance tuning is parked until after v1 (PERF overlay F8 and `bench.bat` stay).
- No code changes beyond the version: the About box's fallback string now reads 1.0.0.
- 409 test methods (unchanged), format v12, no new packages.

This round follows BimGo's approach (its `build\publish.ps1` and `installer\BimGo.iss`), minus the Revit bundle.

## Decisions taken
| # | Decision |
|---|---|
| D-407 | Self-contained .NET 10 win-x64 folder publish (no single file, no trimming). No runtime prerequisite. |
| D-408 | Per-user install in `%LocalAppData%\Programs\Throng`, with no admin prompt. |
| D-409 | Only `.throng` is associated, through HKCU keys written by the `.iss`. `.throngprofile`, `.thronglib` and `.throngdevice` stay import-from-app. |
| D-410 | Uninstall keeps `%AppData%\Throng` (your library) and `%LocalAppData%\Throng\Logs`, with no prompt. |
| D-411 | Start menu shortcut, plus a desktop shortcut as an optional task, unticked by default. |
| D-412 | LICENSE is shown as an information page, with no accept step. Notices and OFL are installed next to the exe. |
| D-413 | Signing comes later: `-Sign -CertThumbprint` in the script, off for 1.0. |
| D-414 | The version is `<Version>` in `Directory.Build.props`, now **1.0.0**. It feeds the assemblies, the installer and the setup file name. |
| D-415 | Layout as BimGo: `build\publish.ps1`, `installer\Throng.iss` and `artifacts\` (gitignored), plus `publish.bat` at the root. |
| D-416 | From BimGo, setup waits for Throng to be closed (Retry / Cancel) and starts the app folder clean. "Start Throng" at the end is unticked. |
| D-417 | Publisher Gavin Nicholls, MIT. The AppId is `{4F693B47-3FE5-43E3-B724-77382442AD08}` and never changes. |
| D-418 | Judgement calls for this round (below). |

## What changed
- **`publish.bat`** (root) calls **`build\publish.ps1`**, passing any arguments through, then pauses. The script:
  1. Reads `<Version>` from `Directory.Build.props` (must be major.minor.patch).
  2. Clears `artifacts\`.
  3. Runs `dotnet test Throng.sln -c Release --filter TestCategory!=Bench`.
  4. Runs `dotnet publish src\Throng.App -c Release -r win-x64 --self-contained true` (no trim, no single file) to `artifacts\app\`, then removes `.pdb` files.
  5. Checks that `Throng.exe`, `LICENSE`, `THIRD-PARTY-NOTICES.md` and `Assets\Fonts\OFL.txt` are there.
  6. Signs if `-Sign` is passed.
  7. Runs `ISCC /DAppVersion=<v>` into `artifacts\`. ISCC is found from `-InnoCompiler`, then the three usual Inno Setup 6 folders, then PATH.
  8. Writes `Throng-Setup-<v>.exe` and `Throng-Setup-<v>.sha256.txt`.

  Switches: `-SkipTests`, `-Sign -CertThumbprint <t>`, `-InnoCompiler <path>`.
- **`installer\Throng.iss`** (Inno Setup 6):
  - Per user, x64, Windows 10+, modern wizard. The folder is fixed and there's no folder or group page.
  - Steps: LICENSE info page, then the desktop shortcut option, then install.
  - `[InstallDelete] {app}\*` runs first, so an upgrade leaves no stale DLLs behind.
  - The `.throng` association goes in HKCU, with the icon taken from `Throng.exe,0`.
    - It's also listed under OpenWithProgids and `Applications\Throng.exe\SupportedTypes`, and `ChangesAssociations=yes` refreshes Explorer.
    - Uninstall removes only Throng's own values and keys.
  - Uninstall deletes only `{app}`.
  - Setup and uninstall both check whether Throng.exe is running (WMI) and ask the user to close it. A silent setup just fails if Throng is open.
- **Version** is 1.0.0 (`Directory.Build.props`). The About box shows it.
- **THIRD-PARTY-NOTICES.md**:
  - The bundled .NET runtime and WPF are now listed (MIT, Microsoft / .NET Foundation), because self-contained ships them.
  - Inno Setup is recorded as a build tool that isn't shipped.
- **README**: new "Release / installer" section. **BRIEF** is now v0.49 (§15, §16 M9, §20.3).

## How to test
1. Install **Inno Setup 6** (jrsoftware.org; the default per-machine install is fine).
2. Close Visual Studio, then double-click **`publish.bat`**.
   - Expect: the tests pass, then `== Done: …\artifacts\Throng-Setup-1.0.0.exe` and a SHA-256 line.
   - The setup should be roughly 60–90 MB.
3. Run the setup.
   - Expect: no UAC prompt, then the licence info page (Next only, no accept), then the desktop-shortcut option (unticked), then Install, then "Start Throng" (unticked).
4. Start menu ▸ Throng opens. **Help ▸ About** says **Throng 1.0.0**.
5. Save a study. In Explorer, the `.throng` file shows the Throng icon, and double-clicking it opens that study.
6. Open the PDF report and the demos from the installed copy. This proves fonts and the report work outside VS.
7. **Upgrade:** bump `<Version>` to 1.0.1, publish and run the new setup over the old one.
   - Expect: no second entry in Apps & features, and the version reads 1.0.1.
   - Change it back to 1.0.0 afterwards, or keep it.
8. With Throng open, run the setup.
   - Expect: "Please close Throng…" with Retry / Cancel. Close Throng, choose Retry, and it carries on.
9. **Uninstall** from Apps & features.
   - Expect: the app folder and shortcuts are gone, and `.throng` files lose the association.
   - `%AppData%\Throng\Library` and `%LocalAppData%\Throng\Logs` are still there.
10. Optional: test on a PC or VM **without** the .NET 10 runtime. It should start without asking for .NET.
11. Optional: check `publish.bat -SkipTests` and the `-InnoCompiler` path override.

## Judgement calls (D-418)
- **Folder:** fixed, with no folder page, like BimGo. This keeps `[InstallDelete] {app}\*` safe, because it can never point at a folder the user chose.
- **Tests:** the release test run filters out `TestCategory=Bench`, so the bench never slows a release.
- **ProgId** is `Throng.Study` and the file type reads "Throng study". The `.throng` default value is removed on uninstall, but only Throng's own value (`uninsdeletevalue`).
- **Silent installs** (`/SILENT`, `/VERYSILENT`) fail quietly if Throng is running, rather than showing a message.
- **No SHA-256 for the app folder**, only for the setup.
- **Publisher URL** is left blank until there's a git repo.
- **No ReadyToRun.** It would speed up the first start a little, but it makes the install larger. It can be added later with `-p:PublishReadyToRun=true`.
- **One instance per double-click.** Double-clicking a `.throng` while Throng is open starts a second Throng. That's the current behaviour, and a single-instance handoff could come later.

## Known limitations
- Unsigned, so SmartScreen shows "Windows protected your PC" on first run (More info ▸ Run anyway) until there's a certificate (D-413).
- The PDFsharp transitive *(verify)* lines in THIRD-PARTY-NOTICES are still open. Please paste the output of `dotnet list src\Throng.App package --include-transitive` once.

## Watch list
- If `Assets\Fonts\OFL.txt` lands somewhere else in the publish output, the script's licence-file check will say so. Adjust the path there and in this note.
- Some setups have restricted WMI. The running check then says "not running" and Windows' own locked-file prompt takes over, the same as BimGo.

## Next
- v1 is out. Backlog:
  - performance (start with the nav compile time)
  - replay video / GIF export
  - an across-runs nodes CSV
  - fuller SERVICES
  - signing
  - opening `.thronglib` / `.throngprofile` / `.throngdevice` from Explorer


---

# Round 026b: stair-end squeeze (before 026 testing)

**Status:** built on r026, **unbuilt in the sandbox**. 409 test methods; the round-007 tuning diagnostics gain a
"no stair-end squeeze" variant.

**Gavin:** with big crowds, people at stair feet block each other and wall the mouth off. One switch
(`SimTuning.StairEndSqueeze`, on) now applies within **3 m** of any **stair or ramp** end. It covers both the floor in
front of the end and the flight or landing that meets it. There, people going the same way on the same surface:
- push each other at the landing share (`LandingSocialFactor` 0.3, D-135)
- count as **85 %** of their combined radius for contact (`StairEndContactFactor`)

Escalator and travelator ends are unchanged; they already close up at the comb (D-128). Code is in
`Engine/SimEngine.cs`: `BuildStairEnds`, `NearStairEnd`, and the `squeeze` branch in the crowd loop.

**Test:** the stadium demo (egress), or your concourse study. Crowds at a stair foot or head should pack into the mouth
and keep flowing, without long stand-offs. Up close, people can overlap a little within 3 m of the end. That's
expected at 85 % contact.

**Open for round 027 (queries):** route choice between free-circulation devices. Gavin's screenshot shows everyone
heading for the two middle stairs while the side stairs sit empty. People should try a quieter stair a little further
away once theirs fills.

What exists today:
- A stair's wait is counted once its entry is within the person's sight (30 m default, D-179).
- People look again on the approach (D-180), with an 8 s bonus for staying (`StayBonusS`).

Suspects to check with Gavin's study:
- People more than 30 m away don't see the queue they're joining.
- The walk to another stair is costed as free walking.
- The side stairs might not allow that direction.
- The wait read at a choked foot.


---

# Round 027: route choice between free-circulation devices

**Status:** built on r026b, **unbuilt in the sandbox**. Format v12; no new tests beyond a "no crowd sight" tuning
variant.

**Gavin:** everyone piles onto the two middle stairs while the side stairs sit empty. A stair or escalator should
become *known* as high traffic, and past a trade-off point people should use the next nearest free-ish equivalent.

## Decisions taken
| # | Decision |
|---|---|
| D-423 | Big crowds are seen from further. Each person barely moving towards a connector (anywhere on the approach) adds 0.25 m to its sight distance, up to +50 m. |
| D-424 | Trade-off is expected time. The stay bonus drops from 8 s to 5 s, plus 15 % of the current connector's wait, so people spill over steadily. Exit choice keeps its 8 s. |
| D-425 | An equivalent is any route to the same place. Stairs, escalators and ramps are weighed by the profile (stair effort, mechanised preference, needs), as before. |
| D-426 | Herding: people already heading for a connector count towards its wait even out of sight. People beyond 5 m count ÷ capacity. Reviews were already staggered (`IsReviewTick`). |

## What changed
- `SimEngine`:
  - Counts `_queued[k]` (people barely moving on the way in), then `UpdateCrowdSight` sets `_seenFromM[k]` and `_pendingS[k]`.
  - `ReviewApproach` reviews once the target is within sight + `_seenFromM`.
  - Every route choice passes `seenFromM`, `pendingWaitS` and `stayWaitShare`.
- `RoutePlanner.Choose`:
  - Reach = sight + crowd bonus.
  - Out of sight, the wait is the pending share.
  - Stay discount = `staySeconds` + `stayWaitShare × wait`.
- `SimTuning.CrowdSight` (on). Off = D-179 sight only, but the 5 s / 15 % stay rule still applies.

## How to test
1. Run your concourse study (or the stadium sector, egress).
2. As the middle stairs fill, people further back should peel off to the side stairs. The spill should be steady, not
   one big swing, and the side stairs should take a share of the flow.
3. Compare with the same seed and `CrowdSight` off (Round007 diagnostics, "no crowd sight").
4. A small queue (a handful of people) shouldn't send anyone across the floor.

## Judgement calls
- Lifts get the sight bonus (a lobby crowd is visible) but no pending term. Lift waits already have their own counting.
- The constants (0.25 m per person, 50 m cap, 15 %, 5 s) are first guesses. They're private constants in
  `SimEngine.cs` for tuning after your test.


---

# Round 028: GitHub docs and quicker publish (v1 ship)

**Status:** built on r027, **unbuilt in the sandbox**.

## Decisions taken
| # | Decision |
|---|---|
| D-427 | `README.md` is now user-facing: hero image, features, download / install, quick start and doc links. Building, layout and conventions moved to `docs/developing.md`. |
| D-428 | `docs/` holds `getting-started.md`, `user-guide.md` and `developing.md` (which includes releasing), plus `docs/images/` with 8 placeholder PNGs and a shot list. |
| D-429 | The repo is `github.com/aussieBIMguru/Throng`, used for Releases and Issues links and the installer's publisher, support and updates URLs. `CHANGELOG.md` has a 1.0.0 entry; there's no CONTRIBUTING file yet. |
| D-430 | Long tests are tagged `[TestCategory("Slow")]`. `publish.ps1` skips them unless `-AllTests` is passed; Test Explorer still runs them. |

The nine tests tagged Slow:
- `Diagnostics_Round007Rules`
- `StadiumSector_FansGetThroughTheTurnstilesAndUpstairs`
- `Batch_AveragesGroupRows_AndWritesThemToJourneysCsv`
- `Cancelled_ThrowsAndStops` and `BatchRun_IsPlayOnItsSeed`
- `LiftResults_LeaveTheWarmUpOut` and `LiftResults_CountBoardingsWaitsAndRides`
- `Demo_PeopleReachThePlatform` and `Poisson_RateIsAboutRight`

I picked these from simulated duration and batches, not timings. If publish is still slow, sort Test Explorer by
duration and add `[TestCategory("Slow")]` to the worst.

## To do before pushing
1. Replace the placeholder screenshots (`docs/images/README.md` lists them), then delete the `<!-- TODO image -->`
   notes.
2. Run `publish.bat` and tag `v1.0.0`.
3. Create a GitHub release with `Throng-Setup-1.0.0.exe` and its `.sha256.txt`.
4. Decide whether `ai/` goes public. It holds the design notes and round history; the docs link to it once, from
   developing.md.
