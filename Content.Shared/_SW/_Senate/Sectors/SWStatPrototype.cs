using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Sectors;

[Prototype("swStat")]
public sealed partial class SWStatPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField(required: true)]
    public ProtoId<SWGlobalStatPrototype> Global;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string ShortName = string.Empty;

    [DataField]
    public int Order;

    [DataField]
    public float Weight = 1f;

    [DataField]
    public float Default = 50f;

    [DataField]
    public float Min = 0f;

    [DataField]
    public float Max = 100f;
}
