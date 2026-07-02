namespace Content.Server._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidTargetingTabletComponent : Component
{
    [DataField]
    public string Faction = string.Empty;

    public EntityUid? CurrentUiUser;
}
