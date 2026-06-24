using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Crises;

[Serializable, NetSerializable]
public enum SWCrisisComparison : byte
{
    Below,
    Above,
}

[Serializable, NetSerializable]
public enum SWCrisisType : byte
{
    Sectoral,
    Inter,
    Republic,
}

[Prototype("swCrisis")]
public sealed partial class SWCrisisPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public int Order;

    /// <summary>
    /// All conditions (on the origin sector's globals) must hold to spawn the crisis and to keep it
    /// worsening. When they no longer hold the crisis stops growing and severity decays.
    /// </summary>
    [DataField]
    public List<SWCrisisCondition> Conditions = new();

    [DataField]
    public float StartSeverity = 30f;

    /// <summary>
    /// Severity gained per tick while the conditions still hold (the unaddressed root cause worsens).
    /// </summary>
    [DataField]
    public float SeverityGain = 4f;

    /// <summary>
    /// True = the crisis arises at the Republic level (conditions checked against republic globals); it
    /// spawns already Republican and covers every active sector.
    /// </summary>
    [DataField]
    public bool Republic;

    /// <summary>
    /// Globals the crisis drags down (weight per stat). Actual pull scales with current severity.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> Pressure = new();

    [DataField]
    public List<SWCrisisSolution> Solutions = new();

    /// <summary>
    /// Emergency laws an affected senator or the Chancellor can table; passing one cuts severity.
    /// </summary>
    [DataField]
    public List<SWCrisisLaw> EmergencyLaws = new();
}

[DataDefinition]
public sealed partial class SWCrisisCondition
{
    [DataField(required: true)]
    public ProtoId<SWGlobalStatPrototype> Stat;

    [DataField]
    public SWCrisisComparison Comparison = SWCrisisComparison.Below;

    [DataField]
    public float Threshold;
}

[DataDefinition]
public sealed partial class SWCrisisLaw
{
    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    /// <summary>
    /// Severity removed if this emergency law is passed by the Senate.
    /// </summary>
    [DataField]
    public float Relief = 25f;

    /// <summary>
    /// Republic policy (sub-stat) effects applied when the law passes.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();
}

[DataDefinition]
public sealed partial class SWCrisisSolution
{
    [DataField]
    public string Label = string.Empty;

    [DataField]
    public string Description = string.Empty;

    /// <summary>
    /// Credits drawn from the acting sector's budget (0 = free).
    /// </summary>
    [DataField]
    public int Cost;

    /// <summary>
    /// How much severity this solution removes.
    /// </summary>
    [DataField]
    public float Relief = 20f;

    /// <summary>
    /// One-off permanent bias shift on the affected sector (helps recover the root-cause stats).
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> SectorEffects = new();
}
