# AI_FGF — agent guide: start here

AI_FGF (AI Form Generation Framework) builds Windows Forms tool-runner windows (and the same windows on
Linux/macOS with Avalonia) from a short description: a `tool.json` spec, or a few lines of C#. Every
layout is checked at 96–192 DPI, in light and dark themes, so a generated form needs no hand-tuning.

This page is the instructions: read it first. The framework itself is in [`ai-fgf/`](ai-fgf/) — sources,
samples, tools, the JSON schema and the full reference ([`ai-fgf/README.md`](ai-fgf/README.md)).

```
AGENT_GUIDE.md     ← you are here: which path to take, and when the work is done
ai-fgf/
  AI_FGF.slnx      the solution (net10.0 and net11.0)
  README.md        the full reference: every element, option and diagnostic
  src/             AI_FGF.Core (no UI) ← AI_FGF (WinForms), AI_FGF.Avalonia, AI_FGF.Avalonia.Direct3D
  samples/         demos and the tool.json template
  schemas/         tool.schema.json (editor completion and validation for tool.json)
  tools/           AI_FGF.Check (validate a spec, draft a spec from a form, MVC split), packaging
```

## Before anything

```
cd ai-fgf
dotnet build AI_FGF.slnx -c Debug        # needs the .NET 10 or .NET 11 SDK; must end with 0 warnings, 0 errors
```

All paths below are relative to `ai-fgf/`. `Check` means
`tools\AI_FGF.Check\bin\Debug\net11.0-windows\AI_FGF.Check.exe` (`net10.0-windows` with the .NET 10 SDK).
New features (3D progress bar and later) are .NET 11 only; .NET 10 builds keep the earlier feature set.

## Which path

Read the table, take one path, stop when its "done when" holds.

| Situation | Path | Done when |
|---|---|---|
| A command-line tool / script needs a form, described in words | **A. Spec** (`tool.json`, no UI code) | `Check my-tool.json` → `RESULT: PASS` |
| An existing WinForms form (Designer-made or hand-coded) is to be replaced | **B. Migrate** (draft the spec from the form) | `Check draft …` → PASS, then A's check |
| The form needs logic a spec cannot express (parse output, custom buttons, results) | **C. Code** (subclass `ToolForm`) | build at 0 warnings; `AiFgfConformance.CheckAcrossDpi` empty |
| The same form must run on Linux/macOS | **D. Avalonia** (same source) | snapshot parity with the WinForms form |
| An app that is not a tool runner (registry, services, downloads) must take AI_FGF without a rewrite | **E. MVC adoption** (`Check mvc`) | builds; `controller.Unbound` empty; `AiFgfConformance.CheckAcrossDpi` empty |

## A. A form from a description — `tool.json`

1. Copy `samples/AI_FGF.Template/tool.json` (it uses every element). Keep its `"$schema"` line, pointed at
   `schemas/tool.schema.json`: the editor then completes names, shows each property's documentation and
   flags typos (`additionalProperties: false`), wrong kinds and missing `key`/`label`.
2. Set `title`, `executable` (or `interpreter` + `script`), `fixedArgs`.
3. One field per option of the tool: `kind` = `text | file | folder | choice | number | flag | memo`;
   `arg` = `"-o"` (→ `-o value`), `"--x={value}"` (one token) or, for a flag, the switch; `positional: true`
   for bare values; `required`, `default`, `min`/`max`/`decimals`, `choices`, `pattern`,
   `enabledWhen`/`visibleWhen` (`"Key"`, `"!Key"`, `"Key=value"`, `"Key!=value"`).
4. `Check my-tool.json` until PASS. It validates the spec (errors must go; warnings name fields that never
   reach the tool), prints the command line the defaults produce, and checks the layout at 96–192 DPI.
5. Run it: `samples\AI_FGF.Template\bin\Debug\net11.0-windows\AI_FGF.Template.exe my-tool.json`, or in code
   `AiFgfApp.Run(() => new SpecToolForm(ToolSpec.FromFile("my-tool.json")))`.

## B. Replacing an existing form

1. `Check draft <app.dll|exe> <Namespace.FormType> -o my-tool.json` — creates the form (parameterless
   constructor), reads its labels and inputs into a draft spec in reading order: text boxes (a *Browse…*
   button beside one makes it `file`, or `folder` if the caption says folder/directory/папка), multi-line
   boxes → `memo`, spinners → `number` with range and decimals, drop-downs → `choice` with items and
   editability, check boxes → `flag`; current values become defaults; an OK/Run/Start button becomes `runButton`.
   It writes `my-tool.old-snapshot.json` (the old form's captions, inputs, fonts and bounds).
2. It then builds the new form and lists every caption or input of the old one the new one lacks. The old
   form's own OK/Cancel are expected there (the tool form has Run and the close box); anything else is a
   field the draft missed — add it to the spec.
3. Fill what a form cannot tell: `executable`, each field's `arg`. The draft's "to do" lines are exactly
   these gaps. Then path A from step 4.
4. In code: `AiFgfMigration.DraftSpec(form)`, `AiFgfSnapshot.Of(form)`,
   `newSnap.Compare(oldSnap, AiFgfSnapshotTolerance.ContentOnly)`. Any two snapshots:
   `Check compare reference.json candidate.json [--content]`.

## C. A form in code — subclass `ToolForm`

```csharp
public sealed class PingToolForm : ToolForm
{
    protected override string FormTitle => "Ping";
    protected override string RunButtonText => "▶ &Ping";
    protected override void BuildParameters(FormScaffold.RowBuilder rows) => rows
        .Text("Host:", value: "127.0.0.1", required: true)
        .Number("Count:", min: 1, max: 100, value: 4)
        .Checks(("Resolve", AiFgf.Check("Resolve addresses")));
    protected override ProcessRunner? BuildRun() => new ProcessRunner("ping")
        .Option("-n", Fields.Int("Count")).ArgIf(Fields.Bool("Resolve"), "-a").Arg(Fields.Text("Host"));
}
```

- Rows: `Text`, `Memo`, `FileText`, `FolderText`, `Combo`, `Choice`/`Choice<TEnum>`, `Number`, `Checks`, `Custom`;
  `.EnableWhen(f => …)` / `.VisibleWhen(f => …)` on the row just added. Keys default to the label without
  `&` and `:`.
- Values: `Fields.Text/Int/Decimal/Bool/Get<TEnum>(key)`; `Fields.Rule(key, v => error-or-null)`. Numbers are
  culture-proof (`1,5` under ru-RU is 1.5; stored and passed invariant); a number field reads as shown.
- Hooks: `ConfigureButtons(bar)`, `OnRunFinished(result)` (fill `ResultsView`), `RunTimeout`, `PresetButtons`,
  `ThemeButton`, `SettingsKey` (null = don't persist).
- Never set fonts, sizes or positions by hand: `FormScaffold` and `AutoMinSize` own the layout at every DPI.
- Tests: subclasses return `SettingsKey => null`, or set `ToolForm.PersistSettings = false`.
- Check: `AiFgfConformance.CheckAcrossDpi(() => new MyForm(), 96, 120, 144, 192)` must be empty.

## D. The same form on Linux/macOS — Avalonia

The project references `src/AI_FGF.Avalonia` and adds `<Using Include="AI_FGF.Avalonia" />`. The C#
form source compiles unchanged (`samples/AI_FGF.Demo.Avalonia` links the WinForms demo's files).
`WinFormsMetrics` reproduces the WinForms spacing; `AiFgfSnapshot.Of(window)` vs a WinForms snapshot
(the golden `*.winforms.json` files of the test suite) holds every element within 3 px, in the same font and size.

Real Direct3D 12 rendering (the WinForms look: the bevel/glow shader, not the drop-shadow
fallback) is also available on Avalonia, Windows-only and experimental: reference `src/AI_FGF.Avalonia.Direct3D`
and either set `"renderer": "direct3d"` in tool.json, or override `ToolForm.UseDirect3D => true`. No other
code changes - referencing the project is the whole opt-in (`AiFgfRenderers.Direct3D`, set by a module
initializer); on Linux/macOS, or without the add-on referenced, or if Direct3D could not initialise, the
drop shadow is used unchanged. See `samples/AI_FGF.Demo.Direct3D.Spec`.

## E. An existing app, piece by piece: view + controller

1. `Check mvc Form1.cs` writes the following next to the form (or into `-o dir`):
   - `Form1View.cs` and `Form1View.Designer.cs`: the markup, without the wiring, its fields `internal`;
   - `Form1Controller.cs`: the code-behind as written, with handlers marked `[AiFgfOn("ctl", "Event")]` and
     `OnLoad`-style overrides turned into `[AiFgfOn("View", "Load")]` handlers;
   - `Form1Controller.Controls.cs`: a property per control, so handlers compile unchanged.
2. Do its "to do" lines:
   - start with `Application.Run(new Form1Controller().View)`;
   - move overrides such as `ProcessCmdKey`/`WndProc` to `Form1View.cs`;
   - rename the `.resx`;
   - delete `Form1.cs` and `Form1.Designer.cs`.
3. Build at 0 warnings. `new Form1Controller().Unbound` must be empty: every handler found its control and event.
4. The controller themes the view (`AiFgfAdopt.Theme`), centres its rows (`AiFgfAdopt.AlignRows`) and gives it a
   minimum size from its anchors (`AutoMinSize.GuardPlaced`). Turn these off with `AiFgfMvc.DefaultAdoption`
   or the `AiFgfController(view, AiFgfAdoption)` constructor.
5. Optional: a model class with `AiFgfMvc.Bind(View, model)`. Properties match controls by name without prefix,
   or by `[AiFgfBind("name")]`. Handlers can then read `model.Path` instead of `txtPath.Text`.
6. `AiFgfConformance.CheckAcrossDpi(() => new Form1Controller().View, 96, 120, 144, 192)` must be empty. What it
   reports is the old form's own layout: a label too close to its box at 192 DPI, a log that wraps. Fix
   these in the markup.
7. Outside this repository, reference the package (`tools/Pack.ps1` → `artifacts/packages`,
   `<PackageReference Include="AI_FGF" Version="…" />`), not the project.

Worked example (development repository only, not in the published archive): `tests/AI_FGF.LayoutTests/Legacy`
(before) and `Legacy/Migrated` (after), checked by `MvcTests`.

## Pitfalls

- A field with no `arg` and not `positional` never reaches the tool (a warning, not an error).
- `enabledWhen`/`visibleWhen` fields that are inactive are neither validated nor passed.
- Keys are case-insensitive and must be unique; `{Key}` placeholders must name a field.
- `default` of a `choice` must be one of its `choices`; of a `number`, within `min`–`max`.
- Do not read or write `%AppData%\AI_FGF` from tests.
