# Round 023: replay scrubbing (M8)

**Status:** built 2026-11-01, **unbuilt in the sandbox** (no .NET SDK; statically reviewed: brace / XAML checks, CRLF).
022 confirmed by Gavin (no issues). Decisions D-373–D-376 (queries), D-377 (judgement calls). **375 test methods** (+5).
**Format stays v11** (recordings are session only, cleared at RESET). No new dependencies.

## Decided (D-373–D-377)

| # | What |
|---|---|
| D-373 | Every PLAY records a frame each 0.5 s of simulated time; at a 1 GB cap the older half thins (1/s, then 2 s…). |
| D-374 | Scrubbed view: people, lift cars, trains, clock and counters at that time; RESULTS graph cursor; tables and RUN maps stay at the latest time; live maps blank with a note. |
| D-375 | Transport-bar slider + clock; drag pauses and shows the past; PLAY replays forward at SPEED then carries on live; LIVE jumps to now; ← / → 0.5 s, Shift 10 s; click the RESULTS graph to jump. |
| D-376 | With SOURCE on a batch: RESULTS ▸ RUN (run · seed) + REPLAY runs that seed at MAX into the viewport recording, pauses at the end; SOURCE flips to LAST RUN; study seed untouched, batch kept. |
| D-377 | Judgement calls below. |

## What changed

**Sim**
- `Engine/AgentStore.cs`: `Serial` per person (unique since the last `Clear`, kept by swap-removes, reset with the store).
  Never read by the simulation, so runs are unchanged (test).
- `Replay/` (new, `Throng.Sim.Replay`): `ReplayRecording` (frames, thinning, `CaptureIfDue(engine)`, `TryReadAgents`,
  `StateAt`; `FrameIntervalS` 0.5, `DefaultCapBytes` 1 GB, `CarStride` 5), `ReplayState` (public), internal
  `PackedAgent` (22 bytes: serial, x, y, z and heading as `Half`, stress and stuck bytes, colour palette index, radius cm,
  height 2 cm), `AgentLook` (palette colour), `ReplayFrame`.
- `SimRunner.cs`: records on its own thread after every step (and at start / RESET); new `SimRunner(model, seed,
  durationS)`; `Seed`, `Recording`, `ViewTimeS` (while set, `CopySnapshot`, `CopyLiftCarStates`, `CopyLiftDoorShares`,
  `CopyTrainStates` and `ViewStats` show the recording; the live engine is untouched). Car stride shared with the recording.

**App**
- `ViewModels/MainViewModel.Replay.cs` (new): scrub state, `ScrubPositionS` / `ScrubMaximum` / `ScrubTimeText`,
  `LiveCommand`, `ScrubStepCommand`, `JumpToTimeCommand`, replay playback timer (33 ms; MAX replays at 32×),
  `GraphCursorS`, batch `ReplayRunOptions` / `SelectedReplayRun` / `ReplayRunCommand`.
- `MainViewModel.Run.cs`: PLAY toggles replay playback while scrubbed; STEP goes live first; `IsPlaying` includes
  replay; status line says REPLAY; `StartRunAsync(play, seedOverride, durationOverride)`; RESET clears the scrub; replay
  tickers. Status and clock read `SimRunner.ViewStats`.
- `MainViewModel.Batch.cs` / `MainViewModel.cs`: `_batchStudyChanged` (set on any model change after a batch) → REPLAY
  warns the numbers may differ.
- `MainViewModel.Heatmap.cs`: live maps return nothing while scrubbed, note "LIVE MAPS SHOW AT LIVE".
- `Views/TimeSeriesGraph.cs`: `CursorTimeS` (yellow line) and `JumpCommand` (click a time).
- `MainWindow.xaml`: transport bar gains slider, REPLAY/LIVE clock and LIVE button; RESULTS ▸ RUN + REPLAY row (batch
  source only); graph bindings; ← / → / Shift window key bindings (only while there's something to scrub).

**Tests** (`Round023Tests`, +5): frames every 0.5 s read back as the snapshot (x / y exact, z / heading / sizes / stress
to their packing); interpolation between frames; thinning keeps first and latest frames under the cap; recording never
changes the run; serials unique across removals.

## How to test (numbered)

1. Build; 375 tests pass.
2. Demo, PLAY at ×4 for a couple of minutes. The transport bar clock reads `LIVE · 2:10`; the slider grows.
3. Drag the slider back: the run pauses, people / lifts / trains jump to that time, clock and status line show it
   (status starts REPLAY), the RESULTS graph shows a yellow cursor. Tables stay at the latest time.
4. ← / → step 0.5 s, Shift 10 s (click the viewport first so no text box has focus).
5. PLAY: the replay plays forward at SPEED (people move smoothly between frames); PAUSE stops it; let it reach the end:
   it goes LIVE and the run carries on.
6. LIVE from anywhere: back to now, paused; PLAY resumes the run.
7. Click a point on the RESULTS graph: the viewport jumps to that time.
8. MAP ▸ LIVE ▸ DENSITY while scrubbed: no map, legend note "LIVE MAPS SHOW AT LIVE"; RUN maps still show.
9. Office tower: scrub a morning: lift cars and doors move with the slider.
10. STEP while scrubbed: goes live and steps the run.
11. RESET: slider empties (NO RUN); a new PLAY records afresh.
12. Batch (3 runs), SOURCE ▸ BATCH: RESULTS shows RUN (RUN 1 · SEED …) + REPLAY. Pick run 2, REPLAY: it runs at MAX into
    the viewport, pauses at the end with "REPLAY READY ▸ RUN 2 · SEED …", SOURCE shows LAST RUN. Its devices table matches the
    batch's run-02 CSV. STUDY ▸ RUN seed unchanged.
13. Edit the study, then REPLAY: a warning that the numbers may differ.
14. REPLAY while a run is active: asks first, then replaces it.

## Judgement calls (D-377)

- **Serial on AgentStore**: interpolation needs to match people between frames; the store swap-removes, so indices
  aren't stable. A serial costs 4 bytes per person and is never read by the simulation.
- **Frame packing**: 22 bytes per person (not 16) to carry the serial and per-person body size; colour by palette.
  Demo ≈ 60 MB per 30 min.
- **Replay MAX** plays the recording at 32× (the live run's MAX has no fixed rate).
- **LIVE** doesn't resume the run; PLAY does. Reaching the end of a playing replay does resume it (D-375).
- **STEP while scrubbed** goes live first (the recording can't step the engine).
- **Hover crowd waits** (D-183) stay live while scrubbed (not recorded).
- **REPLAY** sets SPEED to MAX (visible in the transport bar), ends any current run after asking, and warns once the
  study has been edited since the batch.

## Known limitations

- Unbuilt in the sandbox.
- No interpolation for people who appear or leave between two frames (they pop in / out at a frame).
- Recordings aren't saved and aren't exported (video / GIF export would be a later round).
- The slider's track starts at 0 even when thinning has dropped nothing; at very long runs older parts replay at 1 s or
  coarser (by design).

## Open questions (next round)

- Backlog next: stadium-scale performance pass + stadium demo, one-page PDF report, group movement; possibly video / GIF
  export of a replay.
