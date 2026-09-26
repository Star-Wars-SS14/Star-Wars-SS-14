namespace Content.Server._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidTargetingTabletComponent : Component
{
    [DataField]
    public string Faction = string.Empty;

    [DataField]
    public string ControlIdPrefix = "DU";

    [DataField]
    public string ControlId = string.Empty;

    [DataField]
    public bool Enabled = true;

    public EntityUid? CurrentUiUser;
}
