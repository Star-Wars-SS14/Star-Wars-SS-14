using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate;

public sealed class SWSenateInfoConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateInfoConsoleComponent, BoundUIOpenedEvent>(OnOpened);
    }

    private void OnOpened(EntityUid uid, SWSenateInfoConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<SWSenateInfoConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWSenateInfoConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<SWSenateInfoConsoleComponent> ent)
    {
        var sectors = new List<SWSectorOccupancy>();
        foreach (var proto in _proto.EnumeratePrototypes<SWSectorPrototype>())
        {
            sectors.Add(new SWSectorOccupancy
            {
                Id = proto.ID,
                Name = proto.Name,
                Color = proto.Color,
                Holder = _senate.GetSectorHolder(proto.ID),
            });
        }

        sectors.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        _ui.SetUiState(ent.Owner, SWSenateInfoConsoleUiKey.Key,
            new SWSenateInfoConsoleState(sectors, _senate.Chancellor, _senate.Speaker, _senate.BuildScheduleRows(), _time.TimeString(), _senate.BuildActiveLaws()));
    }
}
