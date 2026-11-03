using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Politics;

[Serializable, NetSerializable]
public enum SWGlobalWarGovernmentUiKey : byte { Key }

[Serializable, NetSerializable]
public enum SWGlobalWarGovernmentConsoleKind : byte { Diplomacy, Finance }

[Serializable, NetSerializable]
public enum SWGlobalWarViewAvailability : byte { Ready, Unavailable, Forbidden }

/// <summary>One recipient's projection. Restricted data is absent, never sent for client-side hiding.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarDiplomacyView(Guid Generation, long Revision, long RelationsRevision,
    TimeSpan Time, SWGlobalWarViewAvailability Availability, Guid Assignment, long AssignmentRevision,
    SWGlobalWarFaction Faction, SWGlobalWarRelations Relations, TimeSpan WarAvailableAt,
    bool OwnTruceProposal, bool OpponentTruceProposal, List<SWGlobalWarPoliticalAction> Actions,
    List<SWGlobalWarNeutralView> Neutrals, List<SWGlobalWarSanctionView> Sanctions,
    bool CanReadMessages, Guid LatestMessage, List<SWGlobalWarPoliticalResultView> RecentResults,
    long? Treasury, Guid SurrenderConfirmation, TimeSpan? ConfirmationExpires);

[Serializable, NetSerializable]
public sealed record SWGlobalWarNeutralView(ProtoId<SWGlobalWarPlanetPrototype> Planet,
    SWGlobalWarNeutralPhase Phase, SWGlobalWarFaction Claimant, TimeSpan Remaining, bool Paused,
    long? OwnContribution, long? ContributionLimit, TimeSpan FundingEndsAt, bool CanSponsor, bool CanContest);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSanctionView(SWGlobalWarFaction Faction, ProtoId<SWGlobalWarCommodityPrototype> Commodity);

[Serializable, NetSerializable]
public sealed record SWGlobalWarGovernmentMessageView(Guid Id, string Author, SWGlobalWarFaction Faction,
    TimeSpan At, string Text);

[Serializable, NetSerializable]
public sealed record SWGlobalWarPoliticalResultView(Guid Request, bool Accepted, string Reason,
    Guid Confirmation, TimeSpan RetryAfter = default);

[Serializable, NetSerializable]
public sealed class SWGlobalWarDiplomacySnapshot(SWGlobalWarDiplomacyView view) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarDiplomacyView View = view;
}

/// <summary>The server derives the actor, faction and authority from the actual BUI sender.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarPoliticalRequest(Guid generation, Guid request, long assignmentRevision,
    SWGlobalWarPoliticalAction action, long relationsRevision,
    ProtoId<SWGlobalWarPlanetPrototype>? sector = null, ProtoId<SWGlobalWarCommodityPrototype>? commodity = null,
    bool enabled = false, long amount = 0, string text = "", Guid confirmation = default) : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly Guid Request = request;
    public readonly long AssignmentRevision = assignmentRevision;
    public readonly SWGlobalWarPoliticalAction Action = action;
    public readonly long RelationsRevision = relationsRevision;
    public readonly ProtoId<SWGlobalWarPlanetPrototype>? Sector = sector;
    public readonly ProtoId<SWGlobalWarCommodityPrototype>? Commodity = commodity;
    public readonly bool Enabled = enabled;
    public readonly long Amount = amount;
    public readonly string Text = text;
    public readonly Guid Confirmation = confirmation;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarPoliticalResult(SWGlobalWarPoliticalResultView result) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarPoliticalResultView Result = result;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarHistoryRequest(Guid generation, long assignmentRevision, Guid before = default)
    : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly long AssignmentRevision = assignmentRevision;
    public readonly Guid Before = before;
}

/// <summary>A bounded page of private correspondence, checked against the current office on every request.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarHistoryPage(Guid generation, long assignmentRevision, Guid before,
    bool allowed, bool hasOlder, List<SWGlobalWarGovernmentMessageView> messages) : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly long AssignmentRevision = assignmentRevision;
    public readonly Guid Before = before;
    public readonly bool Allowed = allowed;
    public readonly bool HasOlder = hasOlder;
    public readonly List<SWGlobalWarGovernmentMessageView> Messages = messages;
}
