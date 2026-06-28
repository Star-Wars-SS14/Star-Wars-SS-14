using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Ministries;

[Prototype("swFinanceProgram")]
public sealed partial class SWFinanceProgramPrototype : IPrototype
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
    public float InflationDelta;

    [DataField]
    public float DebtDelta;

    [DataField]
    public int Order;
}
