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
using Content.Server.StationEvents.Events;
using Content.Shared.Station.Components;

namespace Content.Server._SW.SWSpawnRandomRule;

public sealed partial class SWSpawnRandomGameruleSystem : StationEventSystem<SWSpawnRandomGameruleComponent>
{
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookupSystem = default!;
    protected override void Started(EntityUid uid, SWSpawnRandomGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var chosenStation))
            return;

        if (!TryComp<StationDataComponent>(chosenStation, out var stationData))
            return;

        var grid = _stationSystem.GetLargestGrid(new Entity<StationDataComponent?>(
            chosenStation.Value, stationData));

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

            var tileCoordinates = new Vector2i(randomX, randomY);

            // no air-blocked areas.
            if (_atmosphere.IsTileSpace(grid, xform.MapUid, tileCoordinates) ||
                _atmosphere.IsTileAirBlocked(grid, tileCoordinates, mapGridComp: gridComp))
            {
                continue;
            }

            // don't spawn inside of solid objects
            var physQuery = GetEntityQuery<PhysicsComponent>();
            var valid = true;

            foreach (var ent in _mapSystem.GetAnchoredEntities(grid, gridComp, tileCoordinates))
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

            var pos = _mapSystem.GridTileToLocal(grid, gridComp, tileCoordinates);
            var entities = _lookupSystem.GetLocalEntitiesIntersecting(grid, tileCoordinates);

            foreach (var entity in entities)
            {
                if (TryComp<PhysicsComponent>(entity, out var comp))
                {
                    if (comp.Hard || (comp.CollisionLayer & (int)CollisionGroup.Impassable) != 0)
                    {
                        valid = false;
                        break;
                    }
                }
            }
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
