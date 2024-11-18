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
    public JediMasteryLevel Mastery = JediMasteryLevel.Padawan;

    [DataField]
    public Dictionary<JediMasteryLevel, string> PopupsMessages = new()
    {
        {JediMasteryLevel.JediKnight, "Вы чувствуете прилив сил.."},
        {JediMasteryLevel.JediMaster, "Ваша кожа содрагает.. сила вышла на новый уровень.."}
    };

    public Dictionary<JediMasteryLevel, string> MasteryAbilities = [];

    [DataField]
    public Dictionary<JediMasteryLevel, ProtoId<AlertPrototype>> JediMasteryAlerts = new ()
    {
        { JediMasteryLevel.Padawan, "AlertPadawan" },
        { JediMasteryLevel.JediKnight, "AlertKnight" },
        { JediMasteryLevel.JediMaster, "AlertMaster" }
    };
}

public enum JediMasteryLevel : byte
{
    Padawan = 0,
    JediKnight = 1,
    JediMaster = 2,
}
