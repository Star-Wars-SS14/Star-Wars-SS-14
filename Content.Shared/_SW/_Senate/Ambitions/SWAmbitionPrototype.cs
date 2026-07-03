using Content.Shared._SW._Senate.Ministries;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ambitions;

[Serializable, NetSerializable]
public enum SWAmbitionRole : byte
{
    Senator,
    Chancellor,
    Minister,
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
    Consolidation,

    /// <summary>
    /// Minister-role ambitions: a named metric read off that office's own system, compared against
    /// Threshold. One generic kind reused for every ministry/department/bureau — see
    /// SWSenateAmbitionSystem.MinisterMetricValue for the (ministryId, metric) lookup table.
    /// </summary>
    MinisterMetric,
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

    /// <summary>
    /// Required when Role == Minister: which ministry/department/bureau office this ambition can be
    /// assigned to (e.g. SWMinistryDefense, SWDepartmentArbitrage, SWBureauShips).
    /// </summary>
    [DataField]
    public ProtoId<SWMinistryPrototype>? MinistryId;

    [DataField]
    public SWAmbitionKind Kind;

    /// <summary>
    /// Global stat id for stat/outdo kinds; sub-stat id for AuthorLawStat; metric name for MinisterMetric.
    /// Unused for law-count kinds.
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
