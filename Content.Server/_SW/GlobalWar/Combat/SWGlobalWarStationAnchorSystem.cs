using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server.Shuttles.Systems;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>
/// Campaign maps load their station grids as free dynamic bodies. A docked hull's thrust or a collision would push them,
/// and hubs and reception points refuse to bind to a moving grid, so every location's stations stop before those bind.
/// </summary>
public sealed class SWGlobalWarStationAnchorSystem : EntitySystem
{
    [Dependency] private readonly ShuttleSystem _shuttles = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarLocationReadyEvent>(OnLocationReady,
            before: [typeof(SWGlobalWarMarketSystem), typeof(SWGlobalWarReceptionSystem)]);
    }

    private void OnLocationReady(ref SWGlobalWarLocationReadyEvent args)
    {
        foreach (var grid in Comp<SWGlobalWarPlanetComponent>(args.Planet).StationGrids)
            if (Transform(grid).MapUid == args.Map)
                _shuttles.Disable(grid);
    }
}
