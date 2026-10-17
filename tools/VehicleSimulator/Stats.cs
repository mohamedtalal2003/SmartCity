using System.Collections.Concurrent;

/// <summary>Counts every reply type, for the summary line every 10 s and the totals at the end.</summary>
public sealed class Stats
{
    private readonly ConcurrentDictionary<string, int> _sinceLastSummary = new();
    private readonly ConcurrentDictionary<string, int> _total = new();

    /// <summary>E.g. "SAVED", "REJECTED:OutsideServiceArea", "UNAVAILABLE", "NETWORK", "EXPIRED".</summary>
    public void Count(string reply)
    {
        _sinceLastSummary.AddOrUpdate(reply, 1, (_, n) => n + 1);
        _total.AddOrUpdate(reply, 1, (_, n) => n + 1);
    }

    /// <summary>The counts since the last call, then starts counting again.</summary>
    public string TakeRecent()
    {
        var text = Format(_sinceLastSummary);
        _sinceLastSummary.Clear();
        return text;
    }

    public string Totals() => Format(_total);

    private static string Format(ConcurrentDictionary<string, int> counts) =>
        counts.IsEmpty ? "no replies" : string.Join(" ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"));
}
