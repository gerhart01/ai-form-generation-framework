# AI_FGF - AI Form Generation Framework

AI_FGF is a lightweight .NET 10 / .NET 11 framework for building form-based desktop tools fast: a
parameters panel, Run/Cancel, a streamed log and a results grid, as a short subclass instead of a
hand-built form. It is pure programmatic WinForms (no XAML, no designer), with an Avalonia front
end that runs the same `tool.json` specs on Linux and macOS. Buttons and input fields render with
real Direct3D 12 (raised bevel, glow halo, animated hover/press, perspective depth), falling back
to flat automatically when it is unavailable. Integrate it into an existing app one form at a time,
or start a new tool from a declarative spec or a short code subclass - either way, minutes, not days.

It is distilled from a family of hand-coded tool GUIs. Those apps
independently converged on the same shape — a *parameters panel → Run/Cancel → streamed
log → results grid* "tool runner" — and each re-implemented the same button styling,
thread-safe logging, process streaming and DPI handling. AI_FGF lifts that shared
code into one reusable library so a new tool window is a short subclass instead of a
thousand-line form.

> No XAML, no designer, no external packages — pure programmatic WinForms, the style
> every source project used. **On Linux** (and macOS) the same `tool.json` forms run on an
> Avalonia front end that looks like the Windows one — see [Linux](#linux).

## Projects

| Path | What it is |
|------|------------|
| `src/AI_FGF.Core` | Platform-neutral core (`net10.0` and `net11.0`): `ToolSpec`, `ProcessRunner`, exit codes, CSV, colour palette. |
| `src/AI_FGF` | The WinForms framework (class library, `net10.0-windows` and `net11.0-windows`), on the core. |
| `src/AI_FGF.Avalonia` | The cross-platform front end (`net10.0` and `net11.0`, Avalonia 11.3): the same API as `src/AI_FGF` (`ToolForm`, `FormScaffold`, `FieldSet`, `SpecToolForm`…). |
| `samples/AI_FGF.Demo.Avalonia` | The WinForms demo's own `PingToolForm.cs`, compiled unchanged against Avalonia. |
| `tests/Shared` | Sources both harnesses compile: the stress-form catalog, golden WinForms snapshots (`Golden/`). |
| `schemas/tool.schema.json` | JSON Schema of `tool.json`, generated from `ToolSpec` (editor completion, typo checks). |
| [`../AGENT_GUIDE.md`](../AGENT_GUIDE.md) (next to this folder) | Start here. The manifest: which path for a new form / a migrated form / code / Linux, and when it is done. |
| `samples/AI_FGF.Linux` | The template for Linux: `AI_FGF.Linux tool.json`. |
| `tests/AI_FGF.AvaloniaTests` | Head-less Avalonia tests (Skia rendering, no display needed). |
| `samples/AI_FGF.Demo` | A working `ping` tool built on it in ~40 lines. |
| `samples/AI_FGF.Template` | `tool.json` — the template with every element; the exe shows any spec. |
| `tools/AI_FGF.Check` | `AI_FGF.Check spec.json` — validates a spec and checks its form at 4 DPIs; `draft` turns an existing form into a spec, `compare` diffs two layout snapshots, `--schema` writes the schema; exit 0 = PASS. |
| `tests/AI_FGF.LayoutTests` | Headless DPI / resize layout tests. |

```
dotnet build AI_FGF.slnx -c Debug
dotnet run --project samples/AI_FGF.Demo -f net11.0-windows            # or -f net10.0-windows
dotnet run --project tests/AI_FGF.LayoutTests -f net11.0-windows   # exit code 0 = all layout checks pass
```

## Linux — Avalonia, the same API

WinForms runs only on Windows; on Linux there is .NET (not .NET Framework), and a UI needs a
cross-platform toolkit. `AI_FGF.Avalonia` is AI_FGF on **Avalonia** with the **same classes and
members** as the WinForms library — `ToolForm`, `FormScaffold` (+ `RowBuilder`/`BarBuilder`), `FieldSet`,
`AiFgf`, `AiFgfLog`, `AiFgfDataGridPanel`, `AiFgfDialog`, `AiFgfThemeDialog`, `AiFgfTheme`, `AiFgfSettings`, `AiFgfApp`,
`SpecToolForm` — over the shared `AI_FGF.Core`. A tool written against one compiles against the other;
only which AI_FGF the project references changes:

```xml
<!-- the Avalonia project of samples/AI_FGF.Demo.Avalonia: the demo's own files, unchanged -->
<Using Include="AI_FGF.Avalonia" />
<Compile Include="..\AI_FGF.Demo\PingToolForm.cs" Link="PingToolForm.cs" />
<Compile Include="..\AI_FGF.Demo\Program.cs" Link="Program.cs" />
```

That demo really is one file for both: `PingToolForm.cs` (rows, rule, buttons, `BuildRun`, results)
builds the WinForms demo and the Avalonia one, and both test harnesses assert the same keys, defaults,
validation, buttons and command line for it. Any `tool.json` runs as `SpecToolForm` on either:

```
dotnet publish samples/AI_FGF.Linux -c Release -f net11.0 -r linux-x64   # framework-dependent: needs .NET 11 on the box (-f net10.0: .NET 10)
./AI_FGF.Linux my-tool.json                                     # or tool.json next to the program
```

It looks like the Windows version: the shared palette (`AiFgfPalette`, with its contrast guarantees), Segoe UI
9.25 pt (Inter where it is missing), the "Parameters" frame with the caption on its line, flat bordered 96x32
buttons with the accent Run, 18-px progress bar, Output/Results tabs, find bar, status bar, invalid fields
tinted, labels of disabled rows dimmed. Rows are responsive (side by side / stacked, hysteresis), memos never
wrap, settings and presets use the same files (a preset saved on Windows loads on Linux).

**Identical look — measured, not eyeballed.** `AiFgfSnapshot.Of(form)` (WinForms) and `AiFgfSnapshot.Of(window)`
(Avalonia) record every visible label, button, field, tab, log and status text with its logical bounds and
the font actually rendered (`AiFgfLayoutSnapshot`). The parity tests compare the Avalonia window of the demo
with a golden WinForms snapshot (`tests/Shared/Golden/ping.winforms.json`): every element within 3 px, same
font family, size and weight. What the comparison found and `WinFormsMetrics` now reproduces: tab headers
were Segoe UI *Semilight* 10 pt (WinForms: the form's 9.25 pt), the log used Cascadia Mono instead of
Consolas, WinForms sizes labels, buttons and tabs with TextRenderer's text padding (4 px a side at 9.25 pt),
its controls carry margins (labels 3,3,8,3; inputs 3,5,3,3), edit boxes are 22.4 px tall with a
`WindowFrame` (#646464) border that focus does not change, check boxes are 20 px with a 13-px glyph,
spinners have stacked arrows, and GDI lays Consolas on whole pixels (9 px at 125 % where Skia used 8.7 — a
log line was 3 % narrower; `AiFgfLog` sets the letter spacing that gives the same pitch at any scale). Text
widths themselves agree within 2 % (measured with TextRenderer and Skia), and both render sub-pixel text.

Differences, where the toolkits differ: controls are Avalonia's (`out TextBox` is an Avalonia `TextBox`;
`Watermark` instead of `PlaceholderText`, except on `AiFgfMemo`), `RunShortcut`/`CancelShortcut` are
`KeyGesture`s, there is no Mica backdrop, and pickers/dialogs are asynchronous in Avalonia — the WinForms-style
synchronous `AiFgf.PickOpenFile`, `AiFgfDialog.Ask` and `AiFgfThemeDialog.Show` run a nested dispatcher loop, and
`…Async` variants exist. On Linux the process runner uses UTF-8, kills the tree with
`Process.Kill(entireProcessTree)` and finds `python3`/`pwsh` without extensions.

Checked here (a Windows host without WSL) by building for `linux-x64` and by the head-less tests, which lay
out and render with the same Skia/HarfBuzz engine Avalonia uses on Linux; the window has not been shown on a
real Linux desktop yet.

## Stress layouts: many controls, every text, every font

`tests/Shared/StressCatalog.cs` describes stress forms once, against the common API, and **both** harnesses
build and check them: 40 buttons with captions from 1 to 90 characters, labels up to 300 characters, 60 rows of
every kind, 20 drop-downs of 50 items (some 120 characters long), 60 check boxes, Russian/Japanese/Chinese/Greek
text, mnemonics (`&`, `&&`), and a mixed form with conditions — each in Segoe UI 7 / 9.25 / 14 pt, Consolas 10,
Times New Roman 12 and Arial 20 pt. WinForms: `AiFgfConformance` at 96/144/192 DPI for the UI font and at 96 DPI
for the others (`STRESS_FULL=1`: all at three DPIs) — no overlap, nothing clipped or sticking out of its container,
every caption rendered readable, the minimum size fits the smallest screen — plus an opened drop-down shows its
longest item. Avalonia: the same forms at three widths, including the opened drop-downs.

What they found, now fixed: labels and button/check-box captions longer than the screen (they wrap beyond
`MaxLabelEm` = 22 em / `MaxTextEm` = 25 em and grow in height); a bar of 40 buttons taller than the screen (the bar
is capped and scrolls when the window is too short, every button reachable); drop-down lists narrower than their
items (`AiFgf.FitDropDownWidth`); a find bar whose button stuck out after its box shrank; and, with 60 rows, an
endless stack/unstack loop on a transient 0-px table width, one table relayout per added row and per child window
created (O(rows²) — now one), and one relayout per memo (now batched). On Avalonia: a body floor that scales with
the font, and a find bar laid out in one pass.

**Random forms.** `tests/Shared/RandomForms.cs` adds what a fixed catalog misses: combinations. Each form draws
1–25 rows from every kind — text, password, memo, file, folder, combo, choice, number, check boxes, radio buttons,
slider, a note (a label as the input), a text box with a button of its own — with random captions from the stress
texts, `required` marks, rows shown or enabled by an earlier check box, 0–12 buttons (sometimes a primary one) and
one of the six fonts; every field also gets a random value that must read back unchanged. 40 forms run by default,
from a fixed seed so a run is repeatable; `AI_FGF_RANDOM_SEED` and `AI_FGF_RANDOM_COUNT` explore others, and a failure names
the seed that rebuilds its form (`AI_FGF_RANDOM_SEED=<seed> AI_FGF_RANDOM_COUNT=1`, `AI_FGF_RANDOM_VERBOSE=1` for every issue).
WinForms checks them with `AiFgfConformance` (spacing included) at 96 DPI and one more DPI the seed picks; Avalonia at
three widths, readable, and with the spacing check at x1 and one more text scale.

What they found, now fixed: after a trip to the minimum size, the parameters took the room of a button bar that was
about to grow back, and the log was squeezed to 0 px; a table stacked by its hysteresis was predicted side by side
(half its height — again the log paid); a label beside wrapping radio buttons sat between two lines; a note and a
slider's value sat 3–8 px off their row's centre line, and a Browse… button stayed at the top of a row a wrapped
label made taller; a long note widened the table past the window instead of wrapping; a check-box or radio caption
capped at 25 em stuck out of a narrower row (captions now wrap at the row's width); a 25-em button beside a field
made the minimum width larger than the screen (such buttons wrap beyond `ParamTable.MaxTrailingEm` = 12 em). On
Avalonia: Fluent's 32-px radio buttons (now as tall as their caption, a 13-px glyph, like the check boxes) and a
slider without margins touching the rows around it. The rendered-text check allows a little less for an enabled
caption in passing colours: ClearType draws "iiii" at 7 pt without one full-strength pixel.

## Layout & DPI tests

WinForms most often goes wrong on **DPI scaling** and **buttons overlapping when a form
is resized**. `tests/AI_FGF.LayoutTests` is a headless harness that guards both. It
builds real forms off-screen (no window shown, no message loop), simulates DPI with
`Control.Scale` — the same call WinForms makes on a real DPI change — across
`1.0× / 1.25× / 1.5× / 1.75× / 2.0×`, and asserts, at a range of window widths:

- **no two buttons ever overlap**, at any scale or width;
- the button bar **wraps to a new row instead of clipping** or running off the edge;
- **no sibling controls overlap anywhere** in a full `ToolForm`, at every size at or
  above the (DPI-scaled) minimum;
- the **parameters section grows with DPI and never clips its rows**;
- the **progress strip scales** and its bar stays inside its panel.

### Button-placement matrix

Every way a form can arrange buttons is built and driven to boundary sizes, then re-checked
at `1.0× / 1.25× / 1.5× / 1.75× / 2.0×` DPI. Strategies covered:

- `FlowLayoutPanel` wrapping — LeftToRight, RightToLeft and TopDown;
- `FlowLayoutPanel` **non-wrapping** (single row);
- `Dock=Left` toolbar row and `Dock=Top` stack;
- elastic `TableLayoutPanel` grid;
- **anchored** buttons (left-anchored vs right-anchored).

Button counts are boundary values `{1, 2, 7, 20}`; container extents are
`half-the-minimum, below-one-button, just-above-minimum, mid, natural, far-too-large`. The
container is sized directly (bypassing the OS minimum window width) so each extent is exact.
For every case the test computes the physical threshold — the smallest extent at which the
buttons can coexist given their own `MinimumSize` — and asserts:

- **at or above the threshold:** no overlaps, no clipping;
- **below it:** the layout degrades predictably (clips/overlaps) — proving the threshold is
  tight and showing where a form's `MinimumSize` must sit.

The anchor test pins the exact **collision width** per DPI (e.g. ~328 px at 1.0×, ~368 px at
2.0×) and verifies the layout is clean just above it and collides just below — the number you
put a `MinimumSize` at. One screenshot per strategy is saved (`placement_*.png`).

This matrix alone contributes ~1300 checks across the DPI scales.

**Threshold report.** The run also writes `artifacts/placement_thresholds.{md,csv}` — a
"minimum size by strategy, button count and DPI" table measured at 100% and scaled
linearly (how per-monitor DPI actually scales a form), so the numbers drop straight into a
form's `MinimumSize`. For example, a 7-button wrapping bar needs ≥ 199 px wide at 100% /
398 px at 200%; the same seven buttons in a single non-wrapping row need ≥ 917 px / 1834 px;
an anchored OK-Cancel-Help toolbar collides below 328 px / 656 px.

### Screenshot (visual) tests

Geometry only sees bounding boxes. The harness also **renders each form to a bitmap**
(`Control.DrawToBitmap`) at `1.0× / 1.25× / 1.5× / 2.0×` and inspects it at the pixel
level, using a high-contrast theme so pixels classify cleanly as panel-background vs
button:

- each button face is **actually painted** (the panel doesn't show through it);
- each neutral button's **label is really rendered** (dark glyph pixels on the face);
- every pair of adjacent buttons is **separated by a visible strip of background** —
  the pixel-level proof that they don't touch or overlap;
- the whole `ToolForm` renders **non-blank and grows with DPI**.

Every capture is saved to `artifacts/screenshots/` (`buttonbar_*.png`, `toolform_*.png`)
so DPI behaviour can be eyeballed, not just asserted.

### Bugs this testing caught

It currently runs **~2680 checks** (geometry + placement matrix + `AutoMinSize` + DWM / overlay /
dynamic buttons + responsive rows, fields, settings and presets), including checks that at the
`AutoMinSize`-guarded minimum the bar wraps, the rows stack and every visible button is inside
the window, on every tab, at every DPI. Writing the tests found these real bugs, all fixed:

1. **Progress strip overlap** — the strip docked its `Fill` label and `Bottom` bar in the
   wrong order, so they overlapped at *every* scale (geometry test caught it).
2. **Log & results not filling their tab** — they were added to the tab pages without
   `Dock = Fill`, so they stayed at the default `UserControl` 150×150 and floated in the
   top-left. No control *overlapped*, so geometry passed — the **2× screenshot** showed a
   tiny dark log box in a huge empty tab. Fixed, and now also guarded by a deterministic
   "composite fills ≥ 80 % of its tab" check.
3. **`AutoMinSize` ignored wrapping** — it took each docked panel's `PreferredSize.Width`, which
   for a wrapping bar is its *one-row* width, so the minimum was ~1100 px and the bar never
   wrapped; and it measured heights at the current width, not at the minimum. Now width uses
   the wrapped/stacked minimum (≈500 px) and height is computed at that width.
4. **Clipped tool strips** — the log's find bar and the grid's filter bar had fixed heights,
   so on a narrow window their buttons wrapped into an invisible second row. Now they grow.
5. **Unpredictable row heights** — `TableLayoutPanel` measures non-`AutoSize` children by their
   *specified* size (a `Label`'s default 100×23) and an `AutoSize` flow panel against an
   unreliable width. Labels are now `AutoSize` + anchored, and wrapping rows are sized by the
   same function that predicts them, so predicted and real heights agree at every DPI.
6. **Log flooding under chatty processes** — every streamed line cost its own `BeginInvoke`,
   `AppendText` and `ScrollToCaret`, so a tool printing thousands of lines a second could
   swamp the UI message queue, and the log grew without limit. Lines are now queued and
   written in coalesced batches (20 000 background lines → 1 UI flush, ~0.1 s in the test),
   order and colours preserved, oldest lines dropped past `MaxLines` (default 50 000).
   A follow-up caught that *UI-thread* appends were still written one by one — O(n²) as the
   text grew, which surfaced as an intermittent multi-minute "hang" of the harness. Every
   thread now queues; reading `Lines`/`LineCount` on the UI thread writes what is queued.
7. **Run lifecycle** (found by the multi-agent bug hunt): a close
   vetoed by a `FormClosing` handler used to leave the window frozen with its run cancelled;
   an exception from `BuildRun` vanished into the discarded `RunAsync` task; closing during a
   run deferred the process-tree kill to a thread-pool continuation that never runs once the
   app exits (orphaned tools); a disposed busy overlay was reused. Teardown now happens in
   `OnFormClosed`, `RunAsync` reports every exception, `ProcessRunner` kills the tree
   synchronously inside `Cancel()` (and exposes `ProcessId`), and the overlay refuses user
   closes and is recreated if needed. The tree-kill test occupies every thread-pool worker
   while cancelling, so a deferred kill fails it deterministically.

8. **`ProcessRunner` robustness** (bug hunt, group I2): output was always decoded as UTF-8,
   garbling `ping`/`cmd`/native tools that write in the console OEM code page (CP866 on a
   Russian Windows) — the default is now `GetOEMCP()`, with `Utf8()`/`WithEncoding(...)` for
   UTF-8 tools. Cancel killed the tree only by walking parent ids and only if the direct child
   was alive, so a grandchild whose parent and grandparent had exited survived (and kept the
   run "running" through its inherited pipe) — the tool now runs inside a Win32 **job object**
   that cancel terminates. Every line used to be kept in memory — `CaptureLines(n)` keeps the
   last *n* (default 100 000; 0 = stream only) and `ProcessResult.Truncated` says so. Results
   are snapshots and no `Output` event fires after `RunAsync` returns. The tests drive the
   harness exe itself as a scripted child (`--child emit-hex|spam|flood|chain`).

9. **CSV** (bug hunt, group I3): `ReadCsv` split records at line breaks inside quoted fields,
   so files written by `WriteCsv` did not round-trip; duplicate header names threw and cells
   beyond the header were silently dropped; an export to a file open in Excel crashed the
   app; and exported cells such as `=HYPERLINK(...)` were live formulas. `AiFgfCsv` now parses
   the whole stream as a state machine (CRLF/LF/CR, BOM), de-duplicates headers
   (`Name_2`…), grows `ColumnN` for wide rows, writes atomically, and prefixes formula-like
   cells with `'` (on by default). `AiFgfDataGridPanel.ExportTo(path)` reports failures in the
   bar instead of throwing.

10. **Settings, presets, fields** (bug hunt, group I4): a settings file with keys differing only
    by case (`Port`/`port`) made `Load` throw despite "never throws"; a relative `SettingsPath`
    crashed on close (`CreateDirectory("")`); a failed preset save crashed the click handler
    and left a `.tmp`; a window maximized, then minimized, then closed came back un-maximized
    (`GetWindowPlacement` now supplies the restore state); a `Number` row restored `2.75` while
    showing `3`; and a fixed validation error reset the control to the theme colour instead of
    its own, or never cleared on control types without a specific hook. `ToolForm` gains
    `SavePresetTo`/`LoadPresetFrom` for presets without dialogs.

11. **Real DPI changes** (bug hunt, group I5). `RealDpiTests` sends forms a genuine
    `WM_DPICHANGED`, so WinForms runs the same per-monitor rescale as dragging a window to
    another monitor — closer to reality than `Control.Scale`. Fixed: the results grid kept
    fixed 26/34 px row/header heights (clipped text at 200 %) — they now follow the font and are
    refitted on `FontChanged`; a first-run window larger than the working area overflowed the
    screen — it is now shrunk to fit. Hardened: `ParamTable` only re-decides its mode on a
    *width* change (or after the rescale has finished), so a font that arrives before the
    width can't flip the layout twice; `AutoMinSize` re-measures after the rescale. Neither of
    those two reproduced on .NET 10's real `WM_DPICHANGED` path, but
    both orderings are now covered.

12. **Log while streaming** (bug hunt, group I6): every flush moved the caret to the end and
    scrolled down, so while a tool was printing the user could neither keep a find match
    nor read earlier output. The log now follows the tail only when the user is at the bottom
    with nothing selected; otherwise it keeps the selection and the first visible line
    (shifted by lines trimmed past `MaxLines`). Also, background lines were marshalled through
    the inner `RichTextBox`, whose handle appears after the control's — a line queued in that
    gap was never written; marshalling now goes through `AiFgfLog` itself.

13. **DWM and fonts** (bug hunt, group I7): `Dwm.Apply(rounded: false)` sent "system default"
    (rounded on Windows 11) instead of "do not round"; the attributes were applied once, so a
    window whose handle was re-created (e.g. `ShowInTaskbar` toggled) lost its Mica/dark title
    bar — they are now kept per form and re-applied on every `HandleCreated`, with
    `Dwm.LastResult(form)` exposing the HRESULTs. The primary (Run) button froze a copy of the
    default font, so a form-wide font change or a theme family never reached it; its bold font
    now follows its parent's font (and is disposed when replaced).

14. **Harness gaps** (bug hunt, group I8 — bugs in the tests themselves): a "minimum shrinks
    after removing a button" check accepted no shrink; overlap checks skipped every
    non-selected TabPage (the Results grid was never checked inside a `ToolForm`); collapsed
    buttons were silently skipped and the primary button's label was never checked; and no
    test ran a complete `ToolForm` run. All fixed — each fix was proven by a mutation the old
    test missed and the new one catches.

The harness also has a watchdog: a case that makes no progress for 120 s is reported as
`HUNG` with its name and the run exits non-zero instead of blocking CI forever.

The harness returns a non-zero exit code on any failure, so it drops straight into CI.

## What you get

| Piece | Replaces the hand-written… | 
|-------|----------------------------|
| `AiFgfTheme` | per-form `ClrApp/ClrSurface/ClrText/ClrAccent…` palette constants |
| `AiFgf` (static factory) | `MakeButton` / `MakeSmallButton` / `StylePrimary` / `MakeText` + browse dialogs |
| `FormScaffold` | the `GroupBox + TableLayoutPanel` rows, wrapping `FlowLayoutPanel` button bar, dock z-ordering |
| `AiFgfLog` | the dark `RichTextBox` console with thread-safe coloured `AppendLine` + find bar; background lines are **batched** (one UI flush per burst, redraw off while writing) and capped at `MaxLines` |
| `AiFgfDataGridPanel` | the styled `DataGridView` Results tab with live filter + CSV import/export |
| `ProcessRunner` | the `Process` + `BeginOutputReadLine` + `CancellationTokenSource` streaming engine |
| `ToolForm` | the whole main form: parameters, Run/Cancel, progress strip, Output/Results tabs, status bar |
| `AiFgfApp` | the identical `Main` body (`ApplicationConfiguration.Initialize(); Application.Run(...)`) |
| `AutoMinSize` | computes & applies the form's `MinimumSize` so buttons never overlap; re-fits on DPI change **and when buttons are added/removed** |
| `ParamTable` | responsive parameter rows: label beside the input when wide, **stacked above it when narrow** (with hysteresis) |
| `FieldSet` | every input registered by key: read/write all values, `required` fields and rules, non-modal invalid tint |
| `AiFgfSettings` | JSON persistence of window placement (DPI-rescaled, pulled back on-screen) and field values; presets |
| `Dwm` | Windows 11 backdrop (Mica/Acrylic), dark title bar and rounded corners — safe no-op pre-Win11 |
| `AiFgfBusyOverlay` | translucent "busy" scrim that dims and tracks the owner during a long operation |

## The fastest path: a tool template (`tool.json`) → a correct form

`samples/AI_FGF.Template/tool.json` is a template that uses **every** element of a tool
form: file/folder pickers, text with a regex rule, a drop-down choice, a number, flags,
a positional argument, a required field, a timeout, presets and a results CSV. To get a form
for a tool, describe the tool in JSON instead of writing UI code:

1. Copy `tool.json`; set `title`, `executable`, `fixedArgs`.
2. For each option the tool takes, keep/edit/delete one field (`kind`, `label`, `arg`, …).
3. Run the checker until it says PASS:

```
dotnet build AI_FGF.slnx -c Debug
tools\AI_FGF.Check\bin\Debug\net11.0-windows\AI_FGF.Check.exe my-tool.json
```

```
Spec      C:\…\my-tool.json
Title     Mirror copy
Command   robocopy /MIR /R:3 C:\src D:\dst
Layout    checked at 96, 120, 144, 192 DPI
RESULT: PASS
```

4. Show it: `AI_FGF.Template.exe my-tool.json`, or in code
   `Application.Run(new SpecToolForm(ToolSpec.FromFile("my-tool.json")))`.

The template starts with `"$schema": "../../schemas/tool.schema.json"`: VS Code, Visual Studio and Rider then
complete property names, show each one's documentation and flag misspelt properties, wrong kinds and fields
without `key`/`label` as you type. The schema is generated from the `ToolSpec` classes and their XML docs
(`AI_FGF.Check --schema schemas/tool.schema.json`); a test fails if the checked-in copy is out of date or
the template uses a property the schema does not know. For agents: `../AGENT_GUIDE.md` (next to this folder) is the one-page
manifest of the paths (spec, migration, code, Linux) and their "done when".

### Replacing an existing form

```
AI_FGF.Check draft MyApp.exe MyApp.BackupForm -o backup.json
```

creates the existing form (Designer-made or hand-coded), reads it into a draft spec — each label paired with
its input (left of it, or above it), in reading order: text boxes (a *Browse…* button beside one makes it a
`file` field, or `folder` when the caption says so), multi-line boxes → `memo`, spinners → `number` with
range and decimals, drop-downs → `choice` with items and editability, check boxes → `flag`, current values →
defaults, an OK/Run button → `runButton` — and writes the old form's snapshot next to it. It then builds the
new form and lists every caption or input of the old one that the new one lacks
(`AiFgfSnapshotTolerance.ContentOnly`: layout and fonts aside). What a form cannot tell — the executable, each
field's `arg` — is printed as "to do"; fill it in and run `AI_FGF.Check backup.json`. In code:
`AiFgfMigration.DraftSpec(form)`, `AiFgfSnapshot.Of(form)`, `snapshot.Compare(reference, tolerance)`.

"Correct" is checked, not assumed. `ToolSpec.Validate()` rejects specs that would produce a
wrong form or command line: duplicate keys, a choice without choices, a default outside its
range or list, a broken regex, `{Key}` placeholders naming no field, a flag marked positional,
and more. It also warns about fields that never reach the tool. `AiFgfConformance` then builds the
form and moves it to each DPI with a real `WM_DPICHANGED`. At the minimum, initial and wide
sizes, on every tab, it reports overlapping controls, buttons outside the window, clipped
labels, collapsed controls and a missing `MinimumSize`. The tests prove the checker isn't
vacuous: a deliberately broken form must produce each of those findings. They also run a
generated form against a child process and compare the arguments it actually received.

**Spacing is checked as drawn.** `AiFgfConformance.CheckSpacing` (part of `Check`) looks at what the
user sees — screen rectangles clipped by every container — and reports: two items overlapping across
containers; neighbours in a row closer than 4 px (2 px beside a label) or in a column closer than 2 px;
a label, input, button or slider thumb not centred on its row neighbour within 2 px (a caption wrapped
onto more lines may line up at the top instead); uneven gaps in a button row; an item within 3 px of
the window's left, top or right edge. Limits are logical pixels, scaled to the DPI. `SpacingTests` runs it
on every tab of the demo, the template, the test form, the dialogs and the whole stress catalog in all
six fonts, at the initial and minimum size, at 100–200 %. What it found and the framework now handles:
the log's find bar and the results filter put the box 8 px above the buttons' centre at 150 % (fixed
top margins only matched 100 %) — `AiFgf.CentreRows` centres a tool bar on its tallest control and scales
its side margins (a flow panel does not, and a nested panel's `DeviceDpi` lags its window's), so buttons
6 px apart at 100 % were still 6 px apart at 200 %; a drop-down that re-measured itself after a DPI or font
change hung 3 px over the check box below it (the table sized its row from the combo's stale specified
height); the theme dialog's colour swatches touched and its tint slider sat 13 px above its label (the
thumb fills only the top 26 px of an auto-sized track bar — `AiFgf.FitTrackBarToThumb`).

On Avalonia the same rules live in `AI_FGF.Avalonia.AiFgfConformance.CheckSpacing(Window)` (in logical
pixels, as Avalonia lays out), and the Avalonia `SpacingTests` run it on the same forms at text scales
x1–x2 (the Linux desktop's text scaling). It found: the log's find bar kept the WinForms top margins, so
the box sat 5 px above the buttons once they grew with the font — the bar now centres each row on its
tallest control, as `AiFgf.CentreRows` does; the parameters caption kept a fixed band and covered the first
row at x2 — the frame's top line and padding follow the caption's font; in a narrow (stacked) table a
file box and its Browse… button were top-aligned rather than centred.

**Text is always visible — and that is checked.** For every label, button, checkbox, group
caption and input, `AiFgfConformance` also verifies that:
- the text fits its control (width and height) and the control lies inside every container and the
  window (a control in a scrolling area only needs to be reachable by scrolling);
- multi-line text boxes do not wrap (long lines scroll sideways), memos show their `lines` and a
  log shows at least 3 lines;
- the text colour contrasts with its background by at least 4.5:1 (WCAG AA), and — on the rendered
  pixels — every caption, disabled ones included, is actually drawn at 3:1 or better;
- the minimum size fits the smallest screen Windows offers at any scale: 1024x528 logical
  (1280x720 at 125 % minus the taskbar).

The tests run this for the template, a 20-field form, a dialog and the reference tool form, in the
light and dark themes, at 100–300 % with a real `WM_DPICHANGED`, check every resolution/scale pair
from 1280x720 to 3840x2160, and every theme colour pair. Things it found and the framework now
handles: a disabled `Label` (2.4:1) and a disabled flat button such as an idle Cancel (1.4:1) were
near invisible — rows now dim their label to the theme's Subtle colour and `AiFgfButton` draws a
readable disabled caption; forms with many fields or buttons were taller than a 720p/768p screen
— `AutoMinSize` now widens the minimum (fewer bar rows, side-by-side fields) and, if that is not
enough, lets the parameters scroll; the find/filter boxes now shrink so their bar stays on one row.

HDR: Windows composites a WinForms (SDR) window into an HDR desktop at the "SDR content
brightness" — layout, DPI and fonts are unaffected, only brightness and colour mapping change. What
keeps text readable there is contrast, so it is enforced with margin (4.5:1 for every theme pair;
the log accent colour was raised from 4.3:1 to 6:1). An HDR display itself cannot be emulated in a
headless test, so HDR is covered through contrast, not through a capture on an HDR screen.

Example: *"Mirror a folder with robocopy: source and destination folders (required), retry
count 0–10 (default 3), optional log file, an option to only list."*

```jsonc
{
  "title": "Mirror copy", "executable": "robocopy", "fixedArgs": [ "/MIR" ],
  "fields": [
    { "key": "Retries", "label": "Retries",  "kind": "number", "min": 0, "max": 10, "default": 3, "arg": "/R:{value}" },
    { "key": "Log",     "label": "Log file", "kind": "file",   "filter": "Logs|*.log|All files|*.*", "arg": "/LOG:{value}" },
    { "key": "List",    "label": "Only list (don't copy)", "kind": "flag", "arg": "/L" },
    { "key": "Source",  "label": "Source",      "kind": "folder", "required": true, "positional": true },
    { "key": "Dest",    "label": "Destination", "kind": "folder", "required": true, "positional": true }
  ]
}
```

| Field property | Meaning |
|---|---|
| `key`, `label` | stable key (settings, presets, `{Key}`), caption — both required |
| `kind` | `text` · `file` · `folder` · `choice` · `number` · `flag` |
| `required`, `default`, `placeholder` | `default` may be a string, number or `true`/`false` |
| `choices`, `editable` | for `choice` |
| `min`, `max`, `decimals` | for `number` |
| `filter` | for `file`: `"Description|*.ext|…"` |
| `pattern`, `patternMessage` | regex the value must match (empty values are allowed unless `required`) |
| `arg` | `"-o"` → `-o value`; `"--x={value}"` → one token; for a `flag`, passed when checked |
| `positional` | pass the bare value after all options, in field order |
| `enabledWhen`, `visibleWhen` | `"Key"`, `"!Key"`, `"Key=value"`, `"Key!=value"` — an inactive field is disabled or hidden (no gap), not validated and **not passed**; conditions apply transitively |

Exit codes: `"successCodes": "0-7"`, `"warningCodes": { "3010": "Reboot required" }`, `"failureMessages": { "16": "…" }` —
a warning is shown in orange as *Finished with warnings* with its message, not as an error.

Scripts: `"interpreter": "pwsh"` (or `"python"`) with `"script": "tool.ps1"` (relative to the JSON file) —
the field arguments become the script's arguments.

Spec-level: `title`, `executable`, `fixedArgs`, `workingDirectory` and `resultsCsv` (both accept
`{Key}`), `encoding` (`oem`/`utf8`), `timeoutSeconds`, `runButton`, `presets`, `busyOverlay`,
`settingsKey` (`""` = don't persist). The JSON may contain comments and trailing commas.

## The flexible path: subclass `ToolForm`

A complete, cancellable, DPI-aware tool window — parameters on top, streamed log, a
results grid, progress and a status bar — is two methods:

```csharp
public sealed class PingToolForm : ToolForm
{
    protected override string RunButtonText => "▶ &Ping";
    protected override bool PresetButtons => true;

    protected override void BuildParameters(FormScaffold.RowBuilder rows)
    {
        rows.Text("Host:", "e.g. 8.8.8.8", value: "127.0.0.1", required: true);
        rows.Number("Count:", min: 1, max: 100, value: 4);
        rows.Checks(("Resolve", AiFgf.Check("Resolve addresses (-a)")));
        rows.Fields.Rule("Host", v => v.Any(char.IsWhiteSpace) ? "Host must be a single name." : null);
    }

    // Only called once every field validates.
    protected override ProcessRunner? BuildRun()
        => new ProcessRunner("ping")
            .Option("-n", Fields.Int("Count"))
            .ArgIf(Fields.Bool("Resolve"), "-a")
            .Arg(Fields.Text("Host"));
}
```

No out-fields needed: rows are read back by key with `Fields.Text` (trimmed), `Int`, `Decimal`, `Bool` and
`Get<TEnum>` — culture-independent, throwing a `FormatException` that names the field. Fixed choices:
`rows.Choice("Mode:", new[] { "fast", "full" }, selected: "fast")` or `rows.Choice("Verbosity:", Verbosity.Normal)`
(enum names); a choice is never left at "no selection", and a saved value no longer offered keeps the default.
`ProcessRunner.Option(name, int|decimal)` formats invariantly (`1234.5`, not `1234,5` under de-DE).
A number typed into a text field is read the user's way too (`AiFgfNumbers.TryParse`): `1,5` is one and a half
under ru-RU/de-DE — never 15 — while `1.5` and `1,234.5` work everywhere. A number field reads as it is shown
(`"3.0"` with one decimal, however the value was entered), and a spec condition `Ratio=1.5` matches `1.50`.
The `out` overloads remain for when you need the control itself.

```csharp
[STAThread]
static void Main() => AiFgfApp.Run(() => new PingToolForm(), AiFgfTheme.Light);
```

Rows can depend on other fields: `rows.FileText("Report:", out _).EnableWhen(f => f.GetValue("WriteReport") == "true")`
or `.VisibleWhen(...)`. A disabled or hidden row is skipped by validation and by `Fields.GetActiveValues()`;
a hidden row takes no space, and the height prediction and `AutoMinSize` follow it.

Rows take `required: true` (shown as `Label *`) and an optional `key`; extra checks go through
`rows.Fields.Rule("Host", v => …)`. Validation runs before `BuildRun` and is non-modal: invalid
fields are tinted (check boxes too: a rule like "accept the terms" is never silent), the messages go to the
log and status bar, and the first one gets focus.
`rows.Number(...)` adds a numeric spinner. A few fixed choices can all be shown at once —
`rows.Radios("Mode:", new[] { "quick", "full" }, selected: "full")` (radio buttons that wrap like a row of check
boxes; read back like a choice: the selected item, written exactly as offered, `&` included). A whole number on a
slider: `rows.Slider("Level:", min: 0, max: 100, value: 40, unit: "%")` — its value is shown beside it ("40 %", as
wide as the widest value so nothing jumps) and read back with `Fields.Int("Level")`. A masked text:
`rows.Password("Password:", required: true)`. All three exist on both front ends (`AiFgfRadioGroup`, `AiFgfSlider`).

**Colours — accent, light/dark, tint:** one colour is enough for a whole theme, and the user can pick it.

```csharp
AiFgfTheme.Current = AiFgfTheme.Create(Color.FromArgb(15, 123, 108), dark: false, tint: 0.3);   // before building forms
AiFgfTheme.Switch(AiFgfTheme.Create(accent, dark: true));   // later: recolours every open window in place
AiFgfThemeChoice? chosen = AiFgfThemeDialog.Show(this);     // swatches + any colour, Light/Dark, background tint
protected override bool ThemeButton => true;          // ToolForm: a "Theme…" button; the choice is saved
```

`AiFgfTheme.Create` derives every colour from the accent: window, field, grid, hover, selection and
border colours take the accent's hue as strongly as `tint` asks (0 = neutral grey, 1 = clearly
tinted); dark gives the dark variant. Whatever is chosen stays readable — every text/background pair
(`TextPairs()`) is brought to at least 4.5:1, so a pale or neon accent is darkened just enough to
carry white button text, keeping its hue. `AiFgfTheme.Accents` lists ten ready-made accents.

Background combinations follow fixed rules (tested on every built-in and generated theme): in a dark
theme the fields are clearly lighter than the window (raised, not dark holes) and their edges are
visible in every theme; the log follows the theme — a near-white page in a light window, dark grey
(never black) in a dark one — with error/warning/success colours kept at 4.5:1 on it. The parts the
system draws (drop-down lists, scroll bars of memos, logs and scrolling parameters) use Windows' own
dark control themes in a dark theme (`AiFgfNativeTheme`; drop-downs use the standard style so their arrow
button follows too), and the progress bar, tab headers and number spin arrows are drawn in the theme's
colours (`AiFgfProgressBar`, `AiFgfTabControl`, `AiFgfNumber`), so no white blocks remain in a dark window.

`AiFgfThemeDialog` previews every change live in all open windows; Cancel/Esc restores the previous
theme. `AiFgfTheme.Switch` maps colours by role (old window colour → new window colour, old accent →
new accent…), so colours an app set itself are kept; a control tagged `AiFgfTheme.KeepColors` (e.g. a
swatch) is left alone. In a spec: `"themeButton": true` and/or
`"theme": { "accent": "#0F7B6C", "dark": false, "tint": 0.2 }` (the tool's default; a user's saved choice wins).

**Memo fields:** `rows.Memo("Exclude:", lines: 3, maxLines: 12)` (spec: `"kind": "memo", "lines": 3,
"maxLines": 12`) is a multi-line `AiFgfMemo` for input — or, with `readOnly: true`, for showing text. Lines never
wrap, and it avoids scrolling instead:
- the height follows the text — at least `lines`, one more per line up to `maxLines` — and the window grows
  with it while the screen has room; only beyond `maxLines` does a vertical scroll bar appear;
- when text is set from code (settings, a preset, a result to show), the window widens to fit the longest
  line, up to the screen's working area; only a line wider than the screen scrolls sideways (typing never
  makes the window jump — a typed line that gets too long scrolls);
- scroll bars appear only when needed, so no empty bar takes a line's height; sizes follow DPI and font.

It opens at the start of its text; the value keeps its line breaks. Limit: the Windows edit control itself
breaks a single line at 1024 characters.

**Dialogs:** a small prompt from the same rows, in one call:

```csharp
var v = AiFgfDialog.Ask(this, "Add target", rows => rows
    .Text("Host:", required: true)
    .Number("Port:", min: 1, max: 65535, value: 443), okText: "&Add", initial: lastValues);
if (v is null) return;   // cancelled
```

OK validates and the dialog stays open (field marked and focused) while anything is invalid; Enter is OK, Esc is
Cancel; the values come back like `Fields.GetActiveValues()` (numbers invariant). The dialog sizes itself to its
content — rows side by side, buttons on one line, no empty band — and refits after a DPI change.
`AiFgfDialog.Create(...)` builds one without showing it.

**Crash handling:** `AiFgfApp.Run` installs `AiFgfCrash`. An unexpected exception on the UI thread writes a report
(exe, version, OS, runtime, DPI, thread, context, the exception and the last log lines) to
`%LocalAppData%\AI_FGF\crash\{exe}` and shows a dialog naming it; the app keeps running. A background-thread
crash still leaves a report. An exception in a `ProcessRunner.Output` subscriber is caught per subscriber
(it used to kill the process from the reader thread and starve the other subscribers) and surfaces as
`ProcessResult.HandlerError`. Pass `new AiFgfCrashOptions { Directory = …, OnCrash = … }` to change either.

**Language:** the words the framework puts on screen itself — the log's find bar, the theme dialog (accent
names included), OK/Cancel of `AiFgfDialog`, the grid's filter bar, file pickers, the crash box, `ToolForm`'s
buttons, tabs and status — come from `AiFgfText`, English by default. Set them once at start-up, before the
first window: `AiFgfText.UseRussian()` (the built-in Russian set), or single words (`AiFgfText.Find = "Suche:"`);
`AiFgfText.UseEnglish()` goes back. Explicit texts passed by the app (`okText:`, `RunButtonText`) still win.
The run log's own lines (`[finished — exit code …]`) stay English — they are a record, not chrome.

**Keyboard:** `Ctrl+Enter` or `F5` runs, `Esc` cancels a run in progress (and does nothing while idle),
`Ctrl+F` jumps to the log's find box. An ampersand in a row label (`"&Input file:"`) is an `Alt` mnemonic that
focuses the input; the field key stays `Input file`. Tab goes parameters → buttons → log, and the window
opens with the first field focused. Override `RunShortcut`/`CancelShortcut` to change the keys.

Out of the box a `ToolForm` also remembers its window placement and field values between
sessions (`%AppData%\AI_FGF\{type}.json`; override `SettingsKey`/`SettingsPath`, or return
null to opt out), can show **Save/Load preset** buttons (`PresetButtons => true`), and can dim
itself with the busy overlay while running (`ShowBusyOverlay => true`).

`ToolForm` handles the rest: enabling/disabling buttons during a run, cancellation that
kills the process tree, streaming stdout/stderr into the log with error colouring,
parsing `##PROGRESS##|activity|percent|status` lines into the progress bar, and
marshalling every cross-thread UI update (and dropping late ones once the form closes).
A PowerShell child that writes CLIXML (`-OutputFormat Xml`, or `pwsh` reading another `pwsh`) is logged
as the console would show it, not as XML. Errors and warnings get their colours, the copies of host
messages are dropped, and stdout and stderr batches are collected apart (`CliXmlLogFilter`, in AI_FGF.Core).
Override `OnRunFinished(ProcessResult)` to load results — e.g. `Results.ShowCsv(path)`.

## Moving an existing app: MVC, piece by piece

Not every app is a tool runner. A launcher that edits the registry, controls a service and downloads
releases does not fit `tool.json` → command line, but it can still take AI_FGF without being rebuilt.
`tests/AI_FGF.LayoutTests/Legacy` holds such a form: Designer-made, absolute coordinates, its own
`LightGreen`/`LightCoral` status colours, one handler shared by two buttons, an `OnShown` override.
`MvcTests` checks every step below on it.

**1. Split the form into view and controller. The markup and the handlers move as they are.**

```
AI_FGF.Check mvc Form1.cs            (reads Form1.Designer.cs next to it; -o dir to write elsewhere)
```

| File | What it is |
|---|---|
| `Form1View.Designer.cs` | the **view**: the old Designer markup, with the event wiring taken out and the fields made `internal` |
| `Form1View.cs` | the view's constructor (`InitializeComponent()`), nothing else |
| `Form1Controller.cs` | the **controller**: the old code-behind with every member as written. Each handler gets `[AiFgfOn("btnStart", "Click")]` from the wiring, and `OnLoad`-style overrides become `[AiFgfOn("View", "Load")]` handlers |
| `Form1Controller.Controls.cs` | one property per control under its old name (`txtPath` → `View.txtPath`), so `txtPath.Text` in a handler still compiles |

`AiFgfController<TView>` forwards the form members handlers use: `Close()`, `Text`, `Invoke`, `BeginInvoke`,
`Cursor`, `DialogResult`, and `MessageBox.Show(this, …)` as an `IWin32Window`. The app starts with
`Application.Run(new Form1Controller().View)`. The split lists what it could not move (an override such as
`ProcessCmdKey` stays on the view, a renamed `.resx`). `AiFgfMvc.Wire` returns, and `controller.Unbound` holds,
every handler whose control or event does not exist, so a test can require none. New handlers need no
attribute: `btnInstall_Click` binds by its name, as in the Designer.

**2. The model.** `AiFgfMvc.Bind(view, model)` binds a plain class to the controls both ways. A property finds
its control by name, ignoring the usual prefixes (`Path` ↔ `txtPath`, `Verbose` ↔ `chkVerbose`), or by
`[AiFgfBind("cmbTransport")]`. Values convert to the property's type: text, `bool`, numbers, enums, a
drop-down's index. A model that implements `INotifyPropertyChanged` updates the view. `AiFgfMvc.Bind(fields, model)`
binds to the keys of a `FormScaffold` form, so a controller can move from Designer markup to scaffold rows
without its model changing.

**3. AI_FGF's look and sizing, on by default** (`AiFgfAdoption`, `AiFgfMvc.DefaultAdoption`):
- `AiFgfAdopt.Theme(form)`: every control gets the colours of its role in `AiFgfTheme.Current`. That covers the
  window, the fields, the flat buttons, the default button in the accent, grids and links, plus the
  dark title bar and native dark scroll bars and drop-downs. A colour the form sets itself keeps its hue:
  `LightGreen` stays green and turns dark green in a dark theme, with its text at 4.5:1 or more. This
  holds for colours set by a handler at run time and for controls added later. `AiFgfTheme.Switch` re-themes
  from the original colours, so switching back to light gives exactly what the form had.
- `AutoMinSize.GuardPlaced(form)`: a minimum size worked out from the controls' positions and anchors,
  instead of a size clamp picked by hand. A stretching text box keeps a usable width, a caption stays whole,
  a right-anchored button stops 6 px short of its neighbour, and a group box needs what its own
  children need. It is re-computed on a DPI change and when a control is shown, hidden or added.
- `AiFgfAdopt.AlignRows(form)`: centres each row (label, text box, Browse button) on its tallest control
  after every DPI change. A Designer form scales by its font, and at 144 DPI the rounding leaves labels
  4–5 px off.

`AiFgfConformance.CheckAcrossDpi(() => new Form1Controller().View, 96, 120, 144, 192)` then checks the old form
like any AI_FGF form. It finds overlaps, clipped text, contrast and spacing problems.

**Depend on a release, not on these sources.** `tools/Pack.ps1 [-Version x.y.z]` packs AI_FGF.Core,
AI_FGF and AI_FGF.Avalonia into `artifacts/packages`. An app adds that folder to its `nuget.config`
and references `<PackageReference Include="AI_FGF" Version="x.y.z" />`. It builds on
`net10.0-windows7.0` and `net11.0-windows7.0` too. Copying single classes is not supported: `AutoMinSize`, the palette and the checks
share `AiFgfColor`, `ParamTable` and the metrics.

## The building blocks (use any on their own)

```csharp
// Theme — set once, everything follows (AiFgfTheme.Light / AiFgfTheme.Dark / your own).
AiFgfTheme.Current = AiFgfTheme.Dark;

// Controls
var run   = AiFgf.PrimaryButton("Run", (_, _) => Go());
var small = AiFgf.SmallButton("Browse…");
var box   = AiFgf.Text("placeholder");
string? f = AiFgf.PickOpenFile("Logs|*.log|All|*.*");

// Scaffold a form without ToolForm
FormScaffold.For(this)
    .Title("My Tool").Size(1000, 720)
    .Parameters("Options", rows => rows
        .FileText("Input:", out var input, "Text|*.txt")
        .FolderText("Output:", out var outDir))
    .Buttons(bar => bar.Primary("Go", (_, _) => Go(), out _))
    .Body(new AiFgfLog())
    .Done();

// Keep a form from shrinking past its buttons (ToolForm does this for you).
AutoMinSize.Guard(form, buttonBar);            // guards width by the bar, height by the docked stack;
                                               // also re-fits automatically when buttons are added/removed
AutoMinSize.Refit(form);                        // or recompute on demand
int w = AutoMinSize.ButtonsExtent(buttons, ButtonLayout.WrapRow, bar.Padding); // or compute it yourself

// Transparency / modern frame (Windows 11; no-op elsewhere) — ToolForm applies Mica by default.
Dwm.Apply(form, BackdropKind.Mica);            // or Acrylic; dark title bar follows the theme

// Raised, lit buttons drawn with Direct3D 12 (Windows 10/11): a pixel shader shapes each face — rounded,
// lighter at the top, a bevel, a soft shadow; the theme's hover fill; sunk and inset when pressed — read back
// into a cached bitmap, with the caption still drawn by GDI (crisp, access keys, contrast-checked). One device
// for the app: the first hardware adapter, else WARP; no Direct3D 12, a driver fault or a lost device → the
// standard flat buttons, same size and colours. AI_FGF_D3D=off / AI_FGF_D3D=warp override; AiFgfDirect3D.Status says which.
// Button style is one framework option (AiFgfEffects): Flat (default) / Raised / Glow. Strongest first:
// AI_FGF_BUTTONS=flat|raised|glow, the user's pick in the "Theme…" dialog (live preview, saved with settings),
// the form (ToolForm.ButtonStyle or "buttonStyle": "glow" in tool.json), AiFgfEffects.DefaultButtons (app-wide).
AiFgfEffects.DefaultButtons = AiFgfButtonStyle.Raised;                        // app default, at start-up
protected override AiFgfButtonStyle ButtonStyle => AiFgfButtonStyle.Glow;     // or one form
someAiFgfButton.Look = AiFgfButtonStyle.Glow;                                 // or one button; GlowColor overrides the accent
// .NET 11 only: the run's progress bar takes the same style - a sunken Direct3D track with a raised or glowing
// accent fill (AiFgfProgressBar.Look), and so do the tab headers - the selected tab raised (glowing with Glow), the
// others sunken (AiFgfTabControl.Look) - and the number fields' spin buttons, raised halves that sink when pressed
// (AiFgfNumber.Look); on .NET 10 all of these stay flat.

// Translucent busy scrim during a long step.
var busy = new AiFgfBusyOverlay();
busy.ShowOver(form, "Working…");  /* … */  busy.HideOverlay();

// Right-aligned (dialog-style) button bar that still wraps instead of clipping.
FormScaffold.For(this).Buttons(b => b.Primary("OK", OnOk, out _), BarAlign.Right).Body(body).Done();

// Process streaming, UI-free (testable)
// Scripts: ProcessRunner.PowerShell("tool.ps1", args) / ProcessRunner.Python("tool.py", args) — full Unicode output
// (pwsh's redirected output is otherwise the OEM code page: '✓' or CJK become '?'), arguments with spaces/quotes
// intact, the script's exit code kept, Python unbuffered so progress arrives as it is printed.
// .ExitCodes(p => p.Success(0, 7).Warning(3010, "Reboot required").Failure(16, "Fatal error")) — result.Outcome is
// Success / Warning / Failure / Cancelled / TimedOut with OutcomeMessage; ToolForm colours and words the result by it.
// .Timeout(TimeSpan.FromMinutes(5)) kills a hung tool's tree; the result says TimedOut (not Cancelled)
// and carries Duration/StartedAt. ToolForm: override RunTimeout; the log shows "[finished — exit code 0 in 1:23]".
var runner = new ProcessRunner("git").Arg("status").WorkingDirectory(dir);
runner.Output += (kind, line) => log.Append(line, kind == OutputKind.StdErr ? Color.Salmon : null);
ProcessResult result = await runner.RunAsync(cts.Token);
```

## Executable requirements

The library targets `net10.0-windows` and `net11.0-windows`, with `UseWindowsForms`. Your **exe** project adds:

```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net11.0-windows</TargetFramework>   <!-- or net10.0-windows -->
<UseWindowsForms>true</UseWindowsForms>
<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
```

`AiFgfApp.Run` performs the high-DPI / visual-styles / default-font setup itself, so it
works even though a class library has no source-generated `ApplicationConfiguration`.

## Design notes carried over from the source projects

- **PerMonitorV2 + `AutoScaleMode.Dpi`** from a 96-DPI baseline, so hand-coded forms
  scale at 125/150/200 % instead of clipping.
- **`AutoSize` + `WrapContents` button bars** grow to a second row rather than truncate.
- **Dock z-order**: Fill added first, then Top, then Bottom — `FormScaffold` enforces it.
- **`ArgumentList`** (never a concatenated command string) sidesteps all quoting bugs.
- **Cancellation kills the whole process tree**, and late cross-thread UI writes are
  dropped once the form is closing — the two things a streaming GUI gets wrong most often.
