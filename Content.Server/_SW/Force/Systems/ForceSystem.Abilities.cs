using Content.Server._SW.Force.Components;
using Content.Server.Lightning;
using Content.Shared._SW;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;

    private void InitializeAbilities()
    {
        SubscribeLocalEvent<ForceComponent, DashAbilityEvent>(DashAbility);
        SubscribeLocalEvent<DarkForceComponent, ForceLightningAbilityEvent>(LightingAbility);
    }

    #region DataLightForce
    private const string DashAbilityId = "ActionJediDash";
    #endregion

    #region DataDarkForce
    private const string ForceLightningAbilityId = "ActionSithForceLightning";
    #endregion

    private void DashAbility(Entity<ForceComponent> ent, ref DashAbilityEvent args)
    {
        var (_, comp) = ent;
        var user = args.Performer;
        var localPhysics = EntityManager.GetComponent<PhysicsComponent>(user);

        if (localPhysics.BodyStatus != BodyStatus.OnGround)
            return;

        _audio.PlayPvs(comp.DashSound, ent);

        _physics.SetBodyStatus(localPhysics, BodyStatus.InAir);
        _physics.SetLinearVelocity(user, localPhysics.LinearVelocity * comp.DashSpeed, body: localPhysics);
        _physics.ApplyLinearImpulse(user, localPhysics.LinearVelocity * comp.DashImpulse, body: localPhysics);
    }

    private void LightingAbility(Entity<DarkForceComponent> ent, ref ForceLightningAbilityEvent args)
    {
        var user = args.Performer;
        var lightningAbility = ent.Comp;

        _lightning.ShootRandomLightnings(user, lightningAbility.LightningRange, lightningAbility.BoltCount, arcDepth: lightningAbility.ArcDepth);
    }
}
