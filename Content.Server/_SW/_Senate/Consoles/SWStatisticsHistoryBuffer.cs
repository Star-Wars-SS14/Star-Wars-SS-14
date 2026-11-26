using System.Linq;
using Content.Shared._SW._Senate;

namespace Content.Server._SW._Senate.Consoles;

/// <summary>Bounded round history, shared by every statistics terminal and collected even while they are closed.</summary>
public sealed class SWStatisticsHistoryBuffer
{
    public const int Capacity = 144;
    public const int IntervalMinutes = 10;
    private readonly List<SWStatisticsSample> _samples = new();
    private int? _lastObservedMinute;

    public void Clear()
    {
        _samples.Clear();
        _lastObservedMinute = null;
    }

    public void Record(int minute, IReadOnlyList<SWGlobalStatEntry> globals)
    {
        // Admin clock rewinds must not connect unrelated observations with a backwards graph.
        if (_lastObservedMinute is { } previous && minute < previous)
            Clear();
        _lastObservedMinute = minute;
        if (globals.Count == 0 || _samples.Count > 0 && minute - _samples[^1].Minute < IntervalMinutes)
            return;
        _samples.Add(new SWStatisticsSample
        {
            Minute = minute,
            Values = globals.Where(g => float.IsFinite(g.Actual) && float.IsFinite(g.Policy)).Select(g =>
                new SWStatisticsValue { Id = g.Id, Actual = g.Actual, Policy = g.Policy }).ToList(),
        });
        if (_samples.Count > Capacity)
            _samples.RemoveAt(0);
    }

    public List<SWStatisticsSample> Snapshot() => _samples.ToList();
}
