using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._SW.Barricade;

public sealed class SWFoldingBarricadeSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWFoldingBarricadeComponent, BeforeDoorClosedEvent>(OnBeforeClose);
    }

    private void OnBeforeClose(EntityUid ent, SWFoldingBarricadeComponent component, BeforeDoorClosedEvent args)
    {
        if (args.Cancelled || !args.PerformCollisionCheck)
            return;

        var xform = Transform(ent);
        if (!HasComp<DoorComponent>(ent) || !TryComp<PhysicsComponent>(ent, out var body) ||
            !TryComp<FixturesComponent>(ent, out var fixtures) || fixtures.FixtureCount != 1 ||
            !fixtures.Fixtures.TryGetValue("fix1", out var fixture) || !fixture.Hard ||
            fixture.Shape is not PolygonShape || xform.MapID == MapId.Nullspace ||
            !HasComp<MapGridComponent>(xform.GridUid) || !TryComp<BroadphaseComponent>(xform.GridUid, out var broadphase))
        {
            args.Cancel();
            return;
        }

        // The query checks hard fixtures against the door's shapes even while its collision is disabled.
        // Keep the same entity-level exclusions as the normal door check.
        var overlapping = new HashSet<EntityUid>();
        var grid = xform.GridUid!.Value;
        var localTransform = _physics.GetRelativePhysicsTransform((ent, xform), grid);
        _lookup.GetLocalEntitiesIntersecting(grid, fixture.Shape, localTransform, overlapping,
            LookupFlags.All & ~LookupFlags.Sensors, broadphase);
        foreach (var other in overlapping)
        {
            if (other == ent || !TryComp<PhysicsComponent>(other, out var physics) || !physics.CanCollide)
                continue;

            if (physics.CollisionLayer == (int) CollisionGroup.GlassLayer ||
                physics.CollisionLayer == (int) CollisionGroup.GlassAirlockLayer ||
                physics.CollisionLayer == (int) CollisionGroup.TableLayer ||
                physics.CollisionLayer == (int) CollisionGroup.ConveyorMask ||
                (physics.CollisionMask & (int) CollisionGroup.LowImpassable) == 0 ||
                ((body.CollisionMask & physics.CollisionLayer) == 0 &&
                 (physics.CollisionMask & body.CollisionLayer) == 0))
                continue;

            args.Cancel();
            return;
        }

        args.PerformCollisionCheck = false;
    }
}
