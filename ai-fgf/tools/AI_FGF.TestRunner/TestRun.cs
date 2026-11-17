using System.Text.RegularExpressions;

namespace AI_FGF.TestRunner;

public enum ShardState { Waiting, Running, Passed, Failed }

/// <summary>One process of the test run (a LayoutTests shard, an AvaloniaTests or an end-to-end run).</summary>
public sealed class Shard(string name, int expected)
{
    public string Name { get; } = name;
    /// <summary>Cases the previous run of this shard had (0 = unknown).</summary>
    public int Expected { get; } = expected;
    public ShardState State { get; set; }
    public int Cases { get; set; }
    public string Current { get; set; } = "";
    public int Passed { get; set; }
    public int Total { get; set; }
    public int Seconds { get; set; }
    /// <summary>When it started running (null while waiting).</summary>
    public DateTime? StartedAt { get; set; }
    /// <summary>How long it took the last time (seconds, 0 = unknown): for the estimate of what is left.</summary>
    public int LastSeconds { get; set; }

    /// <summary>Seconds so far: the final time once finished, the running time while running.</summary>
    public int Elapsed(DateTime now) => State is ShardState.Passed or ShardState.Failed ? Seconds : StartedAt is { } t ? (int)(now - t).TotalSeconds : 0;

    /// <summary>How far it is, 0..1: a finished shard 1, a running one its share of the previous run's cases.</summary>
    public double Fraction => State is ShardState.Passed or ShardState.Failed ? 1 : State == ShardState.Running && Expected > 0 ? Math.Min(0.95, Cases / (double)Expected) : 0;
}

/// <summary>
/// The model of a test run, fed with the lines tools/Run-Tests.ps1 prints: <c>##PLAN##|name|expected</c> (the
/// shards, before anything starts), <c>##SHARD##|name|running</c>, <c>  ·    [name] case</c> (a case starts) and
/// <c>##SHARD##|name|ok|passed|total|seconds</c> (or <c>fail</c>). No UI types.
/// </summary>
public sealed partial class TestRun
{
    private readonly List<Shard> _shards = new();
    private bool _planComplete;

    public IReadOnlyList<Shard> Shards => _shards;

    /// <summary>The run as a whole, 0..1.</summary>
    public double Overall => _shards.Count == 0 ? 0 : _shards.Sum(s => s.Fraction) / _shards.Count;

    public int Checks => _shards.Sum(s => s.Total);
    public int Failed => _shards.Sum(s => s.Total - s.Passed) + _shards.Count(s => s.State == ShardState.Failed && s.Total == 0);

    /// <summary>A new run: the next plan line starts a new list.</summary>
    public void Reset() { _shards.Clear(); _planComplete = false; }

    /// <summary>True for the lines this model reads (the view keeps them out of its log).</summary>
    public static bool IsModelLine(string line) => line.StartsWith("##PLAN##|", StringComparison.Ordinal) || line.StartsWith("##SHARD##|", StringComparison.Ordinal) || CaseLine().IsMatch(line);

    /// <summary>Apply one line; returns the shard it changed (null when none or the plan grew).</summary>
    public Shard? Feed(string line)
    {
        string[] p = line.Split('|');
        if (p[0] == "##PLAN##" && p.Length >= 3)
        {
            if (_planComplete) Reset();
            _shards.Add(new Shard(p[1], int.TryParse(p[2], out int e) ? e : 0));
            return null;
        }
        if (p[0] == "##SHARD##" && p.Length >= 3 && Find(p[1]) is { } s)
        {
            _planComplete = true;
            if (p[2] == "running") { s.State = ShardState.Running; s.StartedAt = DateTime.Now; }
            else
            {
                s.State = p[2] == "ok" ? ShardState.Passed : ShardState.Failed;
                if (p.Length >= 6) { s.Passed = int.Parse(p[3]); s.Total = int.Parse(p[4]); s.Seconds = int.Parse(p[5]); }
                s.Current = "";
            }
            return s;
        }
        if (CaseLine().Match(line) is { Success: true } m && Find(m.Groups[1].Value) is { } c)
        {
            c.Cases++;
            c.Current = m.Groups[2].Value;
            return c;
        }
        return null;
    }

    /// <summary>
    /// The time left, estimated from the last run's durations: the LayoutTests shards run one after another, so their
    /// remaining times add up; the AvaloniaTests runs go alongside and only count when they would take longer. Null
    /// when no shard has a last duration yet.
    /// </summary>
    public TimeSpan? Remaining(DateTime now)
    {
        if (_shards.All(s => s.LastSeconds == 0)) return null;
        double Left(Shard s) => s.State is ShardState.Passed or ShardState.Failed ? 0 : Math.Max(0, s.LastSeconds - s.Elapsed(now));
        double layout = _shards.Where(s => !s.Name.EndsWith("avalonia", StringComparison.Ordinal)).Sum(Left);
        double avalonia = _shards.Where(s => s.Name.EndsWith("avalonia", StringComparison.Ordinal)).Sum(Left);
        return TimeSpan.FromSeconds(Math.Max(layout, avalonia));
    }

    /// <summary>Apply the durations of the previous run to the shards of this one.</summary>
    public void ApplyLast(IReadOnlyDictionary<string, int> last)
    {
        foreach (Shard s in _shards) if (last.TryGetValue(s.Name, out int sec)) s.LastSeconds = sec;
    }

    private Shard? Find(string name) => _shards.FirstOrDefault(s => s.Name == name);

    [GeneratedRegex(@"^\s+·\s+\[(.+?)\] (.*)$")]
    private static partial Regex CaseLine();
}
