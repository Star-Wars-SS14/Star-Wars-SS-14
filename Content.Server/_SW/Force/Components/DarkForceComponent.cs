using Content.Shared.Alert;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class DarkForceComponent : ForceComponent
{
    public List<string> AbilitiesDarkPrototypes =
    [];

    // Lighting
    [DataField("BoltCount")]
    public int BoltCount = 3;

    [DataField("DamageLighting")]
    public Dictionary<SithMasteryLevel, FixedPoint2> DamagesLighting = new ()
    {
        {SithMasteryLevel.SithMasteryLevel1, 3},
        {SithMasteryLevel.SithMasteryLevel2, 4},
        {SithMasteryLevel.SithMasteryLevel3, 5}
    };

    // Mastery
    [DataField]
    public SithMasteryLevel Mastery = SithMasteryLevel.SithMasteryLevel1;

    public Dictionary<SithMasteryLevel, string> MasteryAbilities = new ()
    {
        {SithMasteryLevel.SithMasteryLevel2, "ActionSithForceLightning"}
    };

    [DataField]
    public Dictionary<SithMasteryLevel, ProtoId<AlertPrototype>> SithMasteryAlerts = new ()
    {
        { SithMasteryLevel.SithMasteryLevel1, "AlertSithLevel1" },
        { SithMasteryLevel.SithMasteryLevel2, "AlertSithLevel2" },
        { SithMasteryLevel.SithMasteryLevel3, "AlertSithLevel3" }
    };
}

public enum SithMasteryLevel : byte
{
    SithMasteryLevel1 = 0,
    SithMasteryLevel2 = 1,
    SithMasteryLevel3 = 2,
}
