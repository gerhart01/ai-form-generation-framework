using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace AI_FGF.Avalonia;

/// <summary>
/// The results grid — the same API as the WinForms <c>AiFgfDataGridPanel</c> (<see cref="Show"/>,
/// <see cref="ShowCsv"/>, <see cref="ExportTo"/>, <see cref="StatusText"/>): a filter bar
/// ("Filter:" box across all columns, ✕, Export…, row count) above a read-only grid.
/// </summary>
public sealed class AiFgfDataGridPanel : DockPanel
{
    private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true };
    private readonly TextBox _filter;
    private readonly TextBlock _info;
    private DataTable? _table;

    public AiFgfDataGridPanel()
    {
        _filter = new TextBox { Width = 300, Watermark = AiFgfText.FilterHint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _filter.TextChanged += (_, _) => ApplyFilter();
        _info = AiFgf.Label("", subtle: true);
        _info.Margin = new Thickness(10, 0, 0, 0);
        var bar = new WrapPanel { Margin = new Thickness(8, 3, 8, 6) };
        bar.Children.Add(new TextBlock { Text = AiFgfText.Filter, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        bar.Children.Add(_filter);
        bar.Children.Add(AiFgf.SmallButton("✕", (_, _) => { _filter.Text = ""; _filter.Focus(); }));
        bar.Children.Add(AiFgf.SmallButton(AiFgfText.Export, async (_, _) =>
        {
            if (_table is null || _table.Rows.Count == 0) return;
            if (await AiFgf.PickSaveFileAsync(TopLevel.GetTopLevel(this), "CSV|*.csv|" + AiFgfText.AllFilesFilter, AiFgfText.ExportTitle, "export.csv") is { } path) ExportTo(path);
        }));
        bar.Children.Add(_info);
        bar.SizeChanged += (_, _) =>
        {
            double others = bar.Children.Where(c => c != _filter).Sum(c => c.DesiredSize.Width);
            _filter.Width = Math.Clamp(bar.Bounds.Width - others - 24, 80, 300);
        };
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        Children.Add(_grid);
    }

    public DataGrid Grid => _grid;

    public string StatusText => _info.Text ?? "";

    /// <summary>Show a table (null clears).</summary>
    public void Show(DataTable? table)
    {
        _table = table;
        _grid.Columns.Clear();
        if (table is not null)
            for (int i = 0; i < table.Columns.Count; i++)
                _grid.Columns.Add(new DataGridTextColumn { Header = table.Columns[i].ColumnName, Binding = new Binding($"[{i}]") });
        ApplyFilter();
    }

    public void ShowCsv(string path) => Show(AiFgfCsv.Read(path));

    /// <summary>The rows shown (after the filter), as cell strings.</summary>
    public IReadOnlyList<string[]> VisibleRows { get; private set; } = Array.Empty<string[]>();

    private void ApplyFilter()
    {
        if (_table is null) { _grid.ItemsSource = null; VisibleRows = Array.Empty<string[]>(); _info.Text = ""; return; }
        string term = (_filter.Text ?? "").Trim();
        var all = _table.Rows.Cast<DataRow>().Select(r => r.ItemArray.Select(v => v?.ToString() ?? "").ToArray()).ToList();
        VisibleRows = term.Length == 0 ? all : all.Where(r => r.Any(c => c.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        _grid.ItemsSource = VisibleRows;
        _info.Text = term.Length == 0 ? AiFgfText.Format(AiFgfText.Rows, all.Count) : AiFgfText.Format(AiFgfText.RowsFiltered, VisibleRows.Count, all.Count);
    }

    /// <summary>Write the shown rows as CSV (formula cells guarded). False + a message in the bar on failure.</summary>
    public bool ExportTo(string path, bool guardFormulas = true)
    {
        if (_table is null) return false;
        try
        {
            var view = _table.Clone();
            foreach (string[] row in VisibleRows) view.Rows.Add(row.Cast<object>().ToArray());
            AiFgfCsv.Write(view, path, guardFormulas);
            _info.Text = AiFgfText.Format(AiFgfText.Exported, view.Rows.Count);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _info.Text = AiFgfText.Format(AiFgfText.ExportFailed, ex.Message);
            return false;
        }
    }
}
