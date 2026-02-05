using Content.Server.Power.EntitySystems;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Whitelist;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Server._SW.Lathe.MagnetPickup;

public sealed class SWMagnetPickupSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly PowerChargeSystem _chargeSystem = default!;

    private static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(1);

    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        SubscribeLocalEvent<SWMagnetPickupComponent, MapInitEvent>(OnMagnetMapInit);
        SubscribeLocalEvent<SWMagnetPickupComponent, StartCollideEvent>(OnCollide);
    }

    private void OnCollide(Entity<SWMagnetPickupComponent> ent, ref StartCollideEvent args)
    {
        if (!_chargeSystem.IsPowered(ent, EntityManager))
            return;

        if (args.OurFixtureId != ent.Comp.InputFixtureId)
        {
            return;
        }

        if (!TryComp<StorageComponent>(ent, out var storage))
        {
            return;
        }

        if (!TryComp(ent, out TransformComponent? xform))
        {
            return;
        }

        // No space
        if (!_storage.HasSpace((ent, storage)))
            return; ;

        var parentUid = xform.ParentUid;
        var playedSound = false;
        var finalCoords = xform.Coordinates;
        var moverCoords = _transform.GetMoverCoordinates(ent, xform);
        var near = args.OtherEntity;

        if (HasComp<SWMagnetUnpickupableComponent>(near))
        {
            return;
        }

        if (_whitelistSystem.IsWhitelistFail(storage.Whitelist, near))
            return;

        if (!_physicsQuery.TryGetComponent(near, out var physics) || physics.BodyStatus != BodyStatus.OnGround)
            return;

        if (near == parentUid)
            return;

        var nearXform = Transform(near);
        var nearMap = _transform.GetMapCoordinates(near, xform: nearXform);
        var nearCoords = _transform.ToCoordinates(moverCoords.EntityId, nearMap);

        if (!_storage.Insert(ent, near, out var stacked, storageComp: storage, playSound: !playedSound))
            return;

        if (stacked != null)
            _storage.PlayPickupAnimation(stacked.Value, nearCoords, finalCoords, nearXform.LocalRotation);
        else
            _storage.PlayPickupAnimation(near, nearCoords, finalCoords, nearXform.LocalRotation);
    }


    private void OnMagnetMapInit(EntityUid uid, SWMagnetPickupComponent component, MapInitEvent args)
    {
        component.NextScan = _timing.CurTime;
    }
}
