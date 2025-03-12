using System.Numerics;
using Content.Server._SW.Force.Components;
using Content.Shared._SW;
using Content.Shared.Actions;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Toggleable;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using static Robust.Shared.Maths.Color;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem
{
    private void InitializeAbilities()
    {
        SubscribeLocalEvent<ForceComponent, DashAbilityEvent>(DashAbility);
        SubscribeLocalEvent<DarkForceComponent, ForceLightningAbilityEvent>(LightingAbility);
        SubscribeLocalEvent<LightForceComponent, JediForceSaberAttackEvent>(OnForceAttack);
        SubscribeLocalEvent<DarkForceComponent, SithGaspAbilityEvent>(OnGasp);
        // kyber
        SubscribeLocalEvent<KyberCrystalComponent, MapInitEvent>(OnKyberMapInit);
        SubscribeLocalEvent<KyberCrystalComponent, GetItemActionsEvent>(OnGetKyber);
        SubscribeLocalEvent<SubdueAbilityEvent>(OnSubdueAction);
        SubscribeLocalEvent<KyberCrystalComponent, AfterInteractEvent>(KyberAfterInteract);
        // saber
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChange);
    }

    private readonly Dictionary<string, Color> _colors = new()
    {
        {"White", White},
        {"Black", Black},
        {"Blue",  Blue},
        {"White-blue", LightBlue},
        {"Green", Green},
        {"Indigo", Indigo},
        {"Orange", Orange},
        {"Pink", Pink},
        {"Purple", Purple},
        {"Red", Red},
        {"Red-orange", OrangeRed},
        {"Yellow", Yellow},
        {"Yellow-green", YellowGreen}
    };

    #region Dash
    private void DashAbility(Entity<ForceComponent> ent, ref DashAbilityEvent args)
    {
        var (_, component) = ent;
        var user = args.Performer;

        if(!TryComp<PhysicsComponent>(user, out var physicsComponent))
            return;

        if (!TryComp<TransformComponent>(user, out var xform))
            return;

        if(physicsComponent.BodyStatus != BodyStatus.OnGround)
            return;

        if (TryComp<PullableComponent>(user, out var pullable) && pullable.BeingPulled)
        {
            _pullingSystem.TryStopPull(user, pullable);
        }

        if (TryComp<PullerComponent>(user, out var pulling)
            && pulling.Pulling != null &&
            TryComp<PullableComponent>(pulling.Pulling.Value, out var subjectPulling))
        {
            _pullingSystem.TryStopPull(pulling.Pulling.Value, subjectPulling);
        }

        if (_containerSystem.IsEntityInContainer(user))
        {
            if(!_containerSystem.TryRemoveFromContainer(user))
                return;
        }

        var oldCoords = xform.Coordinates;

        if (physicsComponent.LinearVelocity == Vector2.Zero)
        {
            _popupSystem.PopupEntity("Невозможно использовать стоя/лежа!", user, user, PopupType.MediumCaution);
            return;
        }

        Dash(user, component, physicsComponent, oldCoords);

        args.Handled = true;
    }

    private void Dash(EntityUid uid,
        ForceComponent component,
        PhysicsComponent physicsComponent,
        EntityCoordinates oldCoords)
    {
        PlaySoundAndApplyEffects(component, oldCoords);
        _physicsSystem.SetBodyStatus(uid, physicsComponent, BodyStatus.InAir);

        _physicsSystem.SetLinearVelocity(uid, physicsComponent.LinearVelocity * component.DashSpeed, body: physicsComponent);
        _physicsSystem.ApplyLinearImpulse(uid, physicsComponent.LinearVelocity * component.DashDistance, body: physicsComponent);

        var inDashingComponent = EnsureComp<InDashingComponent>(uid);
        inDashingComponent.ActiveCooldown = inDashingComponent.Cooldown;
    }

    private void PlaySoundAndApplyEffects(ForceComponent component, EntityCoordinates oldCoords)
    {
        _audio.PlayPvs(component.DashSound, oldCoords);
        _entManager.SpawnEntity(component.DashEffectEnd, oldCoords);
    }
    #endregion

    #region Lighting
    private void LightingAbility(Entity<DarkForceComponent> ent, ref ForceLightningAbilityEvent args)
    {
        var user = args.Performer;
        var lightningAbility = ent.Comp;

        DamageSpecifier damage = new(_prototypeManager.Index<DamageTypePrototype>("Heat"), lightningAbility.DamageLighting);

        for (var i = 0; i < lightningAbility.BoltCount; i++)
        {
            _beam.TryCreateBeam(user, args.Target, (lightningAbility.DamageLighting == lightningAbility.BaseLightingDamage) ? "BlueSWLightning" : "RedSWLightning");
            _damageable.TryChangeDamage(args.Target, damage, true);
        }

        args.Handled = true;
    }
    #endregion

    #region ForceAttack
    private void OnForceAttack(EntityUid jedi, LightForceComponent component, ref JediForceSaberAttackEvent args)
    {
        var user = args.Performer;
        var target = args.Target;

        if(!TryComp<ForceComponent>(user, out var forceComponent))
            return;

        var query = EntityQueryEnumerator<SaberComponent>();

        while (query.MoveNext(out var uid, out var saberComponent))
        {
            if (saberComponent.Owner == null || saberComponent.Owner != user)
                continue;

            if(!forceComponent.OwnedSabers.Contains(uid))
                continue;

            if (_containerSystem.IsEntityInContainer(uid) && !_containerSystem.TryRemoveFromContainer(uid))
                continue;

            if (!_toggleSystem.IsActivated(uid))
            {
                _toggleSystem.TryActivate(uid);
            }

            _handsSystem.TryDrop(jedi, uid, jedi.ToCoordinates());

            if (!SetSaberVelocity(uid, target, jedi))
                continue;

            args.Handled = true;
            return;
        }

        _popupSystem.PopupEntity("У вас нет привязанного меча!", jedi, jedi, PopupType.MediumCaution);
        args.Handled = false;
    }

    private bool SetSaberVelocity(EntityUid saber, EntityUid target, EntityUid user)
    {
        if (!TryComp<PhysicsComponent>(saber, out var physics))
            return false;

        _physicsSystem.SetLinearVelocity(saber, Vector2.Zero, false, body: physics);
        _physicsSystem.SetAngularVelocity(saber, 0, false, body: physics);
        var forcePulledComponent = EnsureComp<ForcePulledComponent>(saber);

        forcePulledComponent.TargetUser = target;

        _popupSystem.PopupEntity("Расслабляет руку, меч направляется в неизвестную сторону.", user, PopupType.Medium);

        return true;
    }
    #endregion

    #region SithGasp
    private void OnGasp(EntityUid uid, DarkForceComponent component, SithGaspAbilityEvent args)
    {
        var target = args.Target;

        _blindableSystem.SetMinDamage(target, component.EyeDamage);
        var isSithGasp = EnsureComp<IsSithGaspingComponent>(target);
        isSithGasp.ActiveCooldown = isSithGasp.Cooldown;

        _stunSystem.TryStun(target, component.StunTime, true);

        _popupSystem.PopupEntity("Направляет руку на жертву, напряжение в воздухе усиливается..", uid, PopupType.MediumCaution);

        args.Handled = true;
    }
    #endregion

    #region Kyber
    private void OnKyberMapInit(EntityUid uid, KyberCrystalComponent component, MapInitEvent args)
    {
        _action.AddAction(uid, ref component.KyberActionEntity, component.KyberActionPrototype);
    }

    private void OnGetKyber(EntityUid uid, KyberCrystalComponent component, GetItemActionsEvent args)
    {
        if(!HasComp<ForceComponent>(args.User))
            return;

        if(component.Owner != null)
            return;

        args.AddAction(ref component.KyberActionEntity, component.KyberActionPrototype);
    }

    private void OnSubdueAction(SubdueAbilityEvent args)
    {
        var user = args.Performer;
        var kyber = _handsSystem.GetActiveItem(user);

        if(!TryComp<KyberCrystalComponent>(kyber, out var kyberComp))
            return;

        if (kyberComp.Owner != null)
        {
            _popupSystem.PopupEntity("Камень не может установить связь.. Его сила привязана к другому существу..", user, PopupType.Medium);
            args.Handled = true;
            return;
        }

        kyberComp.Owner = user;

        _action.RemoveAction(kyberComp.KyberActionEntity);

        _popupSystem.PopupEntity("Камень установил связь..", user, PopupType.Medium);
        args.Handled = true;
    }

    private void KyberAfterInteract(EntityUid uid, KyberCrystalComponent component, AfterInteractEvent args)
    {
        if (args.Target == null || !args.CanReach || args.Handled)
            return;

        if (component.Owner == null)
        {
            _popupSystem.PopupEntity("Камень не может установить связь с владельцем..", args.User, PopupType.Medium);
            return;
        }

        if(!TryComp<ForceComponent>(args.User, out var forceComponent))
            return;

        var target = args.Target.Value;

        if(!_tagSystem.HasTag(target, component.SaberFragmentTag))
            return;

        _popupSystem.PopupEntity("В мгновение, камень летит в фрагмент меча.. Меч исправен..", args.User, PopupType.Medium);

        var saber = Spawn(component.SaberPrototype, target.ToCoordinates());

        if(!TryComp<SaberComponent>(saber, out var saberComponent))
            return;

        if (!TryComp(saber, out AppearanceComponent? appearanceComponent))
            return;

        if (component.HexColor == null)
            return;

        _appearance.SetData(saber, ToggleableLightVisuals.Color, _colors[component.HexColor], appearanceComponent);

        saberComponent.Owner = component.Owner;
        forceComponent.OwnedSabers.Add(saber);

        _handsSystem.TryForcePickupAnyHand(args.User, saber);

        saberComponent.CallSaberActionEntity = _action.AddAction(args.User, component.CallSaberAction);

        Del(uid);
        Del(target);
        args.Handled = true;
    }

    private void OnMobStateChange(MobStateChangedEvent args)
    {
        var target = args.Target;

        if(!TryComp<ForceComponent>(target, out var forceComponent))
            return;

        var query = EntityQueryEnumerator<SaberComponent>();

        while (query.MoveNext(out _, out var saberComponent))
        {
            if (saberComponent.Owner == null || saberComponent.Owner != target)
                continue;

            if (args.NewMobState != MobState.Dead)
                continue;

            if(saberComponent.CallSaberActionEntity == null)
                continue;

            _action.RemoveAction(saberComponent.CallSaberActionEntity);
            saberComponent.Owner = null;
            forceComponent.OwnedSabers.Clear();
        }

        if(args.Origin == null)
            return;

        var user = args.Origin;

        if(!TryComp<ForceComponent>(user, out var userForceComponent))
            return;

        if(args is not ({ OldMobState: MobState.Critical, NewMobState: MobState.Dead } or {OldMobState: MobState.Alive, NewMobState: MobState.Critical}))
            return;

        userForceComponent.Experience += userForceComponent.AddExperienceCriticalOrDead;
    }
    #endregion
}
