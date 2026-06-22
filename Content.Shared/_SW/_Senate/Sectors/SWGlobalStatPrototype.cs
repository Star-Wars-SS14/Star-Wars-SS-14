using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Sectors;

[Prototype("swGlobalStat")]
public sealed partial class SWGlobalStatPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string ShortName = string.Empty;

    [DataField]
    public int Order;

    [DataField]
    public Color Color = Color.White;

    [DataField]
    public float Min = 0f;

    [DataField]
    public float Max = 100f;
}
