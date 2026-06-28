using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable]
public enum SWMinistryKind : byte
{
    Ministry,
    Department,
    Bureau,
}

[Prototype("swMinistry")]
public sealed partial class SWMinistryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    /// <summary>
    /// Genitive form used to build the holder's title (e.g. "обороны" -> "Министр обороны").
    /// </summary>
    [DataField]
    public string TitleName = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public SWMinistryKind Kind = SWMinistryKind.Ministry;

    [DataField]
    public Color Color = Color.White;

    [DataField]
    public ProtoId<SWGlobalStatPrototype>? Domain;

    /// <summary>Additional profile globals shown alongside the primary domain (e.g. Industry for Economy).</summary>
    [DataField]
    public List<ProtoId<SWGlobalStatPrototype>> ExtraDomains = new();

    [DataField]
    public ProtoId<SWMinistryPrototype>? Parent;

    [DataField]
    public bool Central;

    [DataField]
    public int Order;
}
