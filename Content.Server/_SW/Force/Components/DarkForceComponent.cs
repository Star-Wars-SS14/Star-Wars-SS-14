using Content.Shared.Alert;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class DarkForceComponent : ForceComponent
{
    public List<string> AbilitiesDarkPrototypes =
    [
        ""
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

    [DataField]
    public Dictionary<SithMasteryLevel, string> PopupsMessages = new()
    {
        {SithMasteryLevel.SithLord, "Вы чувствуете как темная сила поглащает вас.."},
        {SithMasteryLevel.SithDart, "Ваша кожа содрагает.. темная сила вышла на новый уровень.."}
    };

    public Dictionary<SithMasteryLevel, string> MasteryAbilities = new ()
    {
        {SithMasteryLevel.SithLord, "ActionSithForceLightning"}
    };

    [DataField]
    public Dictionary<SithMasteryLevel, ProtoId<AlertPrototype>> SithMasteryAlerts = new ()
    {
        { SithMasteryLevel.SithAkolit, "AlertAkolit" },
        { SithMasteryLevel.SithLord, "AlertLord" },
        { SithMasteryLevel.SithDart, "AlertDart" }
    };
}

public enum SithMasteryLevel : byte
{
    SithAkolit = 0,
    SithLord = 1,
    SithDart = 2,
}
