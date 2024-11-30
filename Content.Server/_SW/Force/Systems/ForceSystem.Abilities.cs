using Content.Server._SW.Force.Components;
using Content.Server.Hands.Systems;
using Content.Server.Lightning;
using Content.Server.Popups;
using Content.Shared._SW;
using Content.Shared.Actions;
using Content.Shared.Coordinates;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Toggleable;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using static Robust.Shared.Maths.Color;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem
{
    private void InitializeAbilities()
    {
        SubscribeLocalEvent<ForceComponent, DashAbilityEvent>(DashAbility);
        SubscribeLocalEvent<DarkForceComponent, ForceLightningAbilityEvent>(LightingAbility);
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

    #region DataLightForce
    private const string DashAbilityId = "ActionJediDash";
    #endregion

    #region DataDarkForce
    private const string ForceLightningAbilityId = "ActionSithForceLightning";
    #endregion

    #region Dash
    private void DashAbility(Entity<ForceComponent> ent, ref DashAbilityEvent args)
    {
        var (_, component) = ent;
        var user = args.Performer;

        if (!TryComp<TransformComponent>(user, out var xform))
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

        var offset = xform.LocalRotation.ToWorldVec().Normalized();
        var direction = xform.LocalRotation.GetDir().ToVec();
        var newOffset = offset + direction * component.DashDistance;

        var coords = xform.Coordinates.Offset(newOffset).SnapToGrid(EntityManager);

        Dash(user, component, coords, oldCoords);

        args.Handled = true;
    }

    private void Dash(EntityUid uid,
        ForceComponent component,
        EntityCoordinates coords,
        EntityCoordinates oldCoords)
    {
        PlaySoundAndApplyEffects(component, coords, oldCoords);
        _transform.SetCoordinates(uid, coords);
    }

    private void PlaySoundAndApplyEffects(ForceComponent component, EntityCoordinates coords, EntityCoordinates oldCoords)
    {
        _audio.PlayPvs(component.DashSound, coords);

        _entManager.SpawnEntity(component.DashEffectStart, coords);
        _entManager.SpawnEntity(component.DashEffectEnd, oldCoords);
    }
    #endregion

    #region Lighting
    private void LightingAbility(Entity<DarkForceComponent> ent, ref ForceLightningAbilityEvent args)
    {
        var user = args.Performer;
        var lightningAbility = ent.Comp;

        DamageSpecifier damage = new(_prototypeManager.Index<DamageTypePrototype>("Heat"), lightningAbility.DamagesLighting[lightningAbility.Mastery]);

        for (var i = 0; i < lightningAbility.BoltCount; i++)
        {
            _beam.TryCreateBeam(user, args.Target, lightningAbility.Mastery == SithMasteryLevel.SithMasteryLevel1 ? "BlueSWLightning" : "RedSWLightning");
            _damageable.TryChangeDamage(args.Target, damage);
        }

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

        _handsSystem.TryForcePickupAnyHand(args.User, saber);

        saberComponent.CallSaberActionEntity = _action.AddAction(args.User, component.CallSaberAction);

        Del(uid);
        Del(target);
        args.Handled = true;
    }

    private void OnMobStateChange(MobStateChangedEvent args)
    {
        var target = args.Target;

        if(!TryComp<ForceComponent>(target, out var targetForceComponent))
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
        }

        if(args.Origin == null)
            return;

        var user = args.Origin;

        if(!TryComp<ForceComponent>(user, out var userForceComponent))
            return;

        if(args is not ({ OldMobState: MobState.Critical, NewMobState: MobState.Dead } or {OldMobState: MobState.Alive, NewMobState: MobState.Critical}))
            return;

        userForceComponent.Experience += userForceComponent.AddExperienceCriticalOrDead;
        UpdateMastery(user, userForceComponent);
    }
    #endregion
}
