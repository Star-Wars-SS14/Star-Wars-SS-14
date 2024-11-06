using Content.Server._SW.Force.Components;
using Content.Server.Hands.Systems;
using Content.Server.Lightning;
using Content.Server.Popups;
using Content.Shared._SW;
using Content.Shared.Actions;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly PullingSystem _pullingSystem = default!;
    [Dependency] private readonly ContainerSystem _containerSystem = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;

    private void InitializeAbilities()
    {
        SubscribeLocalEvent<ForceComponent, DashAbilityEvent>(DashAbility);
        SubscribeLocalEvent<DarkForceComponent, ForceLightningAbilityEvent>(LightingAbility);
        // kyber
        SubscribeLocalEvent<KyberCrystalComponent, MapInitEvent>(OnKyberMapInit);
        SubscribeLocalEvent<KyberCrystalComponent, GetItemActionsEvent>(OnGetKyber);
        SubscribeLocalEvent<SubdueAbilityEvent>(OnSubdueAction);
    }

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

        _lightning.ShootRandomLightnings(user, lightningAbility.LightningRange, lightningAbility.BoltCount, arcDepth: lightningAbility.ArcDepth);

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
            return;
        }

        kyberComp.Owner = user;
        _action.RemoveAction(kyberComp.KyberActionEntity);

        _popupSystem.PopupEntity("Камень установил связь..", user, PopupType.Medium);
    }
    #endregion
}
