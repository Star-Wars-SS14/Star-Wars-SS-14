using System.Runtime.InteropServices.JavaScript;
using Content.Shared.Alert;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class LightForceComponent : ForceComponent
{
    public List<string> AbilitiesLightPrototypes = [];

    // Mastery
    [DataField]
    public JediMasteryLevel Mastery = JediMasteryLevel.JediMasteryLevel1;

    [DataField]
    public Dictionary<JediMasteryLevel, ProtoId<AlertPrototype>> JediMasteryAlerts = new ()
    {
        { JediMasteryLevel.JediMasteryLevel1, "AlertJediLevel1" },
        { JediMasteryLevel.JediMasteryLevel2, "AlertJediLevel2" },
        { JediMasteryLevel.JediMasteryLevel3, "AlertJediLevel3" }
    };
}

public enum JediMasteryLevel : byte
{
    JediMasteryLevel1 = 0,
    JediMasteryLevel2 = 1,
    JediMasteryLevel3 = 2,
}
