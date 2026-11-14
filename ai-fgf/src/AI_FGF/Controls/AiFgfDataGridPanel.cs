using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AI_FGF;

/// <summary>
/// A styled results grid with a live "filter across all columns" box and a CSV export
/// button — the Results tab that the reference GUIs built by hand. Feed it a
/// <see cref="DataTable"/> (or a CSV file) and it renders, filters and exports.
/// </summary>
public sealed class AiFgfDataGridPanel : UserControl
{
    private readonly DataGridView _grid;
    private readonly TextBox _filter;
    private readonly Label _info;
    private DataTable? _table;

    /// <summary>Raised when a cell is clicked; carries the clicked column name and value.</summary>
    public event Action<string, string>? CellActivated;

    public AiFgfDataGridPanel()
    {
        var t = AiFgfTheme.Current;

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AllowUserToResizeColumns = true,
        };
        StyleGrid(_grid);
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string col = _grid.Columns[e.ColumnIndex].HeaderText;
            string val = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "";
            CellActivated?.Invoke(col, val);
        };

        // AutoSize + wrap: on a narrow window the buttons move to a second row instead of
        // being clipped by a fixed-height strip.
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(8, 3, 8, 6),
            BackColor = t.App,
        };
        AiFgf.CentreRows(bar);   // label, box and buttons on one centre line at every DPI
        bar.Controls.Add(new Label { Text = AiFgfText.Filter, AutoSize = true, Margin = new Padding(0, 8, 6, 0), ForeColor = t.Text });
        _filter = new TextBox
        {
            Width = 300,
            Margin = new Padding(0, 4, 6, 0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = t.Surface,
            ForeColor = t.Text,
            PlaceholderText = AiFgfText.FilterHint,
        };
        _filter.TextChanged += (_, _) => ApplyFilter();
        _info = new Label { Text = "", AutoSize = true, Margin = new Padding(10, 8, 0, 0), ForeColor = t.Subtle };
        bar.Controls.Add(_filter);
        AiFgf.FitWidthToBar(_filter, bar, 300);
        bar.Controls.Add(AiFgf.SmallButton("✕", (_, _) => { _filter.Clear(); _filter.Focus(); }));
        bar.Controls.Add(AiFgf.SmallButton(AiFgfText.Export, (_, _) => ExportView()));
        bar.Controls.Add(_info);

        Controls.Add(_grid); // Fill first
        Controls.Add(bar);   // then Top
    }

    /// <summary>The underlying grid, for callers that want finer control.</summary>
    public DataGridView Grid => _grid;

    /// <summary>Show a table. Pass null to clear.</summary>
    public void Show(DataTable? table)
    {
        _table = table;
        ApplyFilter();
    }

    /// <summary>Load a CSV file (comma-separated, simple quoting) into the grid.</summary>
    public void ShowCsv(string path)
    {
        Show(ReadCsv(path));
    }

    // ---------------------------------------------------------------- filter

    private void ApplyFilter()
    {
        if (_table is null) { _grid.DataSource = null; _info.Text = ""; return; }

        string term = _filter.Text.Trim();
        if (term.Length == 0)
        {
            _grid.DataSource = _table;
            _info.Text = AiFgfText.Format(AiFgfText.Rows, _table.Rows.Count);
            return;
        }

        DataTable view = _table.Clone();
        foreach (DataRow row in _table.Rows)
        {
            if (row.ItemArray.Any(v => v?.ToString()?.Contains(term, StringComparison.OrdinalIgnoreCase) == true))
                view.ImportRow(row);
        }
        _grid.DataSource = view;
        _info.Text = AiFgfText.Format(AiFgfText.RowsFiltered, view.Rows.Count, _table.Rows.Count);
    }

    private void ExportView()
    {
        if (_grid.DataSource is not DataTable dt || dt.Rows.Count == 0) return;
        string? path = AiFgf.PickSaveFile("CSV|*.csv|" + AiFgfText.AllFilesFilter, AiFgfText.ExportTitle, "export.csv");
        if (path is not null) ExportTo(path);
    }

    /// <summary>
    /// Export the rows currently shown (after filtering) to <paramref name="path"/>. Never
    /// throws for I/O problems — a file locked by Excel or a protected folder is reported in the
    /// bar and the method returns false; the target is left untouched (written atomically).
    /// </summary>
    public bool ExportTo(string path, bool guardFormulas = true)
    {
        if (_grid.DataSource is not DataTable dt) return false;
        try
        {
            AiFgfCsv.Write(dt, path, guardFormulas);
            _info.ForeColor = AiFgfTheme.Current.Subtle;
            _info.Text = AiFgfText.Format(AiFgfText.Exported, dt.Rows.Count);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(path + ".tmp"); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            _info.ForeColor = AiFgfTheme.Current.LogError;
            _info.Text = AiFgfText.Format(AiFgfText.ExportFailed, ex.Message);
            return false;
        }
    }

    /// <summary>The text of the bar's status label (row counts, export result).</summary>
    public string StatusText => _info.Text;

    // ------------------------------------------------------------ CSV helpers

    /// <summary>Read a CSV file (see <see cref="AiFgfCsv"/> for the dialect).</summary>
    public static DataTable ReadCsv(string path) => AiFgfCsv.Read(path);

    /// <summary>Write a table as CSV; formula-like cells are guarded unless <paramref name="guardFormulas"/> is false.</summary>
    public static void WriteCsv(DataTable dt, string path, bool guardFormulas = true) => AiFgfCsv.Write(dt, path, guardFormulas);

    // ----------------------------------------------------------------- style

    public static void StyleGrid(DataGridView g)
    {
        var t = AiFgfTheme.Current;
        g.BackgroundColor = t.Surface;
        g.BorderStyle = BorderStyle.None;
        g.EnableHeadersVisualStyles = false;
        g.GridColor = t.GridLines;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.AllowUserToResizeRows = false;

        g.ColumnHeadersDefaultCellStyle.BackColor = t.GridHeader;
        g.ColumnHeadersDefaultCellStyle.ForeColor = t.Text;
        // Without these, DataGridView paints the header of whichever column holds the current
        // cell with its own default (system-blue) selection colours, not the theme's - one header
        // visibly mismatched from the rest whenever a cell is selected.
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = t.GridHeader;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = t.Text;
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

        g.RowHeadersDefaultCellStyle.BackColor = t.GridHeader;
        g.RowHeadersDefaultCellStyle.ForeColor = t.Text;
        g.RowHeadersDefaultCellStyle.SelectionBackColor = t.GridHeader;
        g.RowHeadersDefaultCellStyle.SelectionForeColor = t.Text;

        g.DefaultCellStyle.BackColor = t.Surface;
        g.DefaultCellStyle.ForeColor = t.Text;
        g.DefaultCellStyle.SelectionBackColor = t.GridSelection;
        g.DefaultCellStyle.SelectionForeColor = t.Text;
        g.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        g.AlternatingRowsDefaultCellStyle.BackColor = t.GridAlt;

        // Row and header heights follow the font, not fixed pixels: the grid's font is
        // inherited and rescaled on a DPI change, but RowTemplate.Height/ColumnHeadersHeight are
        // not control bounds, so nothing else would rescale them.
        FitToFont(g);
        g.FontChanged -= OnGridFontChanged;
        g.FontChanged += OnGridFontChanged;
    }

    private static void OnGridFontChanged(object? sender, EventArgs e)
    {
        if (sender is DataGridView g) FitToFont(g);
    }

    /// <summary>Size header/rows from the grid's current font (and rebuild the semibold header font).</summary>
    public static void FitToFont(DataGridView g)
    {
        Font baseFont = g.Font;
        Font header = new($"{AiFgfTheme.Current.UiFontFamily} Semibold", baseFont.Size, FontStyle.Regular, baseFont.Unit);
        Font? old = g.ColumnHeadersDefaultCellStyle.Font;
        g.ColumnHeadersDefaultCellStyle.Font = header;
        if (old is not null && !ReferenceEquals(old, baseFont)) old.Dispose();

        int line = baseFont.Height;
        g.ColumnHeadersHeight = header.Height + line * 4 / 5;   // ≈ 34 px at 9.25 pt / 96 DPI
        int rowHeight = line + line / 2;                          // ≈ 26 px at 9.25 pt / 96 DPI
        g.RowTemplate.Height = rowHeight;
        if (g.Rows.Count <= 10_000)                              // RowTemplate only affects new rows
            foreach (DataGridViewRow row in g.Rows) row.Height = rowHeight;
    }
}
