using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Politics;

/// <summary>Public lifecycle signal only. A fresh watch still authenticates the recipient and attachment.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarServiceAvailable : EntityEventArgs;

[Serializable, NetSerializable]
public enum SWGlobalWarAppointmentAction : byte
{
    Offer, Accept, Decline, Resign, Dismiss, ClaimHead, DeclineSuccession,
}

[Serializable, NetSerializable]
public enum SWGlobalWarAppointmentFailure : byte
{
    None, Denied, Changed, VacancyClosed, InvalidRequest, AuthorityChanged, RoleOccupied, EligibilityChanged,
    OfferUnavailable, LocationForbidden, ActorChanged, NoAppointment, CannotAppoint, TargetUnavailable,
    TargetChanged, SameRole, TargetIsHead, SuccessionStage,
}

[Serializable, NetSerializable]
public enum SWGlobalWarOfferStatus : byte { Pending, Accepted, Declined }

[Serializable, NetSerializable]
public sealed record SWGlobalWarAppointmentOfferView(Guid Id, string Sender, SWGlobalWarRole SenderRole,
    SWGlobalWarRole Role, SWGlobalWarAppointmentFailure Failure);

[Serializable, NetSerializable]
public sealed record SWGlobalWarOutgoingOfferView(Guid Id, string Recipient, SWGlobalWarRole Role,
    SWGlobalWarOfferStatus Status, SWGlobalWarAppointmentFailure Failure);

[Serializable, NetSerializable]
public sealed record SWGlobalWarPersonnelRoleView(ProtoId<JobPrototype> Job, SWGlobalWarRole Role, bool Available);

[Serializable, NetSerializable]
public sealed record SWGlobalWarPersonnelTargetView(NetUserId User, string Name, long Revision,
    SWGlobalWarRole? Role, bool CanDismiss, List<ProtoId<JobPrototype>> Jobs);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSuccessionView(Guid Vacancy, int Stage, TimeSpan Remaining, bool Eligible);

[Serializable, NetSerializable]
public sealed record SWGlobalWarAppointmentResultView(Guid Request, SWGlobalWarAppointmentAction Action,
    bool Accepted, SWGlobalWarAppointmentFailure Failure);

/// <summary>Only this actor's offers and permitted personnel choices; never the full appointment registry.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarCharacterView(Guid Generation, long Revision, NetEntity Body, TimeSpan Time,
    SWGlobalWarViewAvailability Availability, SWGlobalWarFaction Faction, Guid Assignment, long AssignmentRevision,
    SWGlobalWarRole? Role, SWGlobalWarAuthority Authority, bool Living, string Head, TimeSpan? HeadReserveRemaining,
    List<SWGlobalWarAppointmentOfferView> Offers, SWGlobalWarSuccessionView? Succession,
    List<SWGlobalWarPersonnelRoleView> Roles, List<SWGlobalWarPersonnelTargetView> Targets,
    List<SWGlobalWarAppointmentResultView> RecentResults, List<SWGlobalWarOutgoingOfferView> Outgoing,
    SWGlobalWarWithdrawalView? Withdrawal = null);

/// <summary>The body and cookie identify a viewing attachment, not permission to act for another player.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarCharacterWatch(Guid cookie, NetEntity? body) : EntityEventArgs
{
    public readonly Guid Cookie = cookie;
    public readonly NetEntity? Body = body;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarCharacterSnapshot(Guid cookie, long sequence, SWGlobalWarCharacterView view) : EntityEventArgs
{
    public readonly Guid Cookie = cookie;
    public readonly long Sequence = sequence;
    public readonly SWGlobalWarCharacterView View = view;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarAppointmentRequest(Guid cookie, Guid generation, Guid request,
    SWGlobalWarAppointmentAction action, long actorRevision, NetUserId target = default, long targetRevision = 0,
    ProtoId<JobPrototype>? job = null, Guid offer = default, Guid vacancy = default, int stage = -1) : EntityEventArgs
{
    public readonly Guid Cookie = cookie;
    public readonly Guid Generation = generation;
    public readonly Guid Request = request;
    public readonly SWGlobalWarAppointmentAction Action = action;
    public readonly long ActorRevision = actorRevision;
    public readonly NetUserId Target = target;
    public readonly long TargetRevision = targetRevision;
    public readonly ProtoId<JobPrototype>? Job = job;
    public readonly Guid Offer = offer;
    public readonly Guid Vacancy = vacancy;
    public readonly int Stage = stage;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarAppointmentResult(Guid cookie, SWGlobalWarAppointmentResultView result) : EntityEventArgs
{
    public readonly Guid Cookie = cookie;
    public readonly SWGlobalWarAppointmentResultView Result = result;
}
