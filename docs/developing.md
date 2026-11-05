# Developing Throng

← [Back to README](../README.md)

Throng is C# / .NET 10 (WPF), with OpenGL through OpenTK and GLWpfControl. Pull requests aren't set up yet. Please open
an [issue](https://github.com/aussieBIMguru/Throng/issues) first.

## Build and run from source

Requirements: Windows 10/11, Visual Studio with the .NET desktop workload, .NET 10 SDK, a GPU/driver with OpenGL 3.3.

1. Open `Throng.sln`.
2. Build (NuGet restores OpenTK, GLWpfControl, CommunityToolkit.Mvvm and MSTest).
3. Run **Throng.App** (it's the default startup project). The exe is `Throng.exe`.
4. Click **DEMO** (or File ▸ New from Demo, Ctrl+Shift+D) to load the Interchange Station demo.

The VT323 retro font is bundled (`src/Throng.App/Assets/Fonts`, SIL OFL); the UI falls back to Consolas if it can't load.

Tests: Test ▸ Run All Tests in Visual Studio, or `dotnet test` from the repo root.

Clean: close Visual Studio and run `clean.bat` to delete every `bin`, `obj` and `TestResults` folder (keeps `.vs`)
before zipping the project. `clean.bat /q` skips the pause.

## Releasing (installer)

Needs the .NET 10 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php) (a build tool; not shipped).

1. Set the version in `Directory.Build.props` (`<Version>`, currently 1.0.0) — the only place it lives.
2. Close Visual Studio and double-click `publish.bat` (or `powershell -ExecutionPolicy Bypass -File build\publish.ps1`).
   It runs the tests (not the bench), publishes Throng.App self-contained for win-x64 to `artifacts\app\` and builds
   `artifacts\Throng-Setup-<version>.exe` plus its SHA-256. Switches: `-SkipTests`, `-AllTests` (also the long tests tagged `Slow`, which are skipped by default), `-InnoCompiler <path to ISCC.exe>`,
   `-Sign -CertThumbprint <thumbprint>` (signing is off for 1.0).

The setup installs per user (no admin) to `%LocalAppData%\Programs\Throng`, adds a Start menu shortcut (desktop
optional) and opens `.throng` files on double-click. Uninstalling keeps your library (`%AppData%\Throng`) and logs.
Unsigned builds show a SmartScreen warning the first time (More info ▸ Run anyway).

Version numbers follow `major.minor.patch`. Add an entry to [CHANGELOG.md](../CHANGELOG.md), then tag the commit
(`v1.0.0`) and attach `Throng-Setup-<version>.exe` and its `.sha256.txt` to a GitHub release.

## Solution layout

```
Throng.sln
├─ src/
│  ├─ Throng.Core        Model, geometry (Clipper2 behind IPolygonOps), library, undo + editing actions, validation, world
│  ├─ Throng.Sim         Clock, RNG, Nav (grids, portals, flow fields, route choice), Engine (social force), SimRunner
│  ├─ Throng.Analysis    Level of Service classification (heatmaps and stats from M5)
│  ├─ Throng.IO          .throng file format, migrations, demo study builder
│  ├─ Throng.Render      OpenGL renderer, cameras, scene building
│  └─ Throng.App         WPF shell (exe: Throng.exe)
├─ tests/                MSTest projects for Core, Sim (+Analysis) and IO
├─ build/              publish.ps1 (release script)
├─ installer/          Throng.iss (Inno Setup 6)
├─ ai/                   Working protocol, brief, decision log, handover and per-round notes (AI exchange folder)
├─ LICENSE               MIT
└─ THIRD-PARTY-NOTICES.md
```

Dependencies only point downward: `App → Render / Analysis / IO → Sim → Core`. Core and Sim have no UI or GL
references, so the engine can run headless.

---

## Files

| Extension | What |
|---|---|
| `.throng` | Study: zip of `manifest.json`, `study.json`, `library.json` (file format v5 since round 007; v1–v4 files still open; underlay images inside `underlays/`) |
| `.throngprofile` | Single exported agent profile (M7) |
| `.thronglib` | Template library exchange (M3) |

---

## Conventions

- C# latest, nullable on, file-scoped namespaces, XML doc headers, `_camelCase` private fields.
- No exceptions surfaced to the user: operations return `Result` / `Result<T>`; exceptions are caught at
  boundaries and logged to `%LocalAppData%\Throng\Logs\throng.log`.
- Every model edit goes through the undo stack.
- SI units internally (metres, seconds).
- Only permissively licensed dependencies (BRIEF §20); every one is listed in `THIRD-PARTY-NOTICES.md`.

## Licensing rules

Only permissively licensed dependencies are allowed: MIT, BSD, Apache-2.0, Boost, zlib, and SIL OFL for fonts. No
copyleft. Every shipped package, including transitive packages and native binaries, is listed in
[THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md), which ships beside `Throng.exe`. Run
`dotnet list src\Throng.App package --include-transitive` after adding or bumping a package.

## Design notes

`ai/` holds the product brief (`BRIEF.md`), the numbered decision log (`DECISIONS.md`) and per-round notes
(`rounds/`), written as the app was built round by round.
