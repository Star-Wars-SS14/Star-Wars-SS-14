using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.SaberForms;

/// <summary>Lightsaber forms available to this Force user.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class SWSaberFormComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<SWSaberFormPrototype> ActiveForm = "SWFormShiiCho";

    [DataField]
    public List<ProtoId<SWSaberFormPrototype>> AllowedForms = new() { "SWFormShiiCho", "SWFormMakashi", "SWFormSoresu" };

    [DataField]
    public EntProtoId Action = "ActionSWSaberForms";

    public EntityUid? ActionEntity;

    public EntityUid? MenuWeapon;

    [AutoNetworkedField]
    public EntityUid? RiposteWeapon;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan RiposteUntil;
}

/// <summary>Whether this blade supports single-blade lightsaber forms.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWSaberFormWeaponComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = true;
}
