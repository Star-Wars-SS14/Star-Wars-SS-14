using Robust.Shared.GameStates;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class KyberCrystalComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Owner;

    [DataField, AutoNetworkedField]
    public string KyberActionPrototype = "ActionSubdueKyber";

    [DataField, AutoNetworkedField]
    public EntityUid? KyberActionEntity;
}
