using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Integer Galactic Credits. Reservations are encumbrances of balances, never another balance.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarLedgerSystem))]
public sealed partial class SWGlobalWarLedgerComponent : Component
{
    public Guid Generation;
    public long Revision;
    public Dictionary<(SWGlobalWarFaction, SWGlobalWarAccount), long> Balances = new();
    public Dictionary<Guid, SWGlobalWarMoneyReservation> Reservations = new();
    public Dictionary<(NetUserId?, Guid), SWGlobalWarLedgerReceipt> Requests = new();
    public Dictionary<Guid, SWGlobalWarLedgerBatchReceipt> SystemBatches = new();
    public Dictionary<Guid, SWGlobalWarMoneyAllocation> Allocations = new();
    public List<SWGlobalWarLedgerEntry> Journal = new();
    public TimeSpan NextCheck;
}

public sealed record SWGlobalWarMoneyReservation(Guid Id, SWGlobalWarFaction Faction, SWGlobalWarAccount Account,
    long Remaining, TimeSpan? Expires, NetUserId? Employee, Guid? Assignment, string Purpose);

public enum SWGlobalWarLedgerAction : byte
{
    Transfer,
    Reserve,
    Commit,
    Release,
    Credit,
    Debit,
    CommitTransfer,
}

/// <summary>Server-side intent. Player commands cannot choose another faction or mint credits.</summary>
public sealed record SWGlobalWarLedgerCommand(Guid Generation, Guid Request, long ObservedRevision,
    SWGlobalWarLedgerAction Action, SWGlobalWarFaction Faction, SWGlobalWarAccount Account,
    long Amount = 0, SWGlobalWarAccount Destination = SWGlobalWarAccount.Treasury, Guid Reservation = default,
    NetUserId? Employee = null, string Purpose = "", Guid Allocation = default);

/// <summary>A portion of an existing reservation, never a second encumbrance of the account balance.</summary>
public sealed record SWGlobalWarMoneyAllocation(Guid Operation, Guid Reservation, long Amount, string Purpose,
    SWGlobalWarMoneyAllocationStatus Status = SWGlobalWarMoneyAllocationStatus.Active);

public enum SWGlobalWarMoneyAllocationStatus : byte
{
    Active,
    Committed,
    Released,
}

public sealed record SWGlobalWarLedgerReceipt(SWGlobalWarLedgerCommand Command, bool Success, Guid Reservation,
    long EffectAmount = 0, SWGlobalWarFinanceFailure Failure = SWGlobalWarFinanceFailure.None);

/// <summary>A server settlement retains the exact commands, including their original observed revisions.</summary>
public sealed record SWGlobalWarLedgerBatchReceipt(IReadOnlyList<SWGlobalWarLedgerCommand> Commands, bool Success);

public sealed record SWGlobalWarLedgerEntry(long Revision, TimeSpan At, NetUserId? Author,
    SWGlobalWarLedgerCommand Command, Guid Reservation, long EffectAmount, string AuthorName,
    SWGlobalWarRole? AuthorRole, SWGlobalWarFinanceEntryKind Kind);

public readonly record struct SWGlobalWarAccountBalance(long Balance, long Reserved)
{
    public long Available => Balance - Reserved;
}

[ByRefEvent]
public readonly record struct SWGlobalWarLedgerChangedEvent(Guid Generation, long Revision);
