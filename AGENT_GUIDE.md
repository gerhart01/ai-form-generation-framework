# AI Forms Generation Framework — generate forms with AI

The AI Forms Generation Framework lets an AI assistant (Claude Code, Copilot, Cursor, any coding agent) produce a
finished desktop form in minutes: you describe the tool in words — or paste its `--help` — and the assistant writes a
short `tool.json` (or a few lines of C#), checks it with one command and hands you a window that already works at
every DPI, in light and dark themes, on Windows (WinForms) and on Linux/macOS (Avalonia). No hand layout, no
designer, no pixel tuning: the framework owns the layout, and its checker tells the assistant when it is done.

This page is written for the assistant as much as for you: point the assistant at it and it has everything it needs.
The framework is in this folder — sources, samples, tools, the JSON schema and the full reference
([`README.md`](README.md)).

## Generate a form in minutes — prompts to copy

Each prompt names the inputs, the file to write and the stop condition, so the assistant needs no further questions.
Replace the parts in `<…>`. One prompt, one spec, one check: usually done in a minute or two.

**1. A form for a command-line tool, from its help text** — the fastest path:

```
Read AGENT_GUIDE.md. Create specs/<tool>.json for the AI Forms Generation Framework: a form for
<tool.exe>. Its help text is below — one field per option, the right kind for each (file, folder, choice,
number, flag, text), required options marked, defaults as documented. Run the Check tool on the spec and
fix it until it prints RESULT: PASS. Then show me the command line the defaults produce.

<paste the output of tool.exe --help here>
```

**2. A form from a description in words:**

```
Read AGENT_GUIDE.md. Using the AI Forms Generation Framework, make a form that runs
<python|pwsh> <script path> with these inputs: <input file (required, *.csv)>, <output folder>,
<mode: fast | full>, <threads 1–16, default 4>, <verbose flag>. Write specs/<name>.json,
run the Check tool until RESULT: PASS, and start it with the template exe so I can see it.
```

**3. Replace an existing WinForms form:**

```
Read AGENT_GUIDE.md, path B. Draft a spec from the form <Namespace.FormType> in <app.dll|exe> with
"Check draft", add what the draft lists as missing (the executable, each field's arg), and repeat
the Check until RESULT: PASS. Report anything of the old form the new one does not have.
```

**4. A console of controls (not a tool runner) — .NET 11:**

```
Read AGENT_GUIDE.md and README.md ("Free-form windows"). Build a WinForms window with
FormScaffold.Board: groups <Connection: host, port, Connect>, <Sound | Battery side by side>,
<Apps: a list with Start/Stop>, and a log on the right. Wire the buttons to <what they do>.
Build with 0 warnings and make AiFgfConformance.CheckAcrossDpi(..., 96, 120, 144, 192) empty.
```

**5. The same form on Linux/macOS:**

```
Read AGENT_GUIDE.md, path D. Run specs/<name>.json with the Avalonia front end and confirm the
window matches the Windows one (snapshot parity within 3 px).
```

**What makes these fast:** the assistant never invents layout — it fills a spec the framework lays out; the Check
tool gives it a yes/no answer (validation, the command line, the layout at 96–192 DPI) instead of you looking at
screenshots; and the template (`samples/…Template/tool.json`) shows it every element at once. Give the tool's real
help text or option list: guessing options is the only slow part.

**Ground rules for the assistant** (they hold for every prompt above):

- Only the framework's builders and the spec decide the layout; never set sizes, fonts or positions by hand.
- The form must be original: no other product's look, icons, images or texts (see "Generated forms must be
  original" below). If the request names an existing application or brings a screenshot, ask first.
- Done means the Check tool prints `RESULT: PASS` (or, for code, the build has 0 warnings and
  `AiFgfConformance.CheckAcrossDpi` is empty) — not "it looks right".

## Before anything

```
dotnet build AI_FGF.slnx -c Debug        # needs the .NET 10 or .NET 11 SDK; must end with 0 warnings, 0 errors
```

All paths below are relative to this folder (`ai-fgf/`). The names on disk keep the short code prefix (`AI_FGF.*`); "the Check
tool" — `Check` below — is `tools\AI_FGF.Check\bin\Debug\net11.0-windows\AI_FGF.Check.exe` (`net10.0-windows` with the
.NET 10 SDK). New features (the 3D components, the board layout and later) are .NET 11 only; .NET 10 builds keep the
earlier feature set.

```
ai-fgf/
  AGENT_GUIDE.md   ← you are here
  AI_FGF.slnx      the solution (net10.0 and net11.0)
  README.md        the full reference: every element, option and diagnostic
  CONTRIBUTING.md  the rules for adding code to the framework
  src/             the model and controllers (no UI) ← the WinForms and the Avalonia front ends
  samples/         demos and the tool.json template
  schemas/         tool.schema.json (editor completion and validation for tool.json)
  tools/           the Check tool (validate a spec, draft a spec from a form, MVC split), the test runner, packaging
```

## Which path

Read the table, take one path, stop when its "done when" holds.

| Situation | Path | Done when |
|---|---|---|
| A command-line tool / script needs a form, described in words | **A. Spec** (`tool.json`, no UI code) | `Check my-tool.json` → `RESULT: PASS` |
| An existing WinForms form (Designer-made or hand-coded) is to be replaced | **B. Migrate** (draft the spec from the form) | `Check draft …` → PASS, then A's check |
| The form needs logic a spec cannot express (parse output, custom buttons, results) | **C. Code** (subclass `ToolForm`) | build at 0 warnings; `AiFgfConformance.CheckAcrossDpi` empty |
| The same form must run on Linux/macOS | **D. Avalonia** (same source) | snapshot parity with the WinForms form |
| An app that is not a tool runner (registry, services, downloads) must take the framework without a rewrite | **E. MVC adoption** (`Check mvc`) | builds; `controller.Unbound` empty; `AiFgfConformance.CheckAcrossDpi` empty |

## A. A form from a description — `tool.json`

1. Copy `samples/AI_FGF.Template/tool.json` (it uses every element). Keep its `"$schema"` line, pointed at
   `schemas/tool.schema.json`: the editor then completes names, shows each property's documentation and
   flags typos (`additionalProperties: false`), wrong kinds and missing `key`/`label`.
2. Set `title`, `executable` (or `interpreter` + `script`), `fixedArgs`.
3. One field per option of the tool: `kind` = `text | file | folder | choice | number | flag | memo | note`
   (`note`: a line of text with no value, .NET 11);
   `arg` = `"-o"` (→ `-o value`), `"--x={value}"` (one token) or, for a flag, the switch; `positional: true`
   for bare values; `"switch": true` shows a flag as an on/off switch, `"password": true` hides a text field's
   input (never saved), `"radio": true` shows a choice as radio buttons (all three .NET 11); `required`, `default`, `min`/`max`/`decimals`, `choices`, `pattern`,
   `enabledWhen`/`visibleWhen` (`"Key"`, `"!Key"`, `"Key=value"`, `"Key!=value"`). More commands from the same
   fields (.NET 11): `"actions": [ { "label": "S&top", "fixedArgs": ["stop"], "fields": ["Name"] } ]` - a button
   each beside Run, with its own `fixedArgs`, optionally its own `executable` and only the `fields` it lists.
4. `Check my-tool.json` until PASS. It validates the spec (errors must go; warnings name fields that never
   reach the tool), prints the command line the defaults produce, and checks the layout at 96–192 DPI.
5. Run it: `samples\AI_FGF.Template\bin\Debug\net11.0-windows\AI_FGF.Template.exe my-tool.json`, or in code
   `AiFgfApp.Run(() => new SpecToolForm(ToolSpec.FromFile("my-tool.json")))`.

## B. Replacing an existing form

1. `Check draft <app.dll|exe> <Namespace.FormType> -o my-tool.json` — creates the form (with a parameterless
   constructor; on .NET 11 any form - one whose constructor takes arguments is made through its Designer
   `InitializeComponent()`), reads its labels and inputs into a draft spec in reading order: text boxes (a *Browse…*
   button beside one makes it `file`, or `folder` if the caption says folder/directory/папка), multi-line
   boxes → `memo`, spinners and track bars → `number` with range and decimals, drop-downs and list boxes → `choice`
   with items and editability, a radio-button group → one `choice` with `"radio": true`, a date picker → `text` with a
   `yyyy-MM-dd` pattern, check boxes (and each item of a checked list) → `flag`; a password box → `text` with
   `"password": true` and **no default** (its value is never copied); a box captioned only by its group box takes the
   group's caption; an empty read-only box (an output log) is left out. **Every tab page** is read, not only the shown
   one. Current values become defaults; the form's AcceptButton (else the lowest Run/Start/Launch/OK button) becomes
   `runButton`.
   It writes `my-tool.old-snapshot.json` (the old form's captions, inputs, fonts and bounds, every tab page; never a
   password).
2. It then builds the new form and compares it with the old one. Group boxes, tab headers and the old window buttons
   (OK, Cancel, Close, Exit…) are listed as "replaced by the tool form" — expected, they do not fail the check. A
   free label (a hint, a status line such as "Ready.") becomes a `note` where it stood, and every other button (not
   the run button, a Browse… or a window button) an entry of `"actions"` whose command you fill in (both .NET 11; on
   .NET 10 they are reported). A line "not in the new form" is a caption or input the draft missed — add it to the
   spec. Only behaviour written in code (a handler that fills one field from another, opens a window) needs path C,
   and only for that part.
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

## Generated forms must be original (copyright and related rights)

Every form an agent generates — from a spec, in code, or at random in tests — must be its own work and infringe no
one's rights in any country: the framework is published from a host in the USA by a developer in Russia, and used
anywhere. The table names those two laws; the rules below are written to hold everywhere, because the same split
runs through the international treaties nearly every country follows — the Berne Convention and TRIPS (expression is
protected, ideas and methods are not, works are protected abroad as at home), the Paris Convention (trademarks,
unfair competition) and the Hague system (industrial designs, GUIs included). Where countries differ, follow the
stricter rule. This is a working checklist, not legal advice; when a form is meant to resemble a particular product,
ask a lawyer first.

**What the law protects — and what it does not**

| | USA (the host) | Russia (the developer) |
|---|---|---|
| Not protected | ideas, procedures, systems, methods of operation (17 U.S.C. §102(b)); a functional arrangement of commands and controls (*Lotus v. Borland*, 1995); elements dictated by function or convention — a Run button, a log, OK/Cancel (merger, *scènes à faire*; *Apple v. Microsoft*, 1994) | ideas, concepts, principles, methods, processes, systems, solutions (Civil Code art. 1259 §5) |
| Copyright | the code; original artwork — icons, images, illustrations; substantial original text; font *files* (as software) | the program **together with the audiovisual displays it generates** (art. 1261): another program's distinctive screens are part of that program |
| Design rights | design patents on GUIs and icons (35 U.S.C. §171) | industrial designs — graphical interfaces are registered as such (art. 1352) |
| Marks and get-up | trademarks and trade dress — a distinctive, non-functional look that identifies a product (Lanham Act §43(a)) | trademarks (arts. 1477, 1484); unfair competition by imitating a competitor's get-up (Law on Protection of Competition, art. 14.6) |
| Notices | as the host of the published archive, a DMCA §512 takedown notice is answered by removing the item | a claim goes to the developer as the author of the program |

**Rules for generating a form**

1. **Layout** comes only from the framework's builders and metrics (`tool.json`, `ToolForm`, `FormScaffold.Board`):
   never traced from a screenshot of other software, never "make it look like <product>". When asked for that,
   build the *function* (the same inputs and actions) in the framework's own arrangement and style.
2. **No third-party assets**: no icons, logos, images, illustrations, sounds or bundled fonts from other products.
   Symbols are Unicode text (▶ ■ ✓) in a system font named by the theme (Segoe UI on Windows — referenced, not
   redistributed). Assets of your own, or under a licence that allows it (CC0, MIT …), recorded in
   `THIRD-PARTY-NOTICES.md`.
3. **Texts** are your own wording: no copied help texts, licence texts or captions of another program; no product or
   brand names in generated captions unless the tool really works with that product (and then only as a plain name,
   never its logo or styled lettering).
4. **Colours** come from the theme generator (`AiFgfTheme.Create` from one accent): an accent colour alone is free to
   use, but a known product's characteristic combination — its colours together with its layout, shapes and marks —
   is not reproduced.
5. **Random forms in tests** use neutral vocabulary and the framework's own control set, so every generated form is
   original by construction; seeds make each one reproducible for review.
6. **Code** is the framework's or under a compatible licence. Before using anyone's ready-made work — a package, a
   snippet, a formula from a page, a shader technique, an asset — check its licence (MIT, BSD, Apache-2.0, ISC, CC0
   and SIL OFL for fonts are fine; GPL/LGPL/AGPL, CC BY-SA and CC BY-NC are not; no licence means no permission;
   a Stack Overflow answer may be read and its idea written as your own code, but a non-trivial snippet is not
   copied verbatim unless its author published it under a compatible licence too) and list it in `THIRD-PARTY-NOTICES.md` with its author, licence and link
   (`CONTRIBUTING.md`, section 4).

**When to ask before generating** — one short question, only when the request points at someone else's work, not on
every form:

- the request names an existing application or brand ("like X", "the same as X's settings window"), or comes with
  a screenshot of another program: ask *"Should the form reproduce X's functions only (recommended), or does it
  need to resemble X — and if so, do you hold the rights to X's design?"* Without a clear yes, build the functions
  in the framework's own look.
- the request brings icons, images, fonts or long texts the user did not say are theirs: ask where they come from
  and under what licence; without an answer, leave them out.
- the form is for a product of the user's own (their own app's look): no question needed — that is their work.

**Checks before a form is published**

- `Check my-tool.json` → `RESULT: PASS` (layout), and a read of every caption: own words, no brand names.
- No image, icon or font file added to the repository without a licence entry in `THIRD-PARTY-NOTICES.md`.
- The publishing script (`pwsh tools/New-PublicArchive.ps1`) passes: it rejects binaries and every name on the
  internal deny-list.
- If the form was requested "like" an existing product: compare the two side by side — same functions are fine, a
  recognisably copied look (arrangement, icons, colours, wording together) is not; change it until it is not.

## Pitfalls

- A field with no `arg` and not `positional` never reaches the tool (a warning, not an error).
- `enabledWhen`/`visibleWhen` fields that are inactive are neither validated nor passed.
- Keys are case-insensitive and must be unique; `{Key}` placeholders must name a field.
- `default` of a `choice` must be one of its `choices`; of a `number`, within `min`–`max`.
- Do not read or write `%AppData%\AI_FGF` from tests.
