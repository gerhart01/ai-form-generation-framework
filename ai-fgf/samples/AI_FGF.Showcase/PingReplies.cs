using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AI_FGF.Showcase;

/// <summary>
/// The model of the showcase's run: the replies <c>ping</c> prints, as rows. Reads the time and TTL of a reply line in
/// any Windows language ("time=12ms TTL=118", "время=12мс TTL=118": TTL is never translated). No UI types.
/// </summary>
public sealed partial class PingReplies
{
    private readonly DataTable _table = new("Replies");

    public PingReplies()
    {
        _table.Columns.Add("#", typeof(int));
        _table.Columns.Add("Time, ms", typeof(int));
        _table.Columns.Add("TTL", typeof(int));
    }

    /// <summary>The replies so far (a new run starts with <see cref="Clear"/>).</summary>
    public DataTable Table => _table;

    public int Count => _table.Rows.Count;

    public void Clear() => _table.Rows.Clear();

    /// <summary>Add the reply in <paramref name="line"/>, if it is one; true when it was.</summary>
    public bool Add(string line)
    {
        if (Parse(line) is not { } r) return false;
        _table.Rows.Add(_table.Rows.Count + 1, r.Milliseconds, r.Ttl);
        return true;
    }

    /// <summary>A reply's round-trip time and TTL, or null for any other line.</summary>
    public static (int Milliseconds, int Ttl)? Parse(string line)
    {
        Match m = Reply().Match(line);
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    // the token right before TTL: "time=12ms TTL=118", "time<1ms TTL=128", "время=12мс TTL=118" - not "bytes=32", which
    // comes first on the line and is followed by the time, not by TTL
    [GeneratedRegex(@"[=<](\d+)\S*\s+TTL=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Reply();
}
