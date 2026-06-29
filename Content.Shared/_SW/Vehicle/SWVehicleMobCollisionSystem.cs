using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Physics.Events;

namespace Content.Shared._SW.Vehicle;

public sealed class SWVehicleMobCollisionSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWVehicleMobCollisionComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(Entity<SWVehicleMobCollisionComponent> ent, ref StartCollideEvent args)
    {
        if (args.OurBody.LinearVelocity.Length() < ent.Comp.MinimumSpeed ||
            HasComp<SWInVehicleComponent>(args.OtherEntity) ||
            HasComp<KnockedDownComponent>(args.OtherEntity) ||
            !_mobState.IsAlive(args.OtherEntity))
        {
            return;
        }

        _stun.TryUpdateParalyzeDuration(args.OtherEntity, ent.Comp.ParalyzeTime);
    }
}
