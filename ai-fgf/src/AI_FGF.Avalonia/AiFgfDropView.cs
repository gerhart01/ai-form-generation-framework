#if NET11_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AI_FGF.Avalonia;

/// <summary>
/// .NET 11: drag and drop onto an Avalonia field - the view's half, as on WinForms: it reads what is dragged (files and
/// folders, text), asks <see cref="AiFgfDrop"/> (the shared decision) and shows "copy" or "no drop"; a drop sets the
/// field's text. The rows of <see cref="FormScaffold"/> - text, file, folder, memo - take drops by themselves.
/// </summary>
public static class AiFgfDropView
{
    /// <summary>Let <paramref name="box"/> take drops as a <paramref name="target"/> field (a file field: matching <paramref name="filter"/>).</summary>
    public static T Accept<T>(T box, DropTarget target, string? filter = null) where T : TextBox
    {
        ArgumentNullException.ThrowIfNull(box);
        DragDrop.SetAllowDrop(box, true);
        bool Takes(DragEventArgs e) => box.IsEnabled && !box.IsReadOnly && Read(e) is var (paths, text) && AiFgfDrop.Accepts(target, paths, text, filter);
        box.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = Takes(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        box.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            e.Handled = true;
            if (!box.IsEnabled || box.IsReadOnly) return;
            var (paths, text) = Read(e);
            if (AiFgfDrop.Value(target, paths, text, box.Text ?? "", filter) is not { } value) return;
            box.Text = value;
            box.CaretIndex = value.Length;
            box.Focus();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        return box;
    }

    /// <summary>The local paths and the text of a drag (either may be null).</summary>
    internal static (IReadOnlyList<string>? Paths, string? Text) Read(DragEventArgs e)
    {
#pragma warning disable CS0618   // DragEventArgs.Data, DataFormats: the data-transfer API that replaces them is not in every Avalonia 11 release this builds with
        IDataObject data = e.Data;
        var paths = data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
        string? text = data.Contains(DataFormats.Text) ? data.GetText() : null;
#pragma warning restore CS0618
        return (paths, text);
    }
}
#endif
