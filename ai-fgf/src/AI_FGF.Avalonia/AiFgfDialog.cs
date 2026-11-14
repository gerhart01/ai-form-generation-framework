using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AI_FGF.Avalonia;

/// <summary>
/// A small modal prompt built from rows — the Avalonia twin of the WinForms <c>AiFgfDialog</c>:
/// <code>
/// var v = AiFgfDialog.Ask(this, "Add target", rows => rows
///     .Text("Host:", required: true)
///     .Number("Port:", min: 1, max: 65535, value: 443), okText: "&amp;Add");
/// if (v is null) return;   // cancelled
/// </code>
/// OK validates and stays open while a field is invalid; Enter = OK, Esc = Cancel; it sizes itself
/// to its content. <see cref="Ask"/> is synchronous like WinForms (a nested dispatcher loop);
/// <see cref="AskAsync"/> awaits instead.
/// </summary>
public sealed class AiFgfDialog : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    private readonly FormScaffold _scaffold;
    private bool _accepted;

    private AiFgfDialog(string title, Action<FormScaffold.RowBuilder> build, string okText, string cancelText)
    {
        _scaffold = FormScaffold.For(this).Title(title);
        Button ok = null!, cancel = null!;
        _scaffold
            .Parameters("", build)
            .Buttons(bar => bar.Button(cancelText, (_, _) => Close(), out cancel).Primary(okText, (_, _) => Accept(), out ok), BarAlign.Right);
        _scaffold.Done();
        Ok = ok;
        Cancel = cancel;
        Ok.IsDefault = true;     // Enter
        Cancel.IsCancel = true;  // Esc
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 420;
        Opened += (_, _) =>
        {
            if (Fields.Keys.FirstOrDefault() is { } first) Fields[first].Focus();
        };
    }

    public static AiFgfDialog Create(string title, Action<FormScaffold.RowBuilder> build, string? okText = null,
                                  IReadOnlyDictionary<string, string>? initial = null, string? cancelText = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        var d = new AiFgfDialog(title, build, okText ?? AiFgfText.Ok, cancelText ?? AiFgfText.Cancel);
        if (initial is not null) d.Fields.SetValues(initial);
        return d;
    }

    /// <summary>Modal and synchronous (WinForms style): the active values, or null if cancelled.</summary>
    public static Dictionary<string, string>? Ask(Window? owner, string title, Action<FormScaffold.RowBuilder> build,
                                                  string? okText = null, IReadOnlyDictionary<string, string>? initial = null)
        => AiFgf.Wait(AskAsync(owner, title, build, okText, initial));

    public static async Task<Dictionary<string, string>?> AskAsync(Window? owner, string title, Action<FormScaffold.RowBuilder> build,
                                                                    string? okText = null, IReadOnlyDictionary<string, string>? initial = null)
    {
        var d = Create(title, build, okText, initial);
        if (owner is not null) await d.ShowDialog(owner);
        else
        {
            var closed = new TaskCompletionSource();
            d.Closed += (_, _) => closed.TrySetResult();
            d.Show();
            await closed.Task;
        }
        return d.Values;
    }

    public FieldSet Fields => _scaffold.Fields;

    public Button Ok { get; }

    public Button Cancel { get; }

    /// <summary>True once OK was accepted.</summary>
    public bool Accepted => _accepted;

    /// <summary>The active values once OK was accepted; null otherwise.</summary>
    public Dictionary<string, string>? Values => _accepted ? Fields.GetActiveValues() : null;

    /// <summary>What OK does: validate, and close only when every active field is valid.</summary>
    public bool Accept()
    {
        if (Fields.Validate().Count > 0) return false;
        _accepted = true;
        Close();
        return true;
    }
}
