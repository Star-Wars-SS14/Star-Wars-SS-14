using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._SW.Vehicle;

public sealed class SWVehicleMobStandBlockSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StandingStateComponent, StandUpAttemptEvent>(OnStandUpAttempt);
    }

    private void OnStandUpAttempt(Entity<StandingStateComponent> ent, ref StandUpAttemptEvent args)
    {
        if (!IsBlockedByVehicle(ent))
            return;

        args.Cancelled = true;
        args.Autostand = true;
        args.Message = (Loc.GetString("knockdown-component-stand-no-room"), PopupType.SmallCaution);
    }

    private bool IsBlockedByVehicle(EntityUid uid)
    {
        var intersecting = _physics.GetEntitiesIntersectingBody(uid, StandingStateSystem.StandingCollisionLayer, false);

        foreach (var entity in intersecting)
        {
            if (HasComp<SWVehicleMobCollisionComponent>(entity))
                return true;
        }

        return false;
    }
}
