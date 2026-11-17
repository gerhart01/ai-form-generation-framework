#if NET11_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AI_FGF;

/// <summary>What kind of field something is dropped on (<see cref="AiFgfDrop"/>).</summary>
public enum DropTarget
{
    /// <summary>A text field: a dropped file or folder gives its path, dropped text the text.</summary>
    Text,
    /// <summary>A file field: a dropped file that matches the field's filter.</summary>
    File,
    /// <summary>A folder field: a dropped folder, or the folder of a dropped file.</summary>
    Folder,
    /// <summary>A multi-line field: each dropped path (or the text) on a line of its own, added to what is there.</summary>
    Memo,
}

/// <summary>
/// .NET 11: drag and drop onto fields - the decision, shared by the WinForms and Avalonia views (they only wire the events):
/// from what is dragged (paths and/or text) and the field's kind, the field's new value, or null when the field takes
/// none of it (the drag then shows "no drop").
/// </summary>
public static class AiFgfDrop
{
    /// <summary>
    /// The field's new value for a drop of <paramref name="paths"/> (files and folders) or <paramref name="text"/>, or null
    /// when it accepts neither. <paramref name="filter"/> is a file field's "Description|*.ext;*.ext2|…" filter.
    /// </summary>
    public static string? Value(DropTarget target, IReadOnlyList<string>? paths, string? text, string current = "", string? filter = null)
    {
        var dropped = (paths ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        switch (target)
        {
            case DropTarget.File:
                return dropped.FirstOrDefault(p => !Directory.Exists(p) && Matches(p, filter));
            case DropTarget.Folder:
                foreach (string p in dropped)
                {
                    if (Directory.Exists(p)) return p;
                    if (File.Exists(p) && Path.GetDirectoryName(p) is { Length: > 0 } dir) return dir;
                }
                return null;
            case DropTarget.Memo:
            {
                var lines = dropped.Count > 0 ? dropped : Lines(text);
                if (lines.Count == 0) return null;
                // the box's own line break: "\n" only where its text already uses bare ones, else Windows' "\r\n"
                string sep = current.Contains('\n') && !current.Contains("\r\n", StringComparison.Ordinal) ? "\n" : "\r\n";
                string head = current.Length == 0 || current.EndsWith('\n') ? current : current + sep;
                return head + string.Join(sep, lines);
            }
            default:
                if (dropped.Count > 0) return dropped[0];
                string? line = Lines(text).FirstOrDefault();   // a one-line field takes the first line of dropped text
                return line;
        }
    }

    /// <summary>True when a drop of these would change the field (the drag shows "copy"), else "no drop".</summary>
    public static bool Accepts(DropTarget target, IReadOnlyList<string>? paths, string? text, string? filter = null)
        => Value(target, paths, text, "", filter) is not null;

    /// <summary>True when <paramref name="path"/> matches one of the patterns of a "Description|*.ext;*.ext2|…" filter (none = any).</summary>
    public static bool Matches(string path, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        string name = Path.GetFileName(path);
        string[] parts = filter.Split('|');
        for (int i = 1; i < parts.Length; i += 2)
            foreach (string pattern in parts[i].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (pattern is "*" or "*.*" || Wildcard(pattern).IsMatch(name)) return true;   // "*.*" is any file, as in Windows' dialogs
        return parts.Length < 2;
    }

    private static Regex Wildcard(string pattern)
        => new("^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<string> Lines(string? text)
        => (text ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
}
#endif
