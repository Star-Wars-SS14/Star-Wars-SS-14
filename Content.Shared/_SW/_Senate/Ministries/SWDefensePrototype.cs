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
