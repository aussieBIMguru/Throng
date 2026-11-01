# Round 022: UI fit review (before scrubbing)

**Status:** built 2026-11-01, **unbuilt in the sandbox** (no .NET SDK; statically reviewed: brace / XAML checks, CRLF).
021 confirmed by Gavin (works perfectly). Decisions D-368–D-371 (queries), D-372 (the recommended defaults below, taken
on "go"). App-only round: no Sim / IO / format change, **370 test methods** (unchanged). Round 023 = replay scrubbing
(uses the transport bar and D-371).

## Review findings (measured from MainWindow.xaml / Theme.xaml)

| Area | Needs | Has | Problem |
|---|---|---|---|
| Main toolbar | ~2,030 px: 15 ToolButtons at 62 + 4 px (~990), 6 separators, speed ×6 (~210), colour ×2 (~150), map ×3 + RUN ×4 (~430), brand block (~160) | window opens 1,440, min 960 | Overflows at every size; the right end (run maps, brand) is cut off. |
| RESULTS chip rows | TABLE ~320 px, GRAPH ~365 px, SOURCE ~230 px | right panel 290 px (−20 margins) | TABLE and GRAPH rows clip. |
| RESULTS tables | 9–11 columns; batch cells "102 (98–107)" | 290 px | Sideways scroll always; fine once rows/combos fixed and panel wider. |
| Status bar run line | ~150 characters with trains + lifts ≈ 1,300 px (Auto column) | ~1,440 minus clock | Squeezes the ticker to nothing while running; batch line similar. |
| Brand block | ~160 px | — | Takes space the toolbar needs on small windows. |
| Draw strip | ~1,300 px with PLACE / CONNECT options | WrapPanel | Wraps to two rows below ~1,300 px; acceptable (height jump only). |

## Decided (D-368–D-371)

| # | What |
|---|---|
| D-368 | Speed, colour and map leave the top toolbar for a **transport bar** along the bottom of the viewport: SPEED [×1 ▾], COLOUR [PROFILE ▾], MAP [OFF ▾] (live and RUN maps in one list). The 023 scrubber lives there. PLAY / STEP / RESET stay on top. |
| D-369 | **Compact toolbar buttons**: width fits the label (min 44 px); DEMO moves to File ▸ New from demo only; UNDO / REDO icon-only with tooltips. Target ~1,150 px total, fits 1,280. |
| D-370 | RESULTS: SOURCE, TABLE and GRAPH become drop-downs (TABLE and GRAPH on one line); right panel opens at 340 px (still draggable, min 220). |
| D-371 | Batch scrubbing (round 023): RESULTS ▸ REPLAY picks run k; it re-runs on seed base+k into the viewport at MAX while recording, then scrubs like PLAY (exact by D-366; no batch memory cost). |

## Defaults taken (D-372; cheap to change)

- Status bar: compact run line (`×4 · 412 IN MODEL · 900 IN · 488 OUT · 37 QUEUING · 12 STRESSED · 0 STUCK`, trains /
  lifts only when present); the full line moves to its tooltip; ticker gets the rest of the width. Batch line:
  `BATCH 2/5 · 3 RUNNING · 46%`.
- Brand block: 32 px logo, tagline hidden below 1,600 px window width.
- Transport bar: 28 px high, shown always (combos usable before a run; MAP shows nothing until there's a map), dark strip
  matching the draw strip; MAP list grouped LIVE (Density, LoS) / RUN (Peak LoS, E+ time, Mean, Trail).
- Draw strip unchanged (wraps by design).
- Chips stay elsewhere (CHECK filters, draw tools, snaps): they fit.

## What changed (as built)

- `Themes/Theme.xaml`: `ToolButton` is label-width (`MinWidth` 44, `Padding` 7,0 through the border) and hides its label
  when `Content` is null; new `ToolIconButton` (min 38).
- `MainWindow.xaml` toolbar: DEMO button removed (File ▸ New from Demo has both demos), UNDO / REDO icon-only, the speed /
  colour / map / run-map chips removed; brand logo 32 px, title 26 px, tagline `BrandTagline` hidden under 1,600 px
  (`MainWindow.xaml.cs` SizeChanged). Estimated width ~930 px (was ~2,030).
- Body grid gains a second row: the **transport bar** under the viewport (`Grid.Row=1`, column 2; side panels and
  splitters span both rows). SPEED, COLOUR, MAP drop-downs bound to `SpeedChoice`, `AgentColoring`, `HeatmapMode`.
- RESULTS: SOURCE drop-down (only with a batch), TABLE and GRAPH drop-downs on one line; the GRAPH chip row is gone.
  Right panel opens at 340 px.
- Status bar: `RunStatusText` is compact; `RunStatusDetail` (full line) is its tooltip; batch line `BATCH 2/5 · 3 RUNNING · 46%`.
- `ViewModels/MainViewModel.Choices.cs` (new): option lists (`LabelledOption<T>`) and `SpeedChoice`, `SelectedTable`,
  `SelectedGraph`, `SourceOptions`, `SelectedSource`; the chip properties remain and stay in sync.

## How to test (numbered)

1. Build; 370 tests pass (nothing in Sim changed).
2. Window at 1,440 (default): the whole toolbar shows, NEW … RESET then THRONG; no clipping. Tagline hidden; widen past
   1,600: it appears.
3. Shrink to ~1,280: toolbar still fits. At the 960 minimum: note what clips (expected only the brand block).
4. UNDO / REDO show icons only with tooltips; File ▸ New from Demo still loads both demos (Ctrl+Shift+D too).
5. Transport bar under the viewport: SPEED ×1 by default; change it before and during PLAY (status line follows).
6. COLOUR PROFILE / STRESS switches colouring live. MAP: each entry shows its map while running; RUN entries show the
   batch maps with SOURCE on a batch (no run needed); NO MAP clears.
7. JOURNEY view (F5): the transport bar stays below the canvas; nothing overlaps.
8. RESULTS: TABLE and GRAPH drop-downs switch table and graph; after a batch SOURCE offers LAST RUN / BATCH (N), and PLAY
   flips it to LAST RUN.
9. Right panel opens at 340 px; drag the splitter: the drop-downs stretch, the table scrolls sideways.
10. While running: the compact status line fits beside the ticker; hover it for the full line. During a batch:
    `BATCH 2/5 · …`.

## Build notes (from the queries)

- Chip properties (`IsSpeed…`, `IsColour…`, `IsHeat…`, `IsTable…`, `IsGraph…`, `IsSource…`) stay for compatibility;
  add `SpeedOptions/SelectedSpeed`, `ColouringOptions/SelectedColouring`, `HeatmapOptions/SelectedHeatmap`,
  `TableOptions/SelectedTable`, `GraphOptions/SelectedGraph`, `SourceOptions/SelectedSource` bound to ComboBoxes.
- ToolButton style: `Width` → `MinWidth="44"`, `Padding="6,0"`; icon-only variant for UNDO / REDO.
- Re-measure after the build at 1,280 / 1,440 / 1,920 widths (checklist).
