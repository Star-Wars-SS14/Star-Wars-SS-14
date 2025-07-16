namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class KyberCrystalComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Owner;

    [ViewVariables(VVAccess.ReadWrite), DataField("hexcolor")]
    public string? HexColor;

    public string CallSaberAction = "CallSaberAction";

    public string KyberActionPrototype = "ActionSubdueKyber";

    public string SWSaberFragmentTag = "SWSaberFragmentTag";

    public string SaberPrototype = "Lightsaber";

    [DataField, AutoNetworkedField]
    public EntityUid? KyberActionEntity;
}
