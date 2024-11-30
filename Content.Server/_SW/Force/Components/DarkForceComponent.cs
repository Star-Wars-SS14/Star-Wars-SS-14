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
    [DataField]
    public int BoltCount = 3;

    [DataField]
    public FixedPoint2 DamageLighting = 4;

    [DataField]
    public FixedPoint2 BonusDamageFromUpgrade = 2.5;

    [DataField]
    public FixedPoint2 BaseLightingDamage = 4;

    // Mastery
    [DataField]
    public SithMasteryLevel Mastery = SithMasteryLevel.SithMasteryLevel1;

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
