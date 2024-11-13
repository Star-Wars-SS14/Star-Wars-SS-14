using Content.Server._SW.Force.Components;
using Content.Server.Beam;
using Content.Server.Hands.Systems;
using Content.Server.Lightning;
using Content.Server.Popups;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Tag;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _action = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForceComponent, ComponentInit>(OnInitForce);
        SubscribeLocalEvent<DarkForceComponent, ComponentInit>(OnInitDark);
        SubscribeLocalEvent<LightForceComponent, ComponentInit>(OnInitLight);

        InitializeAbilities();
        InitializeMastery();
    }

    private void OnInitForce(EntityUid uid, ForceComponent comp, ComponentInit args)
    {
        SetupActions(uid, comp.AbilitiesPrototypes);
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
