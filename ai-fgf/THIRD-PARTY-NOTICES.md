# Third-party notices

AI_FGF is built on the following third-party packages. Each is MIT-licensed,
consistent with AI_FGF's own license (see `LICENSE`).

## Vortice.Windows (AI_FGF, WinForms front end)

- `Vortice.Direct3D12`
- `Vortice.DXGI`
- `Vortice.D3DCompiler`

Used by `src/AI_FGF/Visual/AiFgfDirect3D.cs` to render button faces (Raised bevel,
Glow halo) with Direct3D 12.

Copyright (c) Amer Koleci and contributors.
License: MIT. https://github.com/amerkoleci/Vortice.Windows

## Avalonia (AI_FGF.Avalonia, cross-platform front end)

- `Avalonia`
- `Avalonia.Desktop`
- `Avalonia.Themes.Fluent`
- `Avalonia.Fonts.Inter`
- `Avalonia.Controls.DataGrid`

Used by `src/AI_FGF.Avalonia` for the Linux/macOS front end sharing AI_FGF's API.

Copyright (c) .NET Foundation and contributors.
License: MIT. https://github.com/AvaloniaUI/Avalonia

---

Each package's full license text ships with it via NuGet and is also available at the
project URLs above. `src/AI_FGF.Core` has no third-party dependencies (BCL only).
