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
    /// Reserved for condition-triggered dilemmas. Empty = part of the general random pool.
    /// </summary>
    [DataField]
    public string? Trigger;

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
public sealed partial class SWDilemmaOption
{
    [DataField]
    public string Label = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();

    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> SectorEffects = new();
}
