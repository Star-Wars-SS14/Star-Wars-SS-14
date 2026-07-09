using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Apartments;

[Prototype("swApartmentClass")]
public sealed partial class SWApartmentClassPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public Color Color = Color.White;

    [DataField]
    public int PricePerDay;

    [DataField]
    public int Order;
}
