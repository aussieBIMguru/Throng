# Third-party notices

Throng is licensed under the MIT License (see `LICENSE`). It uses the third-party components below, each under
its own licence. This file ships next to `Throng.exe` (BRIEF §20.3).

> **Audit status (round 003):** versions pinned from Throng.App's `project.assets.json` (D-028) and the full
> transitive package list recorded below. Exact copyright lines still need copying from each package's
> `LICENSE`/`License.txt` in `%UserProfile%\.nuget\packages\<id>\<version>\` (lines marked *(verify)*).
> Test packages (MSTest, Microsoft.NET.Test.Sdk) still float; they don't ship.

### Shipped packages (resolved for Throng.App, net10.0-windows)

| Package | Version | Licence | Pulled in by |
|---|---|---|---|
| Clipper2 | 1.5.4 | Boost Software License 1.0 | Throng.Core |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Throng.App |
| OpenTK.GLWpfControl | 4.3.6 | MIT | Throng.App |
| OpenTK (meta-package) | 4.9.4 | MIT | OpenTK.GLWpfControl |
| OpenTK.Graphics | 4.9.4 | MIT | Throng.Render; OpenTK |
| OpenTK.Core | 4.9.4 | MIT | OpenTK |
| OpenTK.Mathematics | 4.9.4 | MIT | OpenTK |
| OpenTK.Audio.OpenAL | 4.9.4 | MIT | OpenTK (unused by Throng) |
| OpenTK.Compute | 4.9.4 | MIT | OpenTK (unused by Throng) |
| OpenTK.Input | 4.9.4 | MIT | OpenTK (unused by Throng) |
| OpenTK.Windowing.Common | 4.9.4 | MIT | OpenTK |
| OpenTK.Windowing.Desktop | 4.9.4 | MIT | OpenTK |
| OpenTK.Windowing.GraphicsLibraryFramework | 4.9.4 | MIT | OpenTK |
| OpenTK.redist.glfw | 3.4.0.44 | zlib/libpng (GLFW) — native `glfw3.dll` | OpenTK.Windowing.GraphicsLibraryFramework |
| PDFsharp | 6.2.4 | MIT | Throng.App (PDF report, round 025) |
| Microsoft.Extensions.Logging.Abstractions | ≥ 8.0.3 *(verify resolved version)* | MIT | PDFsharp |
| Microsoft.Extensions.DependencyInjection.Abstractions | ≥ 8.0 *(verify)* | MIT | Microsoft.Extensions.Logging.Abstractions |
| System.Security.Cryptography.Pkcs | ≥ 8.0.1 *(verify)* | MIT | PDFsharp |

### Bundled runtime (self-contained release, round 026)

Release builds are self-contained (D-407), so the installer also ships the Microsoft .NET 10 runtime and the Windows
Desktop runtime (WPF) for win-x64. Both are MIT: Copyright (c) .NET Foundation and Contributors (MIT text below).
The runtime also carries its own `THIRD-PARTY-NOTICES.TXT` for components it includes
(https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT and
https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT).

### Build tools (not shipped)

- **Inno Setup 6** (Jordan Russell and Martijn Laan, https://jrsoftware.org) builds the installer. Its own licence
  allows free use, including for commercial software; no Inno Setup source ships with Throng (only the compiled setup
  stub that every Inno-built installer carries).

The GLFW native library ships as `runtimes/win-x64/native/glfw3.dll`. Its zlib/libpng licence only requires that
the notice is kept with source distributions; it is acknowledged here anyway. Copyright (c) 2002-2006 Marcus
Geelnard, (c) 2006-2019 Camilla Löwy *(verify against COPYING.md in the package)*.
> **Audit status (round 001):** package versions float within their major version until the first successful
> restore. The exact versions, transitive packages (including any native binaries such as GLFW pulled in by
> OpenTK) and each component's exact copyright line must be confirmed from the restored packages'
> `LICENSE` files and recorded here. Lines marked *(verify)* are pending that audit.

---

## Shipped dependencies

### OpenTK.Graphics (OpenGL bindings)
- Licence: MIT
- Project: https://github.com/opentk/opentk
- Used by: Throng.Render
- Copyright: the OpenTK project and contributors *(verify exact line from package LICENSE)*

### OpenTK.GLWpfControl (WPF OpenGL host)
- Licence: MIT
- Project: https://github.com/opentk/GLWpfControl
- Used by: Throng.App
- Copyright: the GLWpfControl contributors *(verify exact line from package LICENSE)*

### CommunityToolkit.Mvvm
- Licence: MIT
- Project: https://github.com/CommunityToolkit/dotnet
- Used by: Throng.App
- Copyright: .NET Foundation and Contributors *(verify)*

### VT323 font (bundled in `src/Throng.App/Assets/Fonts`)
- Licence: SIL Open Font License 1.1 (full text ships as `OFL.txt` beside the font)
- Source: https://fonts.google.com/specimen/VT323
- Copyright 2011, The VT323 Project Authors (peter.hull@oikoi.com)

### Clipper2 (polygon booleans and offsetting)
- Licence: Boost Software License 1.0 (text below)
- Project: https://github.com/AngusJohnson/Clipper2 (NuGet `Clipper2`)
- Used by: Throng.Core (behind `IPolygonOps`, D-007), added in round 002; version 1.5.4
- Copyright: Angus Johnson *(verify exact line from package LICENSE)*

### PDFsharp (PDF report)
- Licence: MIT (NuGet `PDFsharp` 6.2.4 lists MIT)
- Project: https://www.pdfsharp.com (empira Software GmbH), https://github.com/empira/PDFsharp
- Used by: Throng.App (`Services/PdfReport.cs`), added in round 025 (D-401); Core build with Windows fonts
- Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany *(verify exact line from package LICENSE)*
- Its dependencies `Microsoft.Extensions.Logging.Abstractions` and `System.Security.Cryptography.Pkcs` are MIT,
  Copyright (c) .NET Foundation and Contributors. **Round 025 audit:** run
  `dotnet list src/Throng.App package --include-transitive` and record the resolved versions in the table above.

### Planned (not yet referenced)
- **ScottPlot** — MIT — charts from M5.

---

## Boost Software License 1.0 text (applies to Clipper2)

```
Boost Software License - Version 1.0 - August 17th, 2003

Permission is hereby granted, free of charge, to any person or organization
obtaining a copy of the software and accompanying documentation covered by
this license (the "Software") to use, reproduce, display, distribute,
execute, and transmit the Software, and to prepare derivative works of the
Software, and to permit third-parties to whom the Software is furnished to
do so, all subject to the following:

The copyright notices in the Software and this entire statement, including
the above license grant, this restriction and the following disclaimer,
must be included in all copies of the Software, in whole or in part, and
all derivative works of the Software, unless such copies or derivative
works are solely in the form of machine-executable object code generated by
a source language processor.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

---

## MIT License text (applies to the MIT components above)

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Test-only components (not shipped)
- **MSTest** (MSTest.TestFramework, MSTest.TestAdapter) — MIT — Microsoft
- **Microsoft.NET.Test.Sdk** — MIT — Microsoft

---

## Acknowledgements (inspiration only; no code or assets used)

These carry no licence obligation; they are credited as a courtesy.

- **Fruin, J. J.** — *Pedestrian Planning and Design* (1971). Level of Service concept and thresholds used in
  `FruinLevelOfService` (values to be verified against the source before results are shared).
- **Helbing, D. & Molnár, P.** — "Social force model for pedestrian dynamics" (1995). Basis for local movement (M2).
- **Moussaïd, M., Perozo, N., Garnier, S., Helbing, D. & Theraulaz, G.** — "The walking behaviour of pedestrian social
  groups and its impact on crowd dynamics" (2010). Idea behind the group cohesion term (round 024, D-383).
- **Weidmann, U.** — *Transporttechnik der Fussgänger* (1993). Walking-speed distributions behind the agent profile defaults.
- **Treuille, A., Cooper, S. & Popović, Z.** — "Continuum Crowds" (2006). Flow-field navigation inspiration.
- **Barney, G.** — *Elevator Traffic Handbook*. Collective control and up-peak lift dispatch concepts.
- **Steele, G., Lea, D. & Flood, C.** — SplitMix64 (2014). Algorithm used by `DeterministicRandom`.
- **Maxis** — SimCity / SimCopter / SimTower era. Visual and UX inspiration only; the app icon and UI are original.
