using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Private political state on the existing rule. Contributions and state correspondence are never networked.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarPoliticsSystem))]
public sealed partial class SWGlobalWarPoliticsComponent : Component
{
    public Guid Generation;
    public long Revision;
    public long RelationsRevision;
    public SWGlobalWarRelations Relations;
    public Dictionary<SWGlobalWarFaction, Guid> TruceConsents = new();
    public HashSet<(SWGlobalWarFaction, ProtoId<SWGlobalWarCommodityPrototype>)> Sanctions = new();
    public Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarNeutralState> Neutrals = new();
    public Dictionary<(NetUserId, Guid), SWGlobalWarPoliticalReceipt> Requests = new();
    public List<SWGlobalWarStateMessage> StateMessages = new();
    public Dictionary<NetUserId, TimeSpan> LastMessage = new();
    public Dictionary<SWGlobalWarFaction, SWGlobalWarSurrenderConfirmation> SurrenderConfirmations = new();
    public Dictionary<SWGlobalWarFaction, TimeSpan> Surrenders = new();
    public Dictionary<SWGlobalWarFaction, TimeSpan> AbsentSince = new();
    public bool DoctrineChecked;
    public bool LimitAnnounced;
    /// <summary>Orbits and surfaces when the campaign was decided at its time limit.</summary>
    public (int Republic, int Empire)? LimitScore;
    public GameTick? SurrenderTick;
    public TimeSpan NextUpdate;
    public SWGlobalWarOutcome Outcome;
    public Dictionary<EntityUid, SWGlobalWarWithdrawalOrder> Withdrawals = new();
    public Dictionary<NetUserId, SWGlobalWarWithdrawalOrder> WithdrawalHistory = new();
    public Dictionary<EntityUid, SWGlobalWarShipWithdrawalOrder> ShipWithdrawals = new();
}

public sealed class SWGlobalWarNeutralState
{
    public EntityUid Planet;
    public SWGlobalWarNeutralPhase Phase;
    public long Revision;
    public long RepublicContribution;
    public long EmpireContribution;
    public SWGlobalWarFaction? DrawnWinner;
    public SWGlobalWarFaction Claimant;
    public Guid DrawOperation;
    public bool PendingOwnership;
    public TimeSpan Remaining;
    public TimeSpan UpdatedAt;
    public SWGlobalWarFaction? BorderConflict;
    public HashSet<Guid> ActiveCaptures = new();
    public HashSet<Guid> RetiredCaptures = new();
}

public sealed record SWGlobalWarPoliticalCommand(Guid Generation, Guid Request, long AssignmentRevision,
    SWGlobalWarPoliticalAction Action, long RelationsRevision = 0,
    ProtoId<SWGlobalWarPlanetPrototype>? Sector = null, ProtoId<SWGlobalWarCommodityPrototype>? Commodity = null,
    bool Enabled = false, long Amount = 0, string Text = "", Guid Confirmation = default);

public sealed record SWGlobalWarPoliticalReceipt(SWGlobalWarPoliticalCommand Command, bool Success,
    string Reason, Guid Confirmation, TimeSpan RetryAfter = default);

public sealed record SWGlobalWarStateMessage(Guid Id, NetUserId Author, string AuthorName,
    SWGlobalWarFaction Sender, TimeSpan SentAt, string Text);

public sealed record SWGlobalWarSurrenderConfirmation(Guid Id, Guid Assignment, TimeSpan Expires);

[ByRefEvent]
public readonly record struct SWGlobalWarPoliticsChangedEvent(Guid Generation, long Revision);

[ByRefEvent]
public readonly record struct SWGlobalWarCampaignOutcomeEvent(Guid Generation, SWGlobalWarOutcome Outcome);
