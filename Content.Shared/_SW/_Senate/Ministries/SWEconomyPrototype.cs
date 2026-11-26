using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable]
public enum SWEconomyPhase : byte
{
    Recession,
    Recovery,
    Growth,
    Overheat,
}

[Prototype("swMegaproject")]
public sealed partial class SWMegaprojectPrototype : IPrototype
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
    public int Phases = 3;

    [DataField]
    public int CostPerPhase;

    /// <summary>Game-hours each phase takes to build.</summary>
    [DataField]
    public int PhaseHours = 5;

    [DataField]
    public SWOpTarget Target = SWOpTarget.Sector;

    /// <summary>One-off shifts applied on completion.</summary>
    [DataField]
    public float GrowthDelta;

    [DataField]
    public float OverheatDelta;

    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();

    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> SectorEffects = new();
}

[Prototype("swEconomyProgram")]
public sealed partial class SWEconomyProgramPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();

    [DataField]
    public float GrowthDelta;

    [DataField]
    public float OverheatDelta;

    [DataField]
    public int Order;
}
