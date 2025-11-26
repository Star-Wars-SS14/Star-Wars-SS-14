using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Physics;
using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using System.Linq;
using System.Numerics;

namespace Content.Shared.Trigger.Systems;

public sealed class ScramOnTriggerSystem : XOnTriggerSystem<ScramOnTriggerComponent>
{
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly INetManager _net = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private HashSet<Entity<MapGridComponent>> _targetGrids = new();

    public override void Initialize()
    {
        base.Initialize();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
    }

    protected override void OnTrigger(Entity<ScramOnTriggerComponent> ent, EntityUid target, ref TriggerEvent args)
    {
        // We need stop the user from being pulled so they don't just get "attached" with whoever is pulling them.
        // This can for example happen when the user is cuffed and being pulled.
        if (TryComp<PullableComponent>(target, out var pull) && _pulling.IsPulled(target, pull))
            _pulling.TryStopPull(ent, pull);

        // Check if the user is pulling anything, and drop it if so.
        if (TryComp<PullerComponent>(target, out var puller) && TryComp<PullableComponent>(puller.Pulling, out var pullable))
            _pulling.TryStopPull(puller.Pulling.Value, pullable);

        _audio.PlayPredicted(ent.Comp.TeleportSound, ent, args.User);

        // Can't predict picking random grids and the target location might be out of PVS range.
        if (_net.IsClient)
            return;

        var xform = Transform(target);
        var targetCoords = SelectRandomTileInRange(target, xform, ent.Comp.TeleportRadius); //SW

        if (targetCoords != null)
        {
            _transform.SetCoordinates(target, targetCoords.Value);
            args.Handled = true;
        }
    }
    //SW start
    private EntityCoordinates? SelectRandomTileInRange(EntityUid uid, TransformComponent userXform, float radius)
    {
        if (userXform.GridUid == null)
        {
            return null;
        }
        _targetGrids.Clear();

        float maxRadius = radius;
        float minRadius = radius - 1.0f;

        EntityUid gridUid = userXform.GridUid.Value;
        if (!TryComp<MapGridComponent>(userXform.GridUid, out var grid))
            return null;
        Vector2 tmp = new Vector2(maxRadius, maxRadius);
        Vector2 tmp2 = new Vector2(minRadius, minRadius);
        var shapeMin = new Box2(userXform.LocalPosition - tmp, userXform.LocalPosition + tmp);
        IEnumerable<TileRef> tilesInMaxRange = _map.GetLocalTilesIntersecting(uid, grid, shapeMin, false);

        var shapeMax = new Box2(userXform.LocalPosition - tmp2, userXform.LocalPosition + tmp2);
        IEnumerable<TileRef> tilesInMinRange = _map.GetLocalTilesIntersecting(uid, grid, shapeMax, false);

        IEnumerable<TileRef> tilesInRange = tilesInMaxRange.Except(tilesInMinRange);

        if (tilesInRange.Count() == 0)
        {
            return null;
        }

        var tileList = new ValueList<Vector2i>();
        EntityCoordinates? targetCoords = null;

        foreach (TileRef tile in tilesInRange)
        {
            if (!tile.Tile.IsEmpty)
            {
                tileList.Add(tile.GridIndices);
            }
        }
        bool valid = false;
        while (tileList.Count != 0)
        {
            var tile = tileList.RemoveSwap(_random.Next(tileList.Count));
            valid = true;
            foreach (var entity in _map.GetAnchoredEntities(gridUid, grid,
                         tile))
            {
                if (!_physicsQuery.TryGetComponent(entity, out var body))
                    continue;

                if (body.BodyType != BodyType.Static ||
                    !body.Hard ||
                    (body.CollisionLayer & (int)CollisionGroup.MobMask) == 0)
                    continue;

                valid = false;
                break;
            }

            if (valid)
            {
                targetCoords = new EntityCoordinates(gridUid,
                    _map.TileCenterToVector(gridUid, grid, tile));
                break;
            }
        }
        return targetCoords;
    }
    //SW end
}

