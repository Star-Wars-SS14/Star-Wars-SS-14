namespace Content.Shared._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidModuleLimitsComponent : Component
{
    [DataField]
    public Dictionary<string, int> Limits = new();
}
