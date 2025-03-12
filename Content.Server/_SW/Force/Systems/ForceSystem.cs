using Content.Server._SW.Force.Components;
using Content.Server.Beam;
using Content.Server.Hands.Systems;
using Content.Server.Item;
using Content.Server.Popups;
using Content.Server.Stunnable;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _action = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ItemToggleSystem _toggleSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly PullingSystem _pullingSystem = default!;
    [Dependency] private readonly ContainerSystem _containerSystem = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly BeamSystem _beam = default!;
    [Dependency] private readonly AlertsSystem _alertsSystem = default!;
    [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly PhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly BlindableSystem _blindableSystem = default!;
    [Dependency] private readonly BlurryVisionSystem _blurryVisionSystem = default!;
    [Dependency] private readonly EyeClosingSystem _eyelids = default!;
    [Dependency] private readonly StunSystem _stunSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForceComponent, ComponentInit>(OnInitForce);
        SubscribeLocalEvent<DarkForceComponent, ComponentInit>(OnInitDark);
        SubscribeLocalEvent<LightForceComponent, ComponentInit>(OnInitLight);

        InitializeAbilities();
        InitializeMastery();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<InDashingComponent>();
        while (query.MoveNext(out var target, out var comp))
        {
            if(!TryComp<TransformComponent>(target, out _))
                break;

            if(!TryComp<PhysicsComponent>(target, out var physicsComponent))
                break;

            comp.ActiveCooldown -= frameTime;

            if (comp.ActiveCooldown <= 0F)
            {
                _physicsSystem.SetBodyStatus(target, physicsComponent, BodyStatus.OnGround);
                RemComp<InDashingComponent>(target);
            }
        }

        var query2 = EntityQueryEnumerator<ForcePulledComponent>();
        while (query2.MoveNext(out var saber, out var comp))
        {
            var target = comp.TargetUser;

            if (target == null)
                continue;

            if(!TryComp<SaberComponent>(saber, out var saberComponent))
                continue;

            comp.ActiveCooldown -= frameTime;

            if (comp.TargetUser != null)
            {
                if(!TryComp<TransformComponent>(target, out var targetTransformComponent))
                    continue;

                if (_transformSystem.GetGridTilePositionOrDefault(saber) != _transformSystem.GetGridTilePositionOrDefault(((EntityUid) target, targetTransformComponent)) && comp.ActiveCooldown <= 0F)
                {
                    if (comp.Attempts >= comp.MaxAttemptsOnTarget)
                    {
                        RemCompDeferred<ForcePulledComponent>(saber);
                        continue;
                    }

                    comp.ActiveCooldown = comp.Cooldown;
                    comp.Attempts += 1;
                    _throwingSystem.TryThrow(saber, targetTransformComponent.Coordinates, comp.ThrowForce);
                }
                else if (_transformSystem.GetGridTilePositionOrDefault(saber) == _transformSystem.GetGridTilePositionOrDefault(((EntityUid) target, targetTransformComponent)))
                {
                    var burnDamageGroup = _prototypeManager.Index<DamageGroupPrototype>("Burn");
                    DamageSpecifier damage = new(burnDamageGroup, comp.DamageFromSaber);

                    _damageableSystem.TryChangeDamage(target, damage, true);
                    comp.TargetUser = null;
                    comp.ForceUser = saberComponent.Owner;
                }
            }
        }

        var query3 = EntityQueryEnumerator<IsSithGaspingComponent>();
        while (query3.MoveNext(out var target, out var comp))
        {
            if(!TryComp<BlindableComponent>(target, out var blindableComponent))
                return;

            comp.ActiveCooldown -= frameTime;

            if (comp.ActiveCooldown <= 0F)
            {
                _blindableSystem.SetMinDamage(target, comp.MinEyeDamage);

                _blindableSystem.AdjustEyeDamage(target, -blindableComponent.EyeDamage);

                _blurryVisionSystem.UpdateBlurMagnitude(target);
                _eyelids.UpdateEyesClosable(target);
                RemComp<IsSithGaspingComponent>(target);

                _popupSystem.PopupEntity("Падает на землю, еле дыша..", target, PopupType.MediumCaution);
                _stunSystem.TryParalyze(target, comp.StunTime, true);
            }
        }
    }

    private void OnInitForce(EntityUid uid, ForceComponent comp, ComponentInit args)
    {
        SetupActions(uid, comp.AbilitiesPrototypes);

        if (TryComp<DarkForceComponent>(uid, out var darkForceComponent))
        {
            if(_alertsSystem.IsShowingAlert(uid, darkForceComponent.SithMasteryAlerts[darkForceComponent.Mastery]))
                return;

            _alertsSystem.ShowAlert(uid, darkForceComponent.SithMasteryAlerts[darkForceComponent.Mastery]);
        }

        if (TryComp<LightForceComponent>(uid, out var lightForceComponent))
        {
            if(_alertsSystem.IsShowingAlert(uid, lightForceComponent.JediMasteryAlerts[lightForceComponent.Mastery]))
                return;

            _alertsSystem.ShowAlert(uid, lightForceComponent.JediMasteryAlerts[lightForceComponent.Mastery]);
        }
    }

    #region darkForce
    private void OnInitDark(EntityUid uid, DarkForceComponent comp, ComponentInit args)
    {
        SetupActions(uid, comp.AbilitiesDarkPrototypes);
    }
    #endregion

    #region lightForce
    private void OnInitLight(EntityUid uid, LightForceComponent comp, ComponentInit args)
    {
        SetupActions(uid, comp.AbilitiesLightPrototypes);
    }
    #endregion

    #region Helpers
    private void SetupActions(EntityUid uid, List<string> actionsPrototype)
    {
        if(actionsPrototype.Count <= 0)
            return;

        foreach (var action in actionsPrototype)
        {
            _action.AddAction(uid, action);
        }
    }
    #endregion
}
