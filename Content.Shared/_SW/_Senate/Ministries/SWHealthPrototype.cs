using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Ministries;

[Prototype("swHealthProgram")]
public sealed partial class SWHealthProgramPrototype : IPrototype
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

    /// <summary>One-off push to vaccine research at day end (scaled by progress).</summary>
    [DataField]
    public float ResearchDelta;

    /// <summary>One-off shift to galaxy-wide infection at day end (negative = suppresses, scaled by progress).</summary>
    [DataField]
    public float SpreadDelta;

    [DataField]
    public int Order;
}
