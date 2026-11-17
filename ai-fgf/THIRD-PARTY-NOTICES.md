# Third-party notices

The AI Forms Generation Framework uses the third-party work listed here — packages and techniques. Every entry was
checked for its licence before it was added; all are permissive (MIT, BSD-3-Clause, SIL OFL 1.1) and compatible with
the framework's own MIT licence (see `LICENSE`). Each package's full licence text ships with it via NuGet and is
available at the project URL given. Anything new is checked and listed here before it is used
(`CONTRIBUTING.md`, "Third-party work").

## Packages the framework ships with (in an app built on it)

### Vortice.Windows — the Direct3D face renderer (`AI_FGF.Direct3D`, used by `AI_FGF` and the Avalonia add-on)

- `Vortice.Direct3D12`, `Vortice.DXGI`, `Vortice.D3DCompiler`, `Vortice.DirectX`, `Vortice.Mathematics`

Used by `src/AI_FGF.Direct3D/AiFgfFaceRenderer.cs` to render the Direct3D 12 faces (buttons, fields, tabs, progress bars,
spin buttons). Copyright (c) Amer Koleci and contributors. License: MIT. https://github.com/amerkoleci/Vortice.Windows

- `SharpGen.Runtime`, `SharpGen.Runtime.COM` (used by Vortice). License: MIT. https://github.com/SharpGenTools/SharpGenTools

### Avalonia — the cross-platform front end (`AI_FGF.Avalonia`)

- `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Controls.DataGrid`,
  and what they bring: `Avalonia.Win32`, `Avalonia.X11`, `Avalonia.Native`, `Avalonia.FreeDesktop`, `Avalonia.Skia`,
  `Avalonia.Remote.Protocol`, `Avalonia.BuildServices`

Used by `src/AI_FGF.Avalonia` for the Linux/macOS front end sharing the framework's API.
Copyright (c) .NET Foundation and contributors. License: MIT. https://github.com/AvaloniaUI/Avalonia

Brought in by Avalonia:

- `SkiaSharp` and `SkiaSharp.NativeAssets.*` — License: MIT (https://github.com/mono/SkiaSharp); the native Skia
  library inside: Copyright (c) Google Inc., License: BSD-3-Clause (https://skia.org).
- `HarfBuzzSharp` and `HarfBuzzSharp.NativeAssets.*` — License: MIT (https://github.com/mono/SkiaSharp); the native
  HarfBuzz library inside: License: "Old MIT" (https://github.com/harfbuzz/harfbuzz).
- `Avalonia.Angle.Windows.Natives` — ANGLE, Copyright (c) The ANGLE Project Authors. License: BSD-3-Clause.
  https://chromium.googlesource.com/angle/angle
- The **Inter** typeface inside `Avalonia.Fonts.Inter` — Copyright (c) The Inter Project Authors, "Inter" a Reserved
  Font Name of Rasmus Andersson. License: SIL Open Font License 1.1 (bundling with software is allowed; the font may
  not be sold by itself). https://github.com/rsms/inter
- `MicroCom.Runtime` (License: MIT, https://github.com/AvaloniaUI/MicroCom), `Tmds.DBus.Protocol` (License: MIT,
  https://github.com/tmds/Tmds.DBus).

### Microsoft .NET packages

- `System.Drawing.Common`, `Microsoft.Win32.SystemEvents`, `System.Management`, `System.CodeDom`,
  `System.Configuration.ConfigurationManager`, `System.Diagnostics.EventLog`, `System.Diagnostics.PerformanceCounter`,
  `System.Security.Cryptography.ProtectedData`, `System.Security.Permissions`, `System.Windows.Extensions`

Copyright (c) .NET Foundation and contributors. License: MIT. https://github.com/dotnet/runtime

## Packages used only by the tests (not in the published archive)

- `Avalonia.Headless`, `Avalonia.Skia` — License: MIT (as Avalonia above).
- `FlaUI.Core`, `FlaUI.UIA3` — UI automation for the end-to-end tests. Copyright (c) 2016-2024 the FlaUI authors.
  License: MIT. https://github.com/FlaUI/FlaUI
- `Interop.UIAutomationClient` (used by FlaUI) — Copyright (c) 2019 Roman. License: MIT.

## Techniques and code snippets

- **Rounded-box signed distance function** — the `box()` function in the pixel shader of
  `src/AI_FGF.Direct3D/AiFgfFaceRenderer.cs` (the one shader of both front ends) follows the
  "Rounded Box - exact" 2D distance function by Inigo Quilez, https://iquilezles.org/articles/distfunctions2d.
  His site states that its code snippets are under the MIT license. Copyright (c) Inigo Quilez. License: MIT.

Everything else in the shaders — the lighting, bevel, halo, the magic glow (hue rotation by Rodrigues' formula,
shimmer, sparks) — and the rest of the framework is the framework's own work.

---

`src/AI_FGF.Core` has no third-party dependencies (BCL only).
