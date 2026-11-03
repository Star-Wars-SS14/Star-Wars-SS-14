using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Politics;

[Serializable, NetSerializable]
public enum SWGlobalWarFinanceAction : byte { Transfer, Reserve, Release }

[Serializable, NetSerializable]
public enum SWGlobalWarFinanceFailure : byte
{
    None,
    Denied,
    InvalidRequest,
    InvalidAmount,
    SameAccount,
    InsufficientFunds,
    InvalidPurpose,
    EmployeeChanged,
    OrderUnavailable,
    CapacityExceeded,
}

[Serializable, NetSerializable]
public enum SWGlobalWarFinanceEntryKind : byte { Ordinary, Opening, NeutralSponsorship, OrderExpired }

[Serializable, NetSerializable]
public sealed record SWGlobalWarFinanceView(Guid Generation, long Revision, TimeSpan Time,
    SWGlobalWarViewAvailability Availability, Guid Assignment, long AssignmentRevision,
    bool CanTransfer, List<SWGlobalWarBudgetView> Budgets, List<SWGlobalWarEmployeeView> Employees,
    List<SWGlobalWarFinanceResultView> RecentResults);

[Serializable, NetSerializable]
public sealed record SWGlobalWarBudgetView(SWGlobalWarAccount Account, long Balance, long Reserved, bool CanManage);

[Serializable, NetSerializable]
public sealed record SWGlobalWarEmployeeView(NetUserId User, string Name, SWGlobalWarAccount Account);

[Serializable, NetSerializable]
public sealed record SWGlobalWarOrderView(Guid Id, SWGlobalWarAccount Account, long Remaining,
    TimeSpan? Expires, string Employee, string Purpose, bool CanRelease);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFinanceEntryView(long Revision, TimeSpan At, string Action,
    SWGlobalWarAccount Account, SWGlobalWarAccount? Destination, long Amount, string Purpose,
    string AuthorName, SWGlobalWarRole? AuthorRole, SWGlobalWarFinanceEntryKind Kind);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFinanceResultView(Guid Request, bool Accepted, Guid Reservation, long EffectAmount = 0,
    SWGlobalWarFinanceFailure Failure = SWGlobalWarFinanceFailure.None);

[Serializable, NetSerializable]
public sealed class SWGlobalWarFinanceSnapshot(SWGlobalWarFinanceView view) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarFinanceView View = view;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFinanceRequest(Guid generation, Guid request, long assignmentRevision,
    SWGlobalWarFinanceAction action, SWGlobalWarAccount account, long amount = 0,
    SWGlobalWarAccount destination = SWGlobalWarAccount.Treasury, Guid reservation = default,
    NetUserId? employee = null, string purpose = "") : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly Guid Request = request;
    public readonly long AssignmentRevision = assignmentRevision;
    public readonly SWGlobalWarFinanceAction Action = action;
    public readonly SWGlobalWarAccount Account = account;
    public readonly long Amount = amount;
    public readonly SWGlobalWarAccount Destination = destination;
    public readonly Guid Reservation = reservation;
    public readonly NetUserId? Employee = employee;
    public readonly string Purpose = purpose;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFinanceResult(SWGlobalWarFinanceResultView result) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarFinanceResultView Result = result;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFinancePageRequest(Guid generation, long assignmentRevision,
    bool journal, long beforeRevision = long.MaxValue, Guid beforeOrder = default) : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly long AssignmentRevision = assignmentRevision;
    public readonly bool Journal = journal;
    public readonly long BeforeRevision = beforeRevision;
    public readonly Guid BeforeOrder = beforeOrder;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFinancePage(Guid generation, long assignmentRevision, bool journal,
    long beforeRevision, Guid beforeOrder, bool allowed, bool hasOlder,
    List<SWGlobalWarOrderView> orders, List<SWGlobalWarFinanceEntryView> entries) : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly long AssignmentRevision = assignmentRevision;
    public readonly bool Journal = journal;
    public readonly long BeforeRevision = beforeRevision;
    public readonly Guid BeforeOrder = beforeOrder;
    public readonly bool Allowed = allowed;
    public readonly bool HasOlder = hasOlder;
    public readonly List<SWGlobalWarOrderView> Orders = orders;
    public readonly List<SWGlobalWarFinanceEntryView> Entries = entries;
}
