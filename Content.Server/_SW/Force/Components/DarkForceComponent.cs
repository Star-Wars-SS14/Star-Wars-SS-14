using Content.Shared.FixedPoint;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class DarkForceComponent : ForceComponent
{
    public List<string> AbilitiesDarkPrototypes =
    [
        "ActionSithForceLightning"
    ];

    // Lighting
    [DataField("BoltCount")]
    public int BoltCount = 3;

    [DataField("DamageLighting")]
    public Dictionary<SithMasteryLevel, FixedPoint2> DamagesLighting = new ()
    {
        {SithMasteryLevel.SithAkolit, 3},
        {SithMasteryLevel.SithLord, 4},
        {SithMasteryLevel.SithDart, 5}
    };

    // Mastery
    [DataField]
    public SithMasteryLevel Mastery = SithMasteryLevel.SithAkolit;
}

public enum SithMasteryLevel : byte
{
    SithAkolit = 0,
    SithLord = 1,
    SithDart = 2,
}
