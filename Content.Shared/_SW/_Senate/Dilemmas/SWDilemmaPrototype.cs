using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Dilemmas;

[Serializable, NetSerializable]
public enum SWDilemmaScope : byte
{
    Sectoral,
    Republic,
}

[Serializable, NetSerializable]
public enum SWDilemmaWindow : byte
{
    Morning,
    Afternoon,
}

[Serializable, NetSerializable]
public enum SWDilemmaStatus : byte
{
    Pending,
    Chosen,
    Applied,
    Failed,
}

[Serializable, NetSerializable]
public enum SWDilemmaComparison : byte
{
    Below,
    Above,
}

[Prototype("swDilemma")]
public sealed partial class SWDilemmaPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public int Order;

    [DataField]
    public SWDilemmaScope Scope = SWDilemmaScope.Sectoral;

    /// <summary>
    /// Budget dilemmas require spending the owner's allocated credits to resolve; effects are positive,
    /// fail effects are empty (unresolved = missed opportunity).
    /// </summary>
    [DataField]
    public bool Budget;

    /// <summary>
    /// Empty = unconditional (part of the general random pool). Non-empty = conditional: every condition
    /// (checked against the sector's global for Sectoral, or the republic's actual global for Republic)
    /// must hold for the dilemma to become eligible — and even then it only gets a chance to fire.
    /// Conditional dilemmas are always non-budget and tend to hit harder.
    /// </summary>
    [DataField]
    public List<SWDilemmaCondition> Conditions = new();

    [DataField]
    public string FailText = string.Empty;

    [DataField]
    public List<SWDilemmaOption> Options = new();

    /// <summary>
    /// Outcome applied if the dilemma is not resolved in time. Republic dilemmas use policy (sub) effects.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> FailPolicyEffects = new();

    /// <summary>
    /// Outcome applied if the dilemma is not resolved in time. Sectoral dilemmas use sector (global) effects.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> FailSectorEffects = new();
}

[DataDefinition]
public sealed partial class SWDilemmaCondition
{
    [DataField(required: true)]
    public ProtoId<SWGlobalStatPrototype> Stat;

    [DataField]
    public SWDilemmaComparison Comparison = SWDilemmaComparison.Below;

    [DataField]
    public float Threshold;
}

[DataDefinition]
public sealed partial class SWDilemmaOption
{
    [DataField]
    public string Label = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public int Cost;

    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();

    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> SectorEffects = new();
}
