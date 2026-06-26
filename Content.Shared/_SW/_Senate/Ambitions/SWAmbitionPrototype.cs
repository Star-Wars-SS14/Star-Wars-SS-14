using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ambitions;

[Serializable, NetSerializable]
public enum SWAmbitionRole : byte
{
    Senator,
    Chancellor,
}

[Serializable, NetSerializable]
public enum SWAmbitionComparison : byte
{
    Above,
    Below,
}

[Serializable, NetSerializable]
public enum SWAmbitionKind : byte
{
    SectorStat,
    RepublicStat,
    OutdoSector,
    AuthorLawStat,
    PassOwnLaws,
    PassBills,
    PartyStrong,
    PartySize,
}

[Prototype("swAmbition")]
public sealed partial class SWAmbitionPrototype : IPrototype
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
    public SWAmbitionRole Role = SWAmbitionRole.Senator;

    [DataField]
    public SWAmbitionKind Kind;

    /// <summary>
    /// Global stat id for stat/outdo kinds; sub-stat id for AuthorLawStat. Unused for law-count kinds.
    /// </summary>
    [DataField]
    public string Stat = string.Empty;

    [DataField]
    public SWAmbitionComparison Comparison = SWAmbitionComparison.Above;

    [DataField]
    public float Threshold;

    /// <summary>
    /// OutdoSector: how far the owner's stat must exceed the rival sector's stat.
    /// </summary>
    [DataField]
    public float Margin;

    /// <summary>
    /// PassOwnLaws / PassBills: how many passed laws are required.
    /// </summary>
    [DataField]
    public int Count;
}
