# Adding code to the AI Forms Generation Framework — the rules

Every change — a fix, a component, a test — is checked against this list before it is committed. A change that
breaks a rule is not finished, however well it works.

## 1. Architecture: model, view, controller

- **Model** — data and rules, no UI types: specs (`ToolSpec`), field values and validation, run results
  (`ProcessResult`, `ExitCodePolicy`), settings. Lives in `AI_FGF.Core` (net10.0/net11.0, no WinForms, no Avalonia).
- **Controller** — what happens and in which order: validate → build the command → run → report; cancel, timeout,
  outcome wording. UI-free, in `AI_FGF.Core`, talking to the view only through an interface (`IToolRunView` and the like),
  so one controller serves WinForms and Avalonia and is tested without a window.
- **View** — controls, layout, painting, input events forwarded to the controller. `AI_FGF` (WinForms) and
  `AI_FGF.Avalonia` hold views only.
- Logic written twice, once per front end, is a defect: move it into the controller or the model.
- Adopting an existing app: `AiFgfController` (+ `[AiFgfOn]`), `AiFgfMvc.Bind` for the model — the same split.

## 2. Target frameworks

- Every project builds for `net10.0` and `net11.0` (`-windows` for WinForms) — except a new project built on .NET 11-only
  features (e.g. `tools/AI_FGF.TestRunner` on the board layout), which targets `net11.0` alone.
- **New features are .NET 11 only**: wrap them in `#if NET11_0_OR_GREATER` (and their tests too). .NET 10 keeps the
  feature set it has and must stay green.
- Fixes to existing behaviour apply to both.

## 3. Layout, DPI, themes

- Not every window is a tool runner: a free-form window (a console of groups) uses `FormScaffold.Board` (.NET 11) —
  never a hand-built root table. Tests cover random shapes of it, not one fixed form.
- Never set fonts, sizes or positions by hand: `FormScaffold`, `ParamTable` and `AutoMinSize` own the layout, at every
  DPI. Sizes that must exist are logical units passed through `LogicalToDeviceUnits`, re-applied on a DPI change.
- Colours come from `AiFgfTheme.Current` at paint time (never cached), so a live theme switch recolours everything.
  Text keeps its contrast on the background it is actually drawn on (4.5:1 nominal; small text on gradients more).
- User-visible strings go through `AiFgfText` (the Russian set must keep forms conformant too).
- Direct3D effects (`AiFgfDirect3D`) always have the flat fallback, the same size and colours; a component exposes
  `Look` (`AiFgfButtonStyle`) and `PaintedDirect3D`, and `ToolForm.ApplyButtonStyle` hands it the form's style.

## 4. Third-party work — check, then credit

Before anything not written for the framework goes in — a package, a code snippet, a formula taken from a page, a
shader technique, an icon, an image, a font, a text — check where it comes from and under which licence:

- **Allowed**: permissive licences compatible with the framework's MIT licence — MIT, BSD-2/3-Clause, Apache-2.0,
  ISC, zlib, Unlicense/CC0, and SIL OFL 1.1 for fonts.
- **Not allowed**: GPL, LGPL and AGPL code; CC BY-SA and CC BY-NC material; anything without a licence ("all rights
  reserved" is the default); assets whose origin is unclear.
- **Stack Overflow** (its answers are CC BY-SA, whose share-alike term the MIT licence cannot carry): reading an answer
  and writing your own code from the idea is fine — ideas and methods are free; copying a non-trivial snippet verbatim
  is not, unless its author also published it under a compatible licence (MIT etc., e.g. in their own repository) —
  then take it from there and credit it as above.
- **Credit it**: add an entry to `THIRD-PARTY-NOTICES.md` — what it is, where it is used, copyright holder, licence,
  link — and, for a snippet or technique, a one-line comment at the place it is used pointing there. A package
  pulled in by another package counts too (check with `dotnet list package --include-transitive`).
- A standard mathematical method written in our own words (a rotation formula, an interpolation) needs no licence;
  say in the comment that it is our own implementation if it could be mistaken for a copy.

## 5. Tests — part of the change, not after it

- Every fix comes with a test that failed before it; every component with a test of its behaviour and its pixels.
- Random combinations are seeded (the seed in the case name), so a failure reproduces.
- Controllers are tested headless with a fake view; views with `AiFgfConformance.CheckAcrossDpi` (96–192 DPI, light
  and dark) and pixel checks.
- No `Thread.Sleep` waits where a state can be set directly (`SetVisualState`, `SetSpinState`, …).
- Test forms never persist settings (`SettingsKey => null`, `ToolForm.PersistSettings = false`).
- Each form and DPI is checked once: do not repeat a check another group already makes.

## 6. Done when

- `dotnet build AI_FGF.slnx -c Debug` — 0 warnings, 0 errors.
- `pwsh tools/Run-Tests.ps1` — `RESULT: PASS` (LayoutTests and AvaloniaTests on both frameworks; never two
  LayoutTests processes at once). While working, `pwsh tools/Run-Tests.ps1 -Changed` (or "Only what changed" in the
  test window) runs just the groups the changes touch and stops at the first failure - about half a minute; `-List`
  shows which and why. It is the quick loop, not "done": a change deep inside can reach groups that do not name it.
- An intended change of the WinForms look: goldens regenerated (`AI_FGF_UPDATE_GOLDEN=1`), Avalonia parity holds.
- README (and the agent guide, if the way to use the framework changed) updated, in English.
- Public files name no private machine, server, path or unrelated product; every third-party package, snippet or
  asset is checked and listed in `THIRD-PARTY-NOTICES.md` (section 4).
- Generated forms (from specs, in code, random ones in tests) follow "Generated forms must be original" in
  `AGENT_GUIDE.md`: the framework's own layout and style, no third-party assets, own wording.
