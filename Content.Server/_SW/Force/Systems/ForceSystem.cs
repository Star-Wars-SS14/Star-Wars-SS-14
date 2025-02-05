using Content.Server._SW.Force.Components;
using Content.Server.Beam;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Tag;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _action = default!;
    [Dependency] private readonly AlertsSystem _alertsSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly BeamSystem _beam = default!;
    [Dependency] private readonly ContainerSystem _containerSystem = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly PhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly PullingSystem _pullingSystem = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;

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
            if (!TryComp<TransformComponent>(target, out var transformComponent))
                break;

            if (!TryComp<PhysicsComponent>(target, out var physicsComponent))
                break;

            comp.ActiveCooldown -= frameTime;

            if (comp.ActiveCooldown <= 0F)
            {
                _physicsSystem.SetBodyStatus(target, physicsComponent, BodyStatus.OnGround);
                RemComp<InDashingComponent>(target);
            }
        }
    }

    private void OnInitForce(EntityUid uid, ForceComponent comp, ComponentInit args)
    {
        SetupActions(uid, comp.AbilitiesPrototypes);

        if (TryComp<DarkForceComponent>(uid, out var darkForceComponent))
        {
            if (_alertsSystem.IsShowingAlert(uid, darkForceComponent.SithMasteryAlerts[darkForceComponent.Mastery]))
                return;

            _alertsSystem.ShowAlert(uid, darkForceComponent.SithMasteryAlerts[darkForceComponent.Mastery]);
        }

        if (TryComp<LightForceComponent>(uid, out var lightForceComponent))
        {
            if (_alertsSystem.IsShowingAlert(uid, lightForceComponent.JediMasteryAlerts[lightForceComponent.Mastery]))
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
        if (actionsPrototype.Count <= 0)
            return;

        foreach (var action in actionsPrototype)
        {
            _action.AddAction(uid, action);
        }
    }

    #endregion
}
