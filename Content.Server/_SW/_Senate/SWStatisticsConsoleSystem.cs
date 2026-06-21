using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate;

public sealed class SWStatisticsConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWStatisticsConsoleComponent, BoundUIOpenedEvent>(OnOpened);
    }

    private void OnOpened(EntityUid uid, SWStatisticsConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<SWStatisticsConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWStatisticsConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<SWStatisticsConsoleComponent> ent)
    {
        var republic = BuildStatEntries(_senate.GetRepublicStats());

        var sectors = new List<SWSectorEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWSectorPrototype>())
        {
            if (!_senate.SectorStats.TryGetValue(proto.ID, out var stats))
                continue;

            sectors.Add(new SWSectorEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Color = proto.Color,
                Claimed = _senate.IsClaimed(proto.ID),
                Stats = BuildStatEntries(stats),
            });
        }

        sectors.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        _ui.SetUiState(ent.Owner, SWStatisticsConsoleUiKey.Key, new SWStatisticsConsoleState(republic, sectors));
    }

    private List<SWStatEntry> BuildStatEntries(Dictionary<string, float> stats)
    {
        var list = new List<SWStatEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!stats.TryGetValue(proto.ID, out var value))
                continue;

            list.Add(new SWStatEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                ShortName = proto.ShortName,
                Color = proto.Color,
                Value = value,
                Min = proto.Min,
                Max = proto.Max,
            });
        }

        return list;
    }
}
