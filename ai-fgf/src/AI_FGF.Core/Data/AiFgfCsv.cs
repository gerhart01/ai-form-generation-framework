using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AI_FGF;

/// <summary>
/// RFC 4180-style CSV for the results grid: quoted fields may contain commas, quotes and
/// line breaks; CRLF, LF and CR line endings and a UTF-8 BOM are accepted. Real tool output
/// is messy, so duplicate header names are made unique (<c>Name</c>, <c>Name_2</c>…) and rows
/// wider than the header grow extra <c>ColumnN</c> columns instead of losing cells.
///
/// Writing guards against spreadsheet formula injection by default: a cell that would be
/// evaluated by Excel (starting with <c>=</c>, <c>@</c>, tab, CR, or a non-numeric <c>+</c>/<c>-</c>)
/// is prefixed with <c>'</c>.
/// </summary>
public static class AiFgfCsv
{
    // -------------------------------------------------------------------- read

    public static DataTable Read(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(reader);
    }

    public static DataTable Read(TextReader reader)
    {
        var dt = new DataTable();
        using IEnumerator<List<string>> records = Records(reader).GetEnumerator();
        if (!records.MoveNext()) return dt;

        foreach (string name in records.Current) AddColumn(dt, name);
        while (records.MoveNext())
        {
            List<string> cells = records.Current;
            if (cells.Count == 1 && cells[0].Length == 0) continue; // blank line
            while (cells.Count > dt.Columns.Count) AddColumn(dt, $"Column{dt.Columns.Count + 1}");
            DataRow row = dt.NewRow();
            for (int i = 0; i < dt.Columns.Count; i++) row[i] = i < cells.Count ? cells[i] : string.Empty;
            dt.Rows.Add(row);
        }
        return dt;
    }

    /// <summary>Add a column, making its name unique (DataTable names are case-insensitive).</summary>
    private static void AddColumn(DataTable dt, string name)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? $"Column{dt.Columns.Count + 1}" : name;
        string unique = baseName;
        for (int n = 2; dt.Columns.Contains(unique); n++) unique = $"{baseName}_{n}";
        dt.Columns.Add(unique);
    }

    /// <summary>A character state machine over the whole stream, so quoted fields may span lines.</summary>
    private static IEnumerable<List<string>> Records(TextReader reader)
    {
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false, any = false;
        int c;
        while ((c = reader.Read()) != -1)
        {
            char ch = (char)c;
            any = true;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { field.Append('"'); reader.Read(); }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    record.Add(field.ToString()); field.Clear();
                    break;
                case '\r':
                case '\n':
                    if (ch == '\r' && reader.Peek() == '\n') reader.Read();
                    record.Add(field.ToString()); field.Clear();
                    yield return record;
                    record = new List<string>();
                    any = false;
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }
        if (any) { record.Add(field.ToString()); yield return record; }
    }

    // ------------------------------------------------------------------- write

    /// <summary>Write a table as CSV (UTF-8 with BOM, so Excel detects the encoding), atomically.</summary>
    public static void Write(DataTable dt, string path, bool guardFormulas = true)
    {
        string tmp = path + ".tmp";
        using (var w = new StreamWriter(tmp, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            Write(dt, w, guardFormulas);
        File.Move(tmp, path, overwrite: true);
    }

    public static void Write(DataTable dt, TextWriter writer, bool guardFormulas = true)
    {
        writer.Write(string.Join(',', dt.Columns.Cast<DataColumn>().Select(c => Field(c.ColumnName, guardFormulas))));
        writer.Write("\r\n");
        foreach (DataRow row in dt.Rows)
        {
            writer.Write(string.Join(',', row.ItemArray.Select(v => Field(v is null or DBNull ? "" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "", guardFormulas))));
            writer.Write("\r\n");
        }
    }

    /// <summary>True if a spreadsheet would evaluate this cell as a formula.</summary>
    public static bool IsFormulaLike(string value)
    {
        if (value.Length == 0) return false;
        char first = value[0];
        if (first is '=' or '@' or '\t' or '\r') return true;
        if (first is '+' or '-')
            return !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _); // "-5" stays a number
        return false;
    }

    private static string Field(string value, bool guardFormulas)
    {
        if (guardFormulas && IsFormulaLike(value)) value = "'" + value;
        bool needsQuotes = value.IndexOfAny([',', '"', '\n', '\r']) >= 0 || value.StartsWith(' ') || value.EndsWith(' ');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
