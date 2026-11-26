using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ministries;

// Security posture ladder: Civil (open society) … Emergency (police state). Higher value = harsher.
[Serializable, NetSerializable]
public enum SWSecurityPosture : byte
{
    Civil,
    Watch,
    Heightened,
    Emergency,
}

[Serializable, NetSerializable]
public enum SWCellState : byte
{
    Reserve,
    Sector,
    Rebuilding,
}

[Serializable, NetSerializable]
public enum SWPlotType : byte
{
    Sedition,
    Assassination,
    Separatist,
    Smuggling,
}

[Prototype("swSecurityOperation")]
public sealed partial class SWSecurityOperationPrototype : IPrototype
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
    public int Cost;

    [DataField]
    public int AdminCost = 1;

    /// <summary>Game-hours before the operation can be launched again.</summary>
    [DataField]
    public int Cooldown = 3;

    /// <summary>0 = instant; &gt;0 = timed operation that applies its effects after this many game-hours.</summary>
    [DataField]
    public int Duration;

    [DataField]
    public SWOpTarget Target = SWOpTarget.None;

    [DataField]
    public float ThreatDelta;

    [DataField]
    public float UnrestDelta;

    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();

    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> SectorEffects = new();

    /// <summary>Minimum posture required to launch (e.g. Emergency for a purge).</summary>
    [DataField]
    public SWSecurityPosture RequirePosture = SWSecurityPosture.Civil;
}

[Prototype("swSecurityProgram")]
public sealed partial class SWSecurityProgramPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    /// <summary>Applied at day end, scaled by the program's progress (republic policy sub-stats).</summary>
    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();

    [DataField]
    public float ThreatDelta;

    [DataField]
    public float UnrestDelta;

    [DataField]
    public int Order;
}
