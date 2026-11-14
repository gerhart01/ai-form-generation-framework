using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// Binds the method it marks to an event of a control, a component or the view itself. The Designer's wiring
/// <c>this.btnInstall.Click += new EventHandler(this.btnInstall_Click);</c> becomes
/// <c>[AiFgfOn("btnInstall", "Click")]</c> on the handler. <c>"View"</c> (or the view's <see cref="Control.Name"/>)
/// names the form itself. One handler may carry several.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AiFgfOnAttribute : Attribute
{
    public AiFgfOnAttribute(string target, string @event) { Target = target; Event = @event; }
    public string Target { get; }
    public string Event { get; }
}

/// <summary>Binds a model property to the control (or <see cref="FieldSet"/> key) of this name, instead of the one found by its own name.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AiFgfBindAttribute : Attribute
{
    public AiFgfBindAttribute(string control) { Control = control; }
    public string Control { get; }
}

/// <summary>
/// Model–view–controller for WinForms. The <b>view</b> is a form with only its markup: a Designer file, or a form
/// built with <see cref="FormScaffold"/>. The <b>controller</b> (<see cref="AiFgfController{TView}"/>) holds the
/// handlers. The <b>model</b> is a plain class whose properties are bound both ways to the view's controls
/// (<see cref="Bind(Control, object, bool)"/>). Moving an old form over then means taking its markup as the view and
/// moving its handlers into the controller, unchanged. <c>AI_FGF.Check mvc Form1.cs</c> does that split
/// (<see cref="AiFgfMvcMigration"/>).
/// </summary>
public static class AiFgfMvc
{
    /// <summary>What a controller created without saying does to its view (<see cref="AiFgfAdoption.All"/>: theme and minimum size).</summary>
    public static AiFgfAdoption DefaultAdoption { get; set; } = AiFgfAdoption.All;

    private static readonly string[] Prefixes =
        { "txt", "tb", "tbx", "chk", "cb", "cbx", "chb", "cmb", "cbo", "ddl", "num", "nud", "rb", "rdo", "rad", "opt", "trk", "dtp", "lbl", "lst", "lb" };

    /// <summary>
    /// Subscribe <paramref name="controller"/>'s handlers to <paramref name="view"/>'s events. A handler is a method
    /// taking (object sender, an EventArgs). It is bound by its <see cref="AiFgfOnAttribute"/>s, or, without any, by its
    /// name <c>target_Event</c> (the Designer's own naming: <c>btnInstall_Click</c>, <c>Form1_Load</c>).
    /// Returns what could not be bound (a handler whose target or event does not exist), so a test can require none.
    /// </summary>
    public static IReadOnlyList<string> Wire(Form view, object controller)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(controller);
        var problems = new List<string>();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (Type? t = controller.GetType(); t is not null && t != typeof(object) && !IsControllerBase(t); t = t.BaseType)
        {
            foreach (MethodInfo m in t.GetMethods(all | BindingFlags.DeclaredOnly))
            {
                var on = m.GetCustomAttributes<AiFgfOnAttribute>().ToList();
                ParameterInfo[] ps = m.GetParameters();
                bool handlerShape = ps.Length == 2 && ps[0].ParameterType == typeof(object) && typeof(EventArgs).IsAssignableFrom(ps[1].ParameterType) && m.ReturnType == typeof(void);
                if (on.Count == 0)
                {
                    // by name: the part before the last '_' must name something on the view
                    int cut = m.Name.LastIndexOf('_');
                    if (!handlerShape || cut <= 0 || cut == m.Name.Length - 1 || m.IsSpecialName) continue;
                    string target = m.Name[..cut];
                    if (FindTarget(view, target) is null) continue;   // just a method with an underscore
                    on.Add(new AiFgfOnAttribute(target, m.Name[(cut + 1)..]));
                }
                else if (!handlerShape)
                {
                    problems.Add($"{m.Name}: a handler takes (object sender, EventArgs e) and returns void");
                    continue;
                }
                foreach (AiFgfOnAttribute a in on)
                {
                    object? target = FindTarget(view, a.Target);
                    EventInfo? evt = target?.GetType().GetEvent(a.Event, BindingFlags.Instance | BindingFlags.Public);
                    if (target is null) { problems.Add($"{m.Name}: no control or component '{a.Target}' on {view.GetType().Name}"); continue; }
                    if (evt?.EventHandlerType is not { } type) { problems.Add($"{m.Name}: {a.Target} ({target.GetType().Name}) has no event '{a.Event}'"); continue; }
                    Delegate? d = Delegate.CreateDelegate(type, controller, m, throwOnBindFailure: false);
                    if (d is null) { problems.Add($"{m.Name}: does not fit {a.Target}.{a.Event} ({type.Name})"); continue; }
                    evt.AddEventHandler(target, d);
                }
            }
        }
        return problems;
    }

    private static bool IsControllerBase(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AiFgfController<>);

    /// <summary>
    /// The control, component or form called <paramref name="name"/> on <paramref name="view"/>: "View", "this" and
    /// the view's own name mean the view; then a control by <see cref="Control.Name"/>; then a field of the view
    /// (a Timer, a ToolTip, an OpenFileDialog — components that are not controls).
    /// </summary>
    public static object? FindTarget(Form view, string name)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (name is "View" or "this" || string.Equals(name, view.Name, StringComparison.Ordinal) || name == view.GetType().Name) return view;
        if (FindControl(view, name) is { } c) return c;
        for (Type? t = view.GetType(); t is not null && t != typeof(Form); t = t.BaseType)
            if (t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } f)
                return f.GetValue(view);
        return null;
    }

    private static Control? FindControl(Control root, string name)
    {
        foreach (Control c in root.Controls)
        {
            if (string.Equals(c.Name, name, StringComparison.Ordinal)) return c;
            if (FindControl(c, name) is { } d) return d;
        }
        return null;
    }

    /// <summary>
    /// Bind <paramref name="model"/>'s properties both ways to the controls of <paramref name="root"/>. A property
    /// finds its control by <see cref="AiFgfBindAttribute"/>, or by name, ignoring case and the usual prefixes
    /// (<c>Path</c> ↔ <c>txtPath</c>, <c>Verbose</c> ↔ <c>chkVerbose</c>). Text boxes and labels give text; check
    /// boxes and radio buttons, bool; spinners, sliders and date pickers, their value; drop-downs, the text or the
    /// index (an int property). The control's value goes to the model as it changes. If the model implements
    /// <see cref="INotifyPropertyChanged"/>, its changes go to the control. At binding, the model's values are
    /// written into the controls, unless a value is null or <paramref name="fromView"/> is true.
    /// </summary>
    public static AiFgfBinding Bind(Control root, object model, bool fromView = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(model);
        var all = Descendants(root).Where(c => c.Name.Length > 0).ToList();
        return Bind(model, fromView, (name, explicitName) =>
            all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))
            ?? (explicitName ? null : all.Where(c => Matches(c.Name, name)).OrderBy(c => c is Label ? 1 : 0).FirstOrDefault()));   // an input before its caption
    }

    /// <summary><see cref="Bind(Control, object, bool)"/> for a form built with <see cref="FormScaffold"/>: a property binds to the field of its key.</summary>
    public static AiFgfBinding Bind(FieldSet fields, object model, bool fromView = false)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(model);
        return Bind(model, fromView, (name, _) => fields.Contains(name) ? fields[name] : null);
    }

    private static AiFgfBinding Bind(object model, bool fromView, Func<string, bool, Control?> find)
    {
        var binding = new AiFgfBinding(model);
        foreach (PropertyInfo p in model.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
            var explicitName = p.GetCustomAttribute<AiFgfBindAttribute>()?.Control;
            Control? c = find(explicitName ?? p.Name, explicitName is not null);
            if (c is null)
            {
                if (explicitName is not null) binding.Unbound.Add($"{p.Name}: no control '{explicitName}'");
                continue;
            }
            if (!AiFgfBinding.Supports(c, p.PropertyType)) { binding.Unbound.Add($"{p.Name}: a {Nice(p.PropertyType)} cannot bind to {c.Name} ({c.GetType().Name})"); continue; }
            binding.Add(p, c, fromView);
        }
        return binding;
    }

    private static string Nice(Type t) => Nullable.GetUnderlyingType(t) is { } u ? Nice(u) + "?" : t.Name;

    /// <summary>"txtPath" matches "Path"; "chkVerbose" matches "verbose".</summary>
    private static bool Matches(string control, string property)
    {
        if (string.Equals(control, property, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (string p in Prefixes)
            if (control.Length > p.Length && control.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                && string.Equals(control[p.Length..].TrimStart('_'), property, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            if (c is UpDownBase) continue;
            foreach (Control d in Descendants(c)) yield return d;
        }
    }
}

/// <summary>A model bound to a view's controls (<see cref="AiFgfMvc.Bind(Control, object, bool)"/>). Dispose to unbind.</summary>
public sealed class AiFgfBinding : IDisposable
{
    private readonly List<(PropertyInfo Property, Control Control, EventHandler Handler)> _links = new();
    private readonly object _model;
    private bool _busy;

    internal AiFgfBinding(object model)
    {
        _model = model;
        if (model is INotifyPropertyChanged n) n.PropertyChanged += OnModelChanged;
    }

    /// <summary>Properties that named a control which is missing or cannot hold their type.</summary>
    public List<string> Unbound { get; } = new();

    /// <summary>The bound properties and their controls.</summary>
    public IEnumerable<(string Property, Control Control)> Links => _links.Select(l => (l.Property.Name, l.Control));

    internal static bool Supports(Control c, Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        return c switch
        {
            CheckBox or RadioButton => t == typeof(bool),
            NumericUpDown or TrackBar => IsNumber(t),
            DateTimePicker => t == typeof(DateTime),
            ComboBox or ListBox => t == typeof(string) || t == typeof(int) || t.IsEnum,
            AiFgfRadioGroup => t == typeof(string) || t == typeof(int) || t.IsEnum,
            _ => t == typeof(string) || IsNumber(t) || t.IsEnum,
        };
    }

    private static bool IsNumber(Type t) => t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double) || t == typeof(float);

    internal void Add(PropertyInfo p, Control c, bool fromView)
    {
        void Changed(object? s, EventArgs e) { if (!_busy) ToModel(p, c); }
        EventHandler h = Changed;
        switch (c)
        {
            case CheckBox box: box.CheckedChanged += h; break;
            case RadioButton r: r.CheckedChanged += h; break;
            case NumericUpDown n: n.ValueChanged += h; break;
            case TrackBar tr: tr.ValueChanged += h; break;
            case DateTimePicker d: d.ValueChanged += h; break;
            case ComboBox combo: combo.SelectedIndexChanged += h; combo.TextChanged += h; break;
            case ListBox list: list.SelectedIndexChanged += h; break;
            default: c.TextChanged += h; break;
        }
        _links.Add((p, c, h));
        if (fromView || p.GetValue(_model) is null) ToModel(p, c);
        else ToView(p, c);
    }

    /// <summary>Write every model value into its control (after the model was changed without notifications).</summary>
    public void Refresh() { foreach (var l in _links) ToView(l.Property, l.Control); }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var l in _links)
            if (string.IsNullOrEmpty(e.PropertyName) || l.Property.Name == e.PropertyName) ToView(l.Property, l.Control);
    }

    private static Type Under(Type t) => Nullable.GetUnderlyingType(t) ?? t;

    private void ToModel(PropertyInfo p, Control c)
    {
        Type t = Under(p.PropertyType);
        object? value = c switch
        {
            CheckBox box => box.Checked,
            RadioButton r => r.Checked,
            NumericUpDown n => Convert.ChangeType(n.Value, t, CultureInfo.InvariantCulture),
            TrackBar tr => Convert.ChangeType(tr.Value, t, CultureInfo.InvariantCulture),
            DateTimePicker d => d.Value,
            ComboBox combo when t == typeof(int) => combo.SelectedIndex,
            ListBox list when t == typeof(int) => list.SelectedIndex,
            ListBox list => t == typeof(string) ? list.GetItemText(list.SelectedItem) : Parse(list.GetItemText(list.SelectedItem) ?? "", t),
            _ => t == typeof(string) ? c.Text : Parse(c.Text, t),
        };
        if (value is null && t.IsValueType && Nullable.GetUnderlyingType(p.PropertyType) is null) return;   // unparsable: keep
        object? old = p.GetValue(_model);
        if (Equals(old, value)) return;
        _busy = true;
        try { p.SetValue(_model, value); }
        finally { _busy = false; }
    }

    private static object? Parse(string text, Type t)
    {
        text = text.Trim();
        if (t.IsEnum) return Enum.TryParse(t, text.Replace(" ", "", StringComparison.Ordinal), true, out object? e) ? e : null;
        string invariant = text.Replace(',', '.');
        return decimal.TryParse(invariant, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d)
            ? Convert.ChangeType(d, t, CultureInfo.InvariantCulture)
            : null;
    }

    private void ToView(PropertyInfo p, Control c)
    {
        if (_busy) return;
        object? v = p.GetValue(_model);
        _busy = true;
        try
        {
            switch (c)
            {
                case CheckBox box: box.Checked = v is true; break;
                case RadioButton r: r.Checked = v is true; break;
                case NumericUpDown n when v is not null: n.Value = Math.Clamp(Convert.ToDecimal(v, CultureInfo.InvariantCulture), n.Minimum, n.Maximum); break;
                case TrackBar tr when v is not null: tr.Value = Math.Clamp(Convert.ToInt32(v, CultureInfo.InvariantCulture), tr.Minimum, tr.Maximum); break;
                case DateTimePicker d when v is DateTime dt: d.Value = dt; break;
                case ComboBox combo when v is int i: combo.SelectedIndex = i >= -1 && i < combo.Items.Count ? i : -1; break;
                case ListBox list when v is int i: list.SelectedIndex = i >= -1 && i < list.Items.Count ? i : -1; break;
                case ListBox list: list.SelectedIndex = list.FindStringExact(Format(v)); break;
                case ComboBox { DropDownStyle: ComboBoxStyle.DropDownList } combo: combo.SelectedIndex = combo.FindStringExact(Format(v)); break;
                default: c.Text = Format(v); break;
            }
        }
        finally { _busy = false; }
    }

    private static string Format(object? v) => v switch
    {
        null => "",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    public void Dispose()
    {
        if (_model is INotifyPropertyChanged n) n.PropertyChanged -= OnModelChanged;
        foreach (var (_, c, h) in _links)
        {
            switch (c)
            {
                case CheckBox box: box.CheckedChanged -= h; break;
                case RadioButton r: r.CheckedChanged -= h; break;
                case NumericUpDown num: num.ValueChanged -= h; break;
                case TrackBar tr: tr.ValueChanged -= h; break;
                case DateTimePicker d: d.ValueChanged -= h; break;
                case ComboBox combo: combo.SelectedIndexChanged -= h; combo.TextChanged -= h; break;
                case ListBox list: list.SelectedIndexChanged -= h; break;
                default: c.TextChanged -= h; break;
            }
        }
        _links.Clear();
    }
}

/// <summary>What <see cref="AiFgfController{TView}"/> also does to its view.</summary>
[Flags]
public enum AiFgfAdoption
{
    /// <summary>Nothing: the view keeps its own look and sizing.</summary>
    None = 0,
    /// <summary><see cref="AiFgfAdopt.Theme(Control)"/>: theme colours, title bar, dark scroll bars.</summary>
    Theme = 1,
    /// <summary><see cref="AutoMinSize.GuardPlaced"/>: a minimum size from the controls' positions and anchors.</summary>
    MinimumSize = 2,
    /// <summary><see cref="AiFgfAdopt.AlignRows"/>: each row of controls centred, at every DPI.</summary>
    AlignRows = 4,
    /// <summary><see cref="AiFgfAdopt.KeepLayoutAcrossDpi"/>: the placed layout kept whole when the window moves to another DPI.</summary>
    DpiLayout = 8,
    /// <summary>All of them.</summary>
    All = Theme | MinimumSize | AlignRows | DpiLayout,
}

/// <summary>
/// The controller of a view (<see cref="AiFgfMvc"/>). It creates the view, subscribes its handlers
/// (<see cref="AiFgfOnAttribute"/>, or the <c>control_Event</c> names), and, by default, themes the view and gives it
/// a minimum size (<see cref="AiFgfAdoption"/>). Handlers moved from an old form keep compiling. This class forwards
/// the form members they usually use (<c>Close()</c>, <c>Text</c>, <c>Invoke</c>, <c>MessageBox.Show(this, …)</c>),
/// and <c>AI_FGF.Check mvc</c> adds a property for each control (<c>txtPath</c> → <c>View.txtPath</c>).
/// <code>
/// Application.Run(new LauncherController().View);
/// </code>
/// </summary>
public abstract class AiFgfController<TView> : IWin32Window where TView : Form, new()
{
    /// <summary>Create the view (<typeparamref name="TView"/>'s parameterless constructor), wire it and adopt it as <see cref="AiFgfMvc.DefaultAdoption"/> says.</summary>
    protected AiFgfController() : this(new TView(), AiFgfMvc.DefaultAdoption) { }

    /// <summary>Wire an existing view.</summary>
    protected AiFgfController(TView view, AiFgfAdoption adoption)
    {
        ArgumentNullException.ThrowIfNull(view);
        View = view;
        Unbound = AiFgfMvc.Wire(view, this);
        if ((adoption & AiFgfAdoption.Theme) != 0) AiFgfAdopt.Theme(view);
        if ((adoption & AiFgfAdoption.DpiLayout) != 0) AiFgfAdopt.KeepLayoutAcrossDpi(view);   // before the others: they measure the result
        if ((adoption & AiFgfAdoption.AlignRows) != 0) AiFgfAdopt.AlignRows(view);
        if ((adoption & AiFgfAdoption.MinimumSize) != 0) AutoMinSize.GuardPlaced(view);
    }

    /// <summary>The view: the form to show.</summary>
    public TView View { get; }

    /// <summary>Handlers that could not be bound (empty when every target and event exists).</summary>
    public IReadOnlyList<string> Unbound { get; }

    /// <summary>Bind <paramref name="model"/> to the view's controls (<see cref="AiFgfMvc.Bind(Control, object, bool)"/>).</summary>
    protected AiFgfBinding Bind(object model, bool fromView = false) => AiFgfMvc.Bind(View, model, fromView);

    // ---- the form members handlers moved from the form use, forwarded to the view

    /// <summary>The view's window handle (<see cref="IWin32Window"/>: <c>MessageBox.Show(this, …)</c> keeps working).</summary>
    public IntPtr Handle => View.Handle;
    public string Text { get => View.Text; set => View.Text = value; }
    public Control.ControlCollection Controls => View.Controls;
    public bool InvokeRequired => View.InvokeRequired;
    public bool IsHandleCreated => View.IsHandleCreated;
    public bool IsDisposed => View.IsDisposed;
    public bool Visible { get => View.Visible; set => View.Visible = value; }
    public bool Enabled { get => View.Enabled; set => View.Enabled = value; }
    public Cursor Cursor { get => View.Cursor; set => View.Cursor = value; }
    public Font Font { get => View.Font; set => View.Font = value; }
    public Size ClientSize { get => View.ClientSize; set => View.ClientSize = value; }
    public Size Size { get => View.Size; set => View.Size = value; }
    public Point Location { get => View.Location; set => View.Location = value; }
    public int Width { get => View.Width; set => View.Width = value; }
    public int Height { get => View.Height; set => View.Height = value; }
    public FormWindowState WindowState { get => View.WindowState; set => View.WindowState = value; }
    public DialogResult DialogResult { get => View.DialogResult; set => View.DialogResult = value; }
    public Control? ActiveControl { get => View.ActiveControl; set => View.ActiveControl = value; }
    public IButtonControl? AcceptButton { get => View.AcceptButton; set => View.AcceptButton = value; }
    public IButtonControl? CancelButton { get => View.CancelButton; set => View.CancelButton = value; }
    public Color BackColor { get => View.BackColor; set => View.BackColor = value; }
    public Color ForeColor { get => View.ForeColor; set => View.ForeColor = value; }
    public void Close() => View.Close();
    public void Show() => View.Show();
    public void Hide() => View.Hide();
    public void Activate() => View.Activate();
    public void Refresh() => View.Refresh();
    public void Invalidate() => View.Invalidate();
    public void SuspendLayout() => View.SuspendLayout();
    public void ResumeLayout() => View.ResumeLayout();
    public void ResumeLayout(bool performLayout) => View.ResumeLayout(performLayout);
    public void PerformLayout() => View.PerformLayout();
    public bool ValidateChildren() => View.ValidateChildren();
    public object Invoke(Delegate method) => View.Invoke(method);
    public object Invoke(Delegate method, params object[] args) => View.Invoke(method, args);
    public void Invoke(Action method) => View.Invoke(method);
    public T Invoke<T>(Func<T> method) => View.Invoke(method);
    public IAsyncResult BeginInvoke(Delegate method) => View.BeginInvoke(method);
    public IAsyncResult BeginInvoke(Delegate method, params object[] args) => View.BeginInvoke(method, args);
    public void BeginInvoke(Action method) => View.BeginInvoke(method);
    public DialogResult ShowDialog() => View.ShowDialog();
    public DialogResult ShowDialog(IWin32Window owner) => View.ShowDialog(owner);

    /// <summary>The controller where a form is expected (<c>dialog.ShowDialog(this)</c>, <c>new Child { Owner = this }</c>).</summary>
    public static implicit operator Form(AiFgfController<TView> controller) => controller?.View!;
}
