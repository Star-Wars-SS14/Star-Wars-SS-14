using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Events;
using System.Numerics;

namespace Content.Shared._SW.Vehicle;

/// <summary>
/// Landspeeder-style collision: knocks standing, alive, non-vehicle mobs down
/// (same guards and knockdown as SWVehicleMobCollision) and additionally flings
/// them away from the vehicle along the collision axis.
/// </summary>
public sealed class SWVehicleKnockbackSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWVehicleKnockbackComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(Entity<SWVehicleKnockbackComponent> ent, ref StartCollideEvent args)
    {
        // Same guards as the base vehicle collision: speed threshold, no vehicles,
        // only standing mobs (KnockedDownComponent = already lying), only alive mobs.
        if (args.OurBody.LinearVelocity.Length() < ent.Comp.MinimumSpeed ||
            HasComp<SWInVehicleComponent>(args.OtherEntity) ||
            HasComp<KnockedDownComponent>(args.OtherEntity) ||
            !_mobState.IsAlive(args.OtherEntity))
        {
            return;
        }

        // Knockdown identical to the base vehicle collision behavior.
        if (ent.Comp.KnockdownTime is { } knockdownTime)
            _stun.TryCrawling(args.OtherEntity, knockdownTime, drop: false);
        else
            _stun.TryUpdateParalyzeDuration(args.OtherEntity, ent.Comp.ParalyzeTime);

        // Fling the victim away from the vehicle along the collision axis.
        var vehiclePosition = Transform(ent.Owner).MapPosition.Position;
        var mobPosition = Transform(args.OtherEntity).MapPosition.Position;
        var direction = mobPosition - vehiclePosition;

        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        _throwing.TryThrow(args.OtherEntity, direction.Normalized(), ent.Comp.ThrowSpeed);
    }
}