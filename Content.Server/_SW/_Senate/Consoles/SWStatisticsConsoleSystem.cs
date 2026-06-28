using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWStatisticsConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateCrisisSystem _crisis = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
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
        var globals = _senate.BuildGlobalEntries();

        var sectors = new List<SWSectorEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWSectorPrototype>())
        {
            if (!_senate.SectorGlobals.ContainsKey(proto.ID))
                continue;

            sectors.Add(new SWSectorEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Color = proto.Color,
                Claimed = _senate.IsClaimed(proto.ID),
                Stats = _senate.BuildSectorGlobals(proto.ID),
            });
        }

        sectors.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        var stance = _defense.Stance;
        var state = new SWStatisticsConsoleState(globals, sectors)
        {
            Crises = _crisis.BuildCrises(null, false),
            ActiveLaws = _senate.BuildActiveLaws(),
            GameTime = _time.TimeString(),
            DefconLevel = SWMinistryDefenseSystem.DefconLevel(stance),
            DefconName = SWMinistryDefenseSystem.StanceName(stance),
            DefconColor = SWMinistryDefenseSystem.StanceColor(stance),
            BorderPressure = _defense.BorderPressure,
            BorderStatus = _defense.BorderStatus(),
            DefconEffects = _defense.BuildStanceEffects(stance),
            PostureName = SWMinistryInternalSecuritySystem.PostureName(_security.Posture),
            PostureColor = SWMinistryInternalSecuritySystem.PostureColor(_security.Posture),
            Threat = _security.Threat,
            ThreatStatus = _security.ThreatStatus(),
            Unrest = _security.Unrest,
            UnrestStatus = _security.UnrestStatus(),
            PostureEffects = _security.BuildPostureEffects(_security.Posture),
        };

        _ui.SetUiState(ent.Owner, SWStatisticsConsoleUiKey.Key, state);
    }
}
