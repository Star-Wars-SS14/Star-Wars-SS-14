using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ministries;

// DEFCON ladder: Peace (5, lowest readiness) … War (1, max readiness). Higher enum value = more severe.
[Serializable, NetSerializable]
public enum SWDefenseStance : byte
{
    Peace,
    Vigilance,
    Alert,
    Mobilization,
    War,
}

[Serializable, NetSerializable]
public enum SWFleetState : byte
{
    Reserve,
    Border,
    Sector,
    Repairing,
}

[Serializable, NetSerializable]
public enum SWOpTarget : byte
{
    None,
    Border,
    Sector,
}

[Prototype("swDefenseOperation")]
public sealed partial class SWDefenseOperationPrototype : IPrototype
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
    public float BorderDelta;

    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();

    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> SectorEffects = new();

    /// <summary>Minimum readiness required to launch (e.g. Mobilization for a pre-emptive strike).</summary>
    [DataField]
    public SWDefenseStance RequireStance = SWDefenseStance.Peace;

    /// <summary>Requires at least one fleet group stationed at the border.</summary>
    [DataField]
    public bool RequireBorderFleet;
}

[Prototype("swDefenseProgram")]
public sealed partial class SWDefenseProgramPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    /// <summary>
    /// Applied at day end, scaled by the program's progress (republic policy sub-stats).
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();

    /// <summary>
    /// Per-day shift to imperial border pressure, scaled by progress (negative = eases the border).
    /// </summary>
    [DataField]
    public float BorderDelta;

    [DataField]
    public int Order;
}
