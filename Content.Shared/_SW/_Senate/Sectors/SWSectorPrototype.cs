using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Sectors;

[Prototype("swSector")]
public sealed partial class SWSectorPrototype : IPrototype
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
    public float Weight = 1f;

    /// <summary>Shipyard world - damaged fleet groups can be repaired here.</summary>
    [DataField]
    public bool Shipyard;

    [DataField]
    public Dictionary<ProtoId<SWGlobalStatPrototype>, float> StartingStats = new();
}
