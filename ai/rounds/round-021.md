# Round 021: seeded batch runs with averages (M8) — QUERIES

**Status:** **Query phase done 2026-10-31 (D-352–D-365). Build: next chat.** Gavin attaches the zip plus any 020
build errors; fix those first (Step 0), then build exactly the decisions below. One build, no split.

## Decided (D-352–D-365)

| # | Answer |
|---|---|
| D-352 | Round 021 = batch runs with averages (after 020 fixes). |
| D-353 | Built-ins saved to MY LIBRARY keep the built-in's id (020 call confirmed; nothing to change). |
| D-354 | N runs, default 5 (2–20), seeds base … base+N−1. |
| D-355 | **Parallel across cores** (not the recommended sequential): each run identical to PLAY on its seed. |
| D-356 | Parallel = cores − 1, automatic, capped at N. |
| D-357 | Viewport shows nothing live: progress line + CANCEL; editing locked. |
| D-358 | All RESULTS tables: mean with min–max. |
| D-359 | Graphs: mean line + min–max band. |
| D-360 | Heatmaps: averaged cumulative; peak = highest across runs. |
| D-361 | 'Until stopped' asks a duration; 'N journeys' runs as set. |
| D-362 | Session only + CSV (summary + per run); format stays v11. |
| D-363 | STUDY ▸ RUN ▸ BATCH…; RESULTS source switch LAST RUN / BATCH (N). |
| D-364 | Overlay ACTUAL = batch mean, labelled BATCH (N). |
| D-365 | CHECK errors block; study warm-up per run. |

## Build notes (from the answers)

- Parallel (D-355) means each run needs its own compiled model or a model safe to share read-only: check whether
  `SimSetup.Build` output (`SimModel`, nav fields) is immutable during a run before sharing; otherwise build once per
  run (compile cost × N). Each worker owns its `SimEngine` and RNG; never share `AgentStore` or density layers.
- Memory with parallel runs: keep per run only `RunResults` + its cumulative density layer for averaging (D-360); drop
  engines as each run ends.
- Determinism test: a batch's run k equals a single headless run on seed base+k; two batches with the same base give
  identical summaries regardless of thread count.
- No new dependencies expected (Parallel / Task from the BCL).

(The original query list follows for reference.)

**Why this round:** backlog order after 020 (HANDOVER §9): seeded batch runs with averages (M8), then replay scrubbing,
stadium-scale performance + stadium demo, one-page PDF report, group movement. The engine already runs headless
(BRIEF §103) and results come from `SimRunner.LatestResults` / `RunResults`, so batches reuse what exists.

## Step 0 — before any new work

- Fix the 020 build errors and test failures Gavin reports (020 was written without a compiler).
- Confirm 020 by walking its 13-step checklist (`round-020.md`).

## Batch A — scope and the 020 leftover (ask first)

1. **What is round 021?**
   - **Batch runs with averages (M8) (Recommended)** — next in the agreed backlog; makes results trustworthy (one seed is one sample).
   - Replay scrubbing (M8) first.
   - Stadium-scale performance pass + stadium demo first.
   - Only fix 020 and confirm it this round.
2. **020 open question: a built-in saved to MY LIBRARY…**
   - **Keeps the built-in's id, so yours replaces it in LIBRARY and new studies (Recommended)** — as built; one Commuter, yours; DELETE brings the built-in back.
   - Gets a new id ("Commuter (mine)"), the built-in stays alongside — loses the DIFFERS link to the study it came from.
3. **How many runs and which seeds?**
   - **N runs (default 5, 2–20) on seeds seed, seed+1, … seed+N−1 (Recommended)** — repeatable; RE-ROLL still changes the base seed.
   - A typed list of seeds.
   - N random seeds each time (not repeatable).
4. **How a batch runs**
   - **Headless on a worker thread, one run after another, progress "run 3 of 5" + CANCEL, editing locked (Recommended)** — simple, deterministic, no GPU.
   - Several runs in parallel across CPU cores (faster, more memory; same results).
   - Visible runs one after another in the viewport.

## Batch B — what gets averaged

5. **Which results**
   - **Every RESULTS table (devices, zones, journeys, lifts, journey nodes): mean, with min–max across runs (Recommended)** — one view, spread shown.
   - Means only.
   - A chosen short list of headline numbers.
6. **Time-series graphs**
   - **Mean line with a shaded min–max band (Recommended)**.
   - Every run overlaid as thin lines.
   - Mean only.
7. **Heatmaps (run heatmaps, M5)**
   - **Average of the runs' cumulative heatmaps; peak = highest across runs (Recommended)**.
   - Last run's heatmap only.
   - None for batches.
8. **Studies set to "until stopped" or "N completed journeys"**
   - **Batch asks for a duration up front for "until stopped"; "N journeys" runs as set (Recommended)**.
   - Batch only allowed for fixed-duration studies.

## Batch C — where it shows and what's kept

9. **Where batch results live**
   - **This session only, plus CSV export (one summary file and one per run) (Recommended)** — no format change.
   - Saved in the .throng (format v12).
   - A separate results file next to the study.
10. **UI**
    - **STUDY ▸ RUN ▸ BATCH… (N, seeds shown, START); RESULTS gets a source switch LAST RUN / BATCH (N) (Recommended)**.
    - A separate BATCH tab.
    - Toolbar button next to PLAY.
11. **Journey run overlay (019) after a batch**
    - **ACTUAL shows the batch mean (counts and shares), with a "BATCH (N)" label (Recommended)**.
    - ACTUAL stays the last single run.
12. **CHECK and warm-up**
    - **Same rules as PLAY: CHECK errors block the batch; each run uses the study's warm-up (Recommended)**.
    - Skip CHECK for batches.

## Non-blocking (end of round notes if not asked)

- A determinism test: two batches with the same base seed give identical summaries.
- Spread wording: show "min–max" or "± standard deviation"? (Recommend min–max: easier to read for architects.)
- Later rounds: replay scrubbing, stadium pass, PDF report (could take the batch summary), group movement.

## Watch list for the build

- `SimRunner` owns its thread; a batch runner must not share engine state between runs, and must free each run.
- Results must match a single PLAY with the same seed (batch run k = PLAY on seed+k).
- Memory: keep per-run `RunResults` only, not snapshots or density layers beyond what averaging needs.
