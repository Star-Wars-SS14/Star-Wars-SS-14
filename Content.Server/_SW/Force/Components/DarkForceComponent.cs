namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class DarkForceComponent : ForceComponent
{
    public List<string> AbilitiesDarkPrototypes =
    [
        "ActionSithForceLightning"
    ];

    // Lighting
    [DataField("LightningRange")]
    public float LightningRange = 3f;

    [DataField("BoltCount")]
    public int BoltCount = 2;

    [DataField("ArcDepth")]
    public int ArcDepth = 1;
}
