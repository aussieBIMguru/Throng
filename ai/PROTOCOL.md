# Throng — Working protocol

**Base protocol for every round and every new chat.** Read this first, then `HANDOVER.md`, `BRIEF.md`,
`DECISIONS.md` and the latest `rounds/round-NNN.md`. If anything elsewhere disagrees with this file, this file wins.

---

## 1. Every round has two phases: Query, then Build

### Phase 1 — Query (no source changes)

1. Gavin attaches the whole zipped project (plus any build errors or feedback).
2. Claude reads the docs above and reviews the code that the round touches.
3. Claude asks **queries first, with recommendations**:
   - Small staged batches (about 4 questions at a time), most fundamental first, so later questions can build on
     earlier answers.
   - Multiple choice. **Recommended option first, labelled "(Recommended)"**, with a one-line reason.
   - Covers anything the round depends on: scope, behaviour, UX, naming, dependencies, trade-offs.
   - Questions that don't block the build go at the end of the round notes instead (see §3).
4. Claude doesn't touch `src/` or `tests/` in this phase. Doc-only housekeeping (like this file) is fine and is
   called out.

### Phase 2 — Build (only what was decided)

1. Gavin says what's decided (answers, or "go with recommendations") and gives the go-ahead to build.
2. Claude logs each answer as a numbered decision in `DECISIONS.md` (with a "why") **before** building.
3. Claude builds exactly the decided scope on the zipped project. Anything new that comes up mid-build and would
   change behaviour or scope is either:
   - small and clearly in the spirit of a decision → done, and listed under "Judgement calls" in the round notes; or
   - a real choice → left out, and asked at the top of the next round's Query phase.
4. Claude returns the **full updated zip**.

**Accepted defaults:** if Gavin doesn't answer a non-blocking question, Claude may use the recommended option only
if it's cheap to change later, and must list it under "Accepted defaults" in the round notes. Anything hard to undo
(file format, public structure, dependencies, licence) always waits for an explicit answer.

---

## 2. Exchange format

- The exchange is always the **whole zipped project**. Gavin attaches a zip; Claude returns the full updated zip.
- Before zipping, Gavin runs `clean.bat` (repo root) to strip `bin/obj/TestResults`. Zips Claude produces also
  exclude `bin/`, `obj/`, `TestResults/` and `.vs/`.
- **Gavin builds and tests in his own Visual Studio** and reports errors; they're fixed first in the next round.
- If Claude's environment has no .NET SDK (check for `dotnet` first), the code is statically reviewed only and the
  round notes say plainly that the round is **unbuilt**.

## 3. What every round updates

| File | What |
|---|---|
| `ai/rounds/round-NNN.md` | Decisions taken, what changed, how to test (numbered checklist), judgement calls, accepted defaults, known limitations, watch list, open questions, next |
| `ai/BRIEF.md` | Kept current; version bumped with a change line at the top |
| `ai/DECISIONS.md` | Numbered decisions appended (next free number), each with a "why"; superseded ones marked, never deleted |
| `ai/HANDOVER.md` | Rewritten when a chat is handed over, or when state changes materially |
| `README.md`, `THIRD-PARTY-NOTICES.md` | Whenever relevant (status line, build steps, new packages) |

## 4. Standing rules

- Dependencies: permissive licences only (BRIEF §20 allow-list). Every shipped package, including transitive ones,
  goes in `THIRD-PARTY-NOTICES.md`; inspirations go in its Acknowledgements. A new dependency is always a Query.
- Code conventions are in `HANDOVER.md` §3 and `README.md` ▸ Conventions.
- Don't-regress list is in `HANDOVER.md` §8; every round's test checklist re-checks it.
