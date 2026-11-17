using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using K = AI_FGF.AiFgfLayoutSnapshot.Kinds;

namespace AI_FGF.Avalonia;

/// <summary>
/// Takes an <see cref="AiFgfLayoutSnapshot"/> of a shown Avalonia window — the same picture the WinForms
/// <c>AiFgfSnapshot.Of(form)</c> takes, so the two versions of one form can be compared element by element
/// (<see cref="AiFgfLayoutSnapshot.Compare"/>): text, logical bounds and the font actually used.
/// </summary>
public static class AiFgfSnapshot
{
    /// <summary>Marks the caption of a parameters frame (the WinForms GroupBox text).</summary>
    public const string GroupCaptionClass = "ai-fgf-group-caption";

    /// <summary>Marks the status-bar text (the WinForms StatusStrip label).</summary>
    public const string StatusClass = "ai-fgf-status";

    public static AiFgfLayoutSnapshot Of(TopLevel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var snap = new AiFgfLayoutSnapshot
        {
            Toolkit = "Avalonia",
            Width = AiFgfLayoutSnapshot.R(window.ClientSize.Width),
            Height = AiFgfLayoutSnapshot.R(window.ClientSize.Height),
        };

        void Add(string kind, string text, Visual at, Visual? font = null)
        {
            Point p = at.TranslatePoint(default, window) ?? default;
            (string name, double pt, bool bold) = Font(font ?? at);
            snap.Elements.Add(new AiFgfElement(kind, text, AiFgfLayoutSnapshot.R(p.X), AiFgfLayoutSnapshot.R(p.Y),
                AiFgfLayoutSnapshot.R(at.Bounds.Width), AiFgfLayoutSnapshot.R(at.Bounds.Height), name, pt, bold));
        }
        string Plain(string? s) => AiFgfLayoutSnapshot.Plain(s, '_');

        void Walk(Visual parent)
        {
            foreach (Visual v in parent.GetVisualChildren())
            {
                if (!v.IsVisible || v.Bounds.Width <= 0 && v.Bounds.Height <= 0) continue;
                switch (v)
                {
                    case ScrollBar: break;
                    case TextBlock t when t.Classes.Contains(GroupCaptionClass): Add(K.Group, t.Text ?? "", t.GetVisualParent() ?? t, t); break;
                    case TextBlock t when t.Classes.Contains(StatusClass): Add(K.Status, t.Text ?? "", t); break;
                    case SelectableTextBlock log: Add(K.Log, "", log.FindAncestorOfType<ScrollViewer>() ?? (Visual)log, log); break;
                    case TabItem tab: Add(K.Tab, Plain(AiFgf.Caption(tab.Header)), tab); break;
                    case CheckBox or RadioButton or ToggleSwitch: Add(K.Check, Plain(AiFgf.Caption(((ContentControl)v).Content)), v); break;
                    case Button b: Add(K.Button, Plain(AiFgf.Caption(b.Content)), b); break;
                    case NumericUpDown n: Add(K.Number, n.Text ?? "", n); break;
                    case ComboBox c: Add(K.Combo, (c.IsEditable ? c.Text : c.SelectedItem?.ToString()) ?? "", c); break;
                    case TextBox t: Add(K.Text, t.Text ?? "", t); break;
                    case DataGrid g: Add(K.Grid, "", g); break;
                    case ProgressBar: Add(K.Progress, "", v); snap.Elements[^1] = snap.Elements[^1] with { Font = "", FontPt = 0, Bold = false }; break;
                    case Label l: Add(K.Label, Plain(AiFgf.Caption(l.Content)), l); break;
                    case TextBlock t when !string.IsNullOrEmpty(t.Text): Add(K.Label, t is AccessText ? Plain(t.Text) : t.Text!, t); break;
                    default: Walk(v); break;
                }
            }
        }
        Walk(window);
        return snap;
    }

    /// <summary>The font that renders <paramref name="v"/>'s text: the first family of its list that exists, in points.</summary>
    internal static (string Name, double Pt, bool Bold) Font(Visual v)
    {
        (FontFamily family, double size, FontWeight weight, FontStyle style) = v switch
        {
            TemplatedControl c => (c.FontFamily, c.FontSize, c.FontWeight, c.FontStyle),
            TextBlock t => (t.FontFamily, t.FontSize, t.FontWeight, t.FontStyle),
            _ => (FontFamily.Default, 0, FontWeight.Normal, FontStyle.Normal),
        };
        if (size <= 0) return ("", 0, false);
        string name = FontManager.Current.TryGetGlyphTypeface(new Typeface(family, style, weight), out IGlyphTypeface? face)
            ? face.FamilyName : family.FamilyNames.FirstOrDefault() ?? family.Name;
        return (name, Math.Round(size * 72 / 96, 2), weight >= FontWeight.SemiBold);
    }
}
