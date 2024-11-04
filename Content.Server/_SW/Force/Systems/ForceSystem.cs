using Content.Server._SW.Force.Components;
using Content.Shared.Actions;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _action = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DarkForceComponent, ComponentInit>(OnInitDark);
        SubscribeLocalEvent<LightForceComponent, ComponentInit>(OnInitLight);

        InitializeAbilities();
    }

    #region darkForce
    private void OnInitDark(EntityUid uid, DarkForceComponent comp, ComponentInit args)
    {
        SetupActions(uid, comp.AbilitiesPrototypes);
    }
    #endregion

    #region lightForce
    private void OnInitLight(EntityUid uid, LightForceComponent comp, ComponentInit args)
    {
        SetupActions(uid, comp.AbilitiesPrototypes);
    }
    #endregion

    #region Helpers
    private void SetupActions(EntityUid uid, List<string> actionsPrototype)
    {
        foreach (var action in actionsPrototype)
        {
            _action.AddAction(uid, action);
        }
    }
    #endregion
}
