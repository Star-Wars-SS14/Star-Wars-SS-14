using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>
/// The changes of owner of one campaign, for the timeline of the verdict. It only collects them: nothing in the
/// campaign reads it. A new generation starts it over, and past the limit a change is not recorded.
/// </summary>
public sealed class SWGlobalWarWarTimeline
{
    public const int Limit = 200;

    private readonly List<SWGlobalWarWarTick> _ticks = new();
    private Guid _generation;

    public void Record(Guid generation, TimeSpan at, SWGlobalWarFaction owner)
    {
        if (_generation != generation)
        {
            _ticks.Clear();
            _generation = generation;
        }

        if (_ticks.Count < Limit)
            _ticks.Add(new SWGlobalWarWarTick(at, owner));
    }

    /// <summary>A copy for the network message. Another generation's changes never reach this one's verdict.</summary>
    public List<SWGlobalWarWarTick> Snapshot(Guid generation)
    {
        return generation == _generation ? new List<SWGlobalWarWarTick>(_ticks) : new List<SWGlobalWarWarTick>();
    }

    public void Clear()
    {
        _ticks.Clear();
        _generation = Guid.Empty;
    }
}
