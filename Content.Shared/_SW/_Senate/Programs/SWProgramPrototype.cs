using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Programs;

[Prototype("swProgram")]
public sealed partial class SWProgramPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public string Category = string.Empty;

    [DataField]
    public int Order;

    [DataField]
    public int Cost;

    /// <summary>One-time project kit, consumed by the project rather than commissioned as generic assets.</summary>
    [DataField]
    public Dictionary<ProtoId<SWCommodityPrototype>, float> Materials = new();

    /// <summary>Active game-hours of implementation after the complete kit has arrived.</summary>
    [DataField]
    public float WorkHours = 2f;

    /// <summary>Existing local supply services that limit a completed project's operation.</summary>
    [DataField]
    public List<string> SuppliesGroups = new();

    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> SectorEffects = new();
}
