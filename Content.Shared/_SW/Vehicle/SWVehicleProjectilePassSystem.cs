using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Shared._SW.Vehicle;

public sealed class SWVehicleProjectilePassSystem : EntitySystem
{
    private readonly Dictionary<EntityUid, EntityUid> _passthroughVehiclesByGun = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<GunComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<GunComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<SWProjectilePassVehicleComponent, PreventCollideEvent>(OnProjectilePreventCollide);
    }

    private void OnAttemptShoot(Entity<GunComponent> ent, ref AttemptShootEvent args)
    {
        if (TryGetPassthroughVehicle(args.User, out var vehicle) ||
            TryGetPassthroughVehicle(ent.Owner, out vehicle))
        {
            _passthroughVehiclesByGun[ent.Owner] = vehicle;
            return;
        }

        _passthroughVehiclesByGun.Remove(ent.Owner);
    }

    private void OnAmmoShot(Entity<GunComponent> ent, ref AmmoShotEvent args)
    {
        if (!_passthroughVehiclesByGun.Remove(ent.Owner, out var vehicle))
            return;

        TagProjectiles(args.FiredProjectiles, vehicle);
    }

    private void TagProjectiles(List<EntityUid> projectiles, EntityUid vehicle)
    {
        foreach (var projectile in projectiles)
        {
            var projectilePass = EnsureComp<SWProjectilePassVehicleComponent>(projectile);
            projectilePass.Vehicle = vehicle;
            Dirty(projectile, projectilePass);
        }
    }

    private void OnProjectilePreventCollide(Entity<SWProjectilePassVehicleComponent> ent, ref PreventCollideEvent args)
    {
        if (TerminatingOrDeleted(ent.Comp.Vehicle))
            return;

        if (args.OtherEntity == ent.Comp.Vehicle ||
            Transform(args.OtherEntity).ParentUid == ent.Comp.Vehicle)
        {
            args.Cancelled = true;
        }
    }

    private bool TryGetPassthroughVehicle(EntityUid source, out EntityUid vehicle)
    {
        vehicle = default;

        if (!HasComp<SWVehicleProjectilePassComponent>(source))
            return false;

        if (HasComp<SWVehicleComponent>(source))
        {
            vehicle = source;
            return true;
        }

        if (TryComp<SWInVehicleComponent>(source, out var inVehicle) &&
            HasComp<SWVehicleComponent>(inVehicle.Vehicle))
        {
            vehicle = inVehicle.Vehicle;
            return true;
        }

        var parent = Transform(source).ParentUid;
        while (parent.IsValid())
        {
            if (HasComp<SWVehicleComponent>(parent))
            {
                vehicle = parent;
                return true;
            }

            parent = Transform(parent).ParentUid;
        }

        return false;
    }
}
