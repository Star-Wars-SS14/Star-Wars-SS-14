using Content.Server.Anomaly.Components;
using Content.Server.GameTicking.Rules;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.StationEvents.Components;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Components;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.Random;
using Content.Server.Atmos.EntitySystems;

namespace Content.Server._SW.SWSpawnRandomRule;

public sealed partial class SWSpawnRandomGameruleSystem : GameRuleSystem<SWSpawnRandomGameruleComponent>
{
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    protected override void Started(EntityUid uid, SWSpawnRandomGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var chosenStation))
            return;

        if (!TryComp<StationDataComponent>(chosenStation, out var stationData))
            return;

        var grid = _stationSystem.GetLargestGrid(stationData);

        if (grid is null)
            return;

        
        SpawnOnRandomLocatin(grid.Value, component.Spawner, component.Count);
        
    }

    private void SpawnOnRandomLocatin(EntityUid grid, string toSpawn, int count = 1)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var xform = Transform(grid);

        var targetCoords = xform.Coordinates;
        var gridBounds = gridComp.LocalAABB;

        for (var i = 0; i < 25; i++)
        {
            var randomX = _random.Next((int)gridBounds.Left, (int)gridBounds.Right);
            var randomY = _random.Next((int)gridBounds.Bottom, (int)gridBounds.Top);

            var tile = new Vector2i(randomX, randomY);

            // no air-blocked areas.
            if (_atmosphere.IsTileSpace(grid, xform.MapUid, tile) ||
                _atmosphere.IsTileAirBlocked(grid, tile, mapGridComp: gridComp))
            {
                continue;
            }

            // don't spawn inside of solid objects
            var physQuery = GetEntityQuery<PhysicsComponent>();
            var valid = true;

            foreach (var ent in _mapSystem.GetAnchoredEntities(grid, gridComp, tile))
            {
                if (!physQuery.TryGetComponent(ent, out var body))
                    continue;
                if (body.BodyType != BodyType.Static ||
                    !body.Hard ||
                    (body.CollisionLayer & (int)CollisionGroup.Impassable) == 0)
                    continue;

                valid = false;
                break;
            }
            if (!valid)
                continue;

            var pos = _mapSystem.GridTileToLocal(grid, gridComp, tile);

            if (!valid)
                continue;

            targetCoords = pos;
            break;
        }
        for (var i = 0; i < count; i++)
        {
            Spawn(toSpawn, targetCoords);
        }
    }
}
