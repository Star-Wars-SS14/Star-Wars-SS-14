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
    Judicial,
}

[Serializable, NetSerializable]
public enum SWAmbitionComparison : byte
{
    Above,
    Below,
    AtLeast,
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
    /// Threshold. One generic kind reused for every ministry/department/bureau - see
    /// SWSenateAmbitionSystem.MinisterMetricValue for the (ministryId, metric) lookup table.
    /// </summary>
    MinisterMetric,

    /// <summary>
    /// Judicial-role ambitions: a named metric read off SWSenateCourtSystem, compared against Threshold.
    /// </summary>
    CourtMetric,

    /// <summary>
    /// A named metric keyed to the office HOLDER's person (not the office), read off SWMercSystem.
    /// </summary>
    PersonMetric,

    /// <summary>
    /// Senator-role committee ambitions: a named metric keyed to the senator's person, read off
    /// SWSenateCommitteeSystem (chairing, bills advanced/returned by their committee).
    /// </summary>
    CommitteeMetric,

    /// <summary>
    /// Count the current holder's enacted amendments whose parent law is still in force.
    /// </summary>
    PassOwnAmendments,

    /// <summary>
    /// Count enacted amendments to laws originally authored by another person.
    /// </summary>
    AmendOthersLaw,

    /// <summary>
    /// The holder's active amendments must contribute positively to a sub-stat that is also
    /// positive in the resulting law. Opposite amendments by that holder offset one another.
    /// </summary>
    AmendmentStat,
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

    /// <summary>
    /// Required when Role == Judicial: which court seat this ambition fits (chief/judge/prosecutor/defender).
    /// </summary>
    [DataField]
    public string CourtRole = string.Empty;

    [DataField]
    public SWAmbitionKind Kind;

    /// <summary>
    /// Global stat id for stat/outdo kinds; sub-stat id for AuthorLawStat / AmendmentStat;
    /// metric name for MinisterMetric.
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
    /// PassOwnAmendments / AmendOthersLaw: how many active enacted amendments are required.
    /// </summary>
    [DataField]
    public int Count;
}
