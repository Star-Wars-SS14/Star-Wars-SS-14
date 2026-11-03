using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>One existing person's withdrawal, never a replacement life or a transferable travel pass.</summary>
public sealed record SWGlobalWarWithdrawalOrder(Guid Id, Guid Generation, NetUserId User, EntityUid Mind,
    EntityUid Body, SWGlobalWarFaction Faction, EntityUid Sector, TimeSpan Deadline,
    SWGlobalWarWithdrawalStatus Status = SWGlobalWarWithdrawalStatus.Countdown, TimeSpan NextAttempt = default,
    ProtoId<SWGlobalWarPlanetPrototype>? Destination = null,
    SWGlobalWarWithdrawalReason Reason = SWGlobalWarWithdrawalReason.Truce, bool ShipReturned = false,
    bool WarResumed = false);

/// <summary>Independent ship group; ownership and every physical passenger are rechecked before any detachment.</summary>
public sealed record SWGlobalWarShipWithdrawalOrder(Guid Id, Guid Generation, EntityUid Ship,
    SWGlobalWarFaction Faction, long OwnershipRevision, EntityUid Sector, TimeSpan Deadline,
    TimeSpan NextAttempt = default, SWGlobalWarWithdrawalStatus Status = SWGlobalWarWithdrawalStatus.Countdown,
    SWGlobalWarWithdrawalReason Reason = SWGlobalWarWithdrawalReason.Truce);
