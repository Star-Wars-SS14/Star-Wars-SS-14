using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Single owner of office occupancy, unfinished Force carriers and entry reservations.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarAssignmentsSystem))]
public sealed partial class SWGlobalWarAssignmentsComponent : Component
{
    public Guid Generation;
    public long Revision;
    public Dictionary<NetUserId, SWGlobalWarAssignment> Assignments = new();
    public Dictionary<Guid, SWGlobalWarAssignmentReservation> Reservations = new();
    public Dictionary<NetUserId, SWGlobalWarForceCarrier> ForceCarriers = new();
    public Dictionary<Guid, SWGlobalWarAppointmentOffer> Offers = new();
    public Dictionary<Guid, SWGlobalWarCompletedOffer> CompletedOffers = new();
    public Dictionary<Guid, TimeSpan> HeadDisconnected = new();
    public Dictionary<SWGlobalWarFaction, SWGlobalWarSuccession> Successions = new();
    public bool InitialAllocationComplete;
    public Dictionary<(NetUserId User, Guid Request), SWGlobalWarAppointmentReceipt> Requests = new();
    public TimeSpan NextCheck;
}

public sealed record SWGlobalWarAssignment(Guid Id, Guid Generation, NetUserId User, EntityUid Mind,
    EntityUid Body, SWGlobalWarFaction Faction, SWGlobalWarRole Role, ProtoId<JobPrototype> Job,
    long Revision, bool DirectEntry);

public sealed record SWGlobalWarAssignmentReservation(Guid Id, Guid Generation, NetUserId User,
    SWGlobalWarFaction Faction, SWGlobalWarRole Role, ProtoId<JobPrototype> Job);

/// <summary>Independent of office or army service, but held in the same authoritative registry.</summary>
public sealed record SWGlobalWarForceCarrier(Guid Id, Guid Generation, NetUserId User, EntityUid Mind,
    EntityUid Body, SWGlobalWarFaction Faction);

public sealed record SWGlobalWarAppointmentOffer(Guid Id, Guid Generation, NetUserId Sender,
    Guid SenderAssignment, long SenderRevision, NetUserId Recipient, EntityUid Mind, EntityUid Body,
    long RecipientRevision, SWGlobalWarFaction Faction, SWGlobalWarRole Role, ProtoId<JobPrototype> Job,
    string SenderName, SWGlobalWarRole SenderRole, EntityUid SenderMind, EntityUid SenderBody, string RecipientName);

public sealed record SWGlobalWarCompletedOffer(SWGlobalWarAppointmentOffer Offer, SWGlobalWarOfferStatus Status);

[ByRefEvent]
public readonly record struct SWGlobalWarAssignmentsChangedEvent(Guid Generation, long Revision);

public sealed record SWGlobalWarAppointmentCommand(Guid Generation, Guid Request,
    SWGlobalWarAppointmentAction Action, long ActorRevision, NetUserId Target, long TargetRevision,
    ProtoId<JobPrototype>? Job = null, Guid Offer = default, Guid Vacancy = default, int Stage = -1);

public sealed record SWGlobalWarAppointmentReceipt(SWGlobalWarAppointmentCommand Command, bool Success, Guid Offer,
    SWGlobalWarAppointmentFailure Failure = SWGlobalWarAppointmentFailure.None, EntityUid? Mind = null,
    EntityUid? Body = null);

public sealed record SWGlobalWarSuccession(Guid Id, int Stage, TimeSpan Deadline,
    Dictionary<NetUserId, Guid> Candidates);
