using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>One checked integer ledger per campaign; no connection to the legacy float bank or faction rule.</summary>
public sealed partial class SWGlobalWarLedgerSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _appointments = default!;
    private int _notificationDepth;
    private readonly Dictionary<EntityUid, Entity<SWGlobalWarLedgerComponent>> _pendingNotifications = new();

    /// <summary>Defers notifications until a validated server operation has also recorded its non-monetary result.</summary>
    public IDisposable DeferNotifications()
    {
        _notificationDepth++;
        return new NotificationScope(this);
    }

    private sealed class NotificationScope(SWGlobalWarLedgerSystem owner) : IDisposable
    {
        private SWGlobalWarLedgerSystem? _owner = owner;

        public void Dispose()
        {
            var current = _owner;
            _owner = null;
            current?.FinishNotifications();
        }
    }

    private void FinishNotifications()
    {
        if (--_notificationDepth != 0)
            return;
        var pending = _pendingNotifications.Values.ToArray();
        _pendingNotifications.Clear();
        foreach (var ledger in pending)
            Notify(ledger);
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>(OnStarted);
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>(OnStopped);
        SubscribeLocalEvent<SWGlobalWarAssignmentsChangedEvent>(OnAssignmentsChanged);
    }

    /// <summary>Resolves the ledger of the active, non-ending generation.</summary>
    public bool TryGetLedger(out Entity<SWGlobalWarLedgerComponent> ledger)
    {
        ledger = default;
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending ||
            !TryComp<SWGlobalWarLedgerComponent>(campaign.Owner, out var state) || state.Generation != campaign.Comp.Generation)
            return false;
        ledger = (campaign.Owner, state);
        return true;
    }

    /// <summary>Returns immutable accounting data for server-side authorization and private presentation.</summary>
    public bool TryGetReservation(Guid id, out SWGlobalWarMoneyReservation reservation)
    {
        reservation = default!;
        return TryGetLedger(out var ledger) && ledger.Comp.Reservations.TryGetValue(id, out reservation!);
    }

    /// <summary>Returns immutable accounting data for server-side authorization and private presentation.</summary>
    public SWGlobalWarAccountBalance GetBalance(SWGlobalWarFaction faction, SWGlobalWarAccount account)
    {
        if (!TryGetLedger(out var ledger) || !ledger.Comp.Balances.TryGetValue((faction, account), out var balance))
            return default;
        return GetBalance(ledger.Comp, faction, account);
    }

    private static SWGlobalWarAccountBalance GetBalance(SWGlobalWarLedgerComponent ledger,
        SWGlobalWarFaction faction, SWGlobalWarAccount account)
    {
        var balance = ledger.Balances[(faction, account)];
        var reserved = ledger.Reservations.Values.Where(r => r.Faction == faction && r.Account == account)
            .Sum(r => r.Remaining);
        return new SWGlobalWarAccountBalance(balance, reserved);
    }

    /// <summary>Runs a player intent only with a current, living appointment and the required account permission.</summary>
    public bool TryExecute(ICommonSession session, long assignmentRevision, SWGlobalWarLedgerCommand command,
        out Guid reservation)
    {
        return TryExecute(session, assignmentRevision, command, out reservation, out _);
    }

    /// <summary>Returns the original accounting refusal after authorization; unauthorized callers get no account details.</summary>
    public bool TryExecute(ICommonSession session, long assignmentRevision, SWGlobalWarLedgerCommand command,
        out Guid reservation, out SWGlobalWarFinanceFailure failure)
    {
        reservation = Guid.Empty;
        failure = SWGlobalWarFinanceFailure.Denied;
        if (!TryGetLedger(out var ledger) || ledger.Comp.Generation != command.Generation ||
            !_appointments.TryGetActor(session, command.Generation, assignmentRevision, out var actor) ||
            actor.Faction != command.Faction || command.Action is SWGlobalWarLedgerAction.Credit or SWGlobalWarLedgerAction.Debit)
            return false;
        var permission = AccountAuthority(command.Account);
        var authority = _appointments.GetAuthority(actor);
        if (command.Action == SWGlobalWarLedgerAction.Transfer)
        {
            if ((authority & SWGlobalWarAuthority.Treasury) == 0)
                return false;
        }
        else if (command.Action is SWGlobalWarLedgerAction.Commit or SWGlobalWarLedgerAction.CommitTransfer)
        {
            // Goods and sponsorship must be committed by their server systems together with their own result.
            // A client cannot burn a reservation and pretend that an item or influence was delivered.
            return false;
        }
        else if ((authority & permission) == 0)
            return false;
        if (command.Action == SWGlobalWarLedgerAction.Reserve && command.Employee == null)
            return false;
        var success = Execute(ledger, session.UserId, command, out reservation);
        failure = ledger.Comp.Requests.TryGetValue((session.UserId, command.Request), out var receipt) &&
                  receipt.Command == command ? receipt.Failure : SWGlobalWarFinanceFailure.InvalidRequest;
        return success;
    }

    /// <summary>Server-only source/sink adapter for actual consumption, funded orders and military events.</summary>
    public bool TryExecuteSystem(SWGlobalWarLedgerCommand command, out Guid reservation, NetUserId? initiator = null)
    {
        reservation = Guid.Empty;
        return TryGetLedger(out var ledger) && ledger.Comp.Generation == command.Generation &&
               Execute(ledger, initiator, command, out reservation, system: true);
    }

    /// <summary>
    /// Settles a bounded server plan atomically. The caller defers notifications until its prevalidated goods plan
    /// and receipt are complete. This method cannot create reservations or accept player-selected commands.
    /// </summary>
    public bool TryExecuteSystemBatch(Guid operation, IReadOnlyList<SWGlobalWarLedgerCommand> commands)
    {
        if (operation == Guid.Empty || commands.Count is < 1 or > 64 || !TryGetLedger(out var ledger))
            return false;
        var plan = commands.ToArray();
        if (plan.Any(c => c.Generation != ledger.Comp.Generation || c.Request == Guid.Empty ||
                          !Enum.IsDefined(c.Account) ||
                          c.Faction is not (SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire) ||
                          c.Action is not (SWGlobalWarLedgerAction.Credit or SWGlobalWarLedgerAction.Debit or
                              SWGlobalWarLedgerAction.Commit or SWGlobalWarLedgerAction.CommitTransfer)) ||
            plan.Select(c => c.Request).Distinct().Count() != plan.Length)
            return false;
        if (ledger.Comp.SystemBatches.TryGetValue(operation, out var prior))
            return prior.Success && prior.Commands.SequenceEqual(plan);
        // A command cannot be imported from a different completed plan or from a standalone accounting operation.
        if (plan.Any(c => ledger.Comp.Requests.ContainsKey((null, c.Request))))
            return false;

        using var notifications = DeferNotifications();
        var expired = Expire(ledger);
        var staged = new SWGlobalWarLedgerComponent
        {
            Generation = ledger.Comp.Generation,
            Balances = new(ledger.Comp.Balances),
            Reservations = new(ledger.Comp.Reservations),
            Allocations = new(ledger.Comp.Allocations),
        };
        var failure = SWGlobalWarFinanceFailure.None;
        foreach (var command in plan)
        {
            failure = Apply(staged, command, out _);
            if (failure != SWGlobalWarFinanceFailure.None)
                break;
        }
        var success = failure == SWGlobalWarFinanceFailure.None;
        if (success)
        {
            ledger.Comp.Balances = staged.Balances;
            ledger.Comp.Reservations = staged.Reservations;
            ledger.Comp.Allocations = staged.Allocations;
        }
        foreach (var command in plan)
        {
            var reservation = command.Action is SWGlobalWarLedgerAction.Commit or SWGlobalWarLedgerAction.CommitTransfer
                ? command.Reservation : Guid.Empty;
            ledger.Comp.Requests.Add((null, command.Request), new SWGlobalWarLedgerReceipt(command, success,
                reservation, success ? command.Amount : 0, failure));
            if (success)
                ledger.Comp.Journal.Add(new SWGlobalWarLedgerEntry(++ledger.Comp.Revision, Now(), null, command,
                    reservation, command.Amount, string.Empty, null, SWGlobalWarFinanceEntryKind.Ordinary));
        }
        ledger.Comp.SystemBatches.Add(operation, new SWGlobalWarLedgerBatchReceipt(Array.AsReadOnly(plan), success));
        if (success || expired)
            Notify(ledger);
        return success;
    }

    /// <summary>Verifies a worker's still-current office, order, purpose and unspent amount before D delivers goods.</summary>
    public bool CanSpendOrder(ICommonSession session, Guid generation, long assignmentRevision,
        Guid reservation, long amount, string purpose)
    {
        if (amount <= 0 || !TryGetLedger(out var ledger) || ledger.Comp.Generation != generation ||
            !_appointments.TryGetActor(session, generation, assignmentRevision, out var employee))
            return false;
        if (Expire(ledger))
            Notify(ledger);
        return ledger.Comp.Reservations.TryGetValue(reservation, out var order) && order.Employee == session.UserId &&
               order.Assignment == employee.Id && order.Faction == employee.Faction && order.Purpose == purpose &&
               order.Remaining - Allocated(ledger.Comp, reservation) >= amount;
    }

    private bool Execute(Entity<SWGlobalWarLedgerComponent> ledger, NetUserId? author,
        SWGlobalWarLedgerCommand command, out Guid reservation, bool system = false)
    {
        reservation = Guid.Empty;
        if (command.Request == Guid.Empty || !Enum.IsDefined(command.Account) ||
            command.Faction is not (SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire))
            return false;
        var key = (system ? null : author, command.Request);
        if (ledger.Comp.Requests.TryGetValue(key, out var receipt))
        {
            if (receipt.Command != command)
                return false;
            reservation = receipt.Reservation;
            return receipt.Success;
        }
        var expired = Expire(ledger);
        var effectAmount = command.Action == SWGlobalWarLedgerAction.Release &&
                           FindReservation(ledger.Comp, command, out var releasing) ? releasing.Remaining : command.Amount;
        var authorName = string.Empty;
        SWGlobalWarRole? authorRole = null;
        if (author is { } user && _appointments.TryGetAssignment(user, out var appointment))
        {
            authorName = Name(appointment.Body);
            authorRole = appointment.Role;
        }
        // No event is raised between validation and recording the complete accounting result.
        // ObservedRevision is audit context, not a precondition on unrelated accounts or another faction's tax.
        var failure = Apply(ledger.Comp, command, out reservation);
        var success = failure == SWGlobalWarFinanceFailure.None;
        ledger.Comp.Requests.Add(key, new SWGlobalWarLedgerReceipt(command, success, reservation,
            success ? effectAmount : 0, failure));
        if (success)
        {
            ledger.Comp.Revision++;
            ledger.Comp.Journal.Add(new SWGlobalWarLedgerEntry(ledger.Comp.Revision, Now(), author, command,
                reservation, effectAmount, authorName, authorRole,
                command.Action == SWGlobalWarLedgerAction.Debit && command.Purpose.StartsWith("neutral-sponsorship:", StringComparison.Ordinal)
                    ? SWGlobalWarFinanceEntryKind.NeutralSponsorship : SWGlobalWarFinanceEntryKind.Ordinary));
        }
        if (success || expired)
            Notify(ledger);
        return success;
    }

    private SWGlobalWarFinanceFailure Apply(SWGlobalWarLedgerComponent ledger, SWGlobalWarLedgerCommand command,
        out Guid reservation)
    {
        reservation = Guid.Empty;
        if (command.Allocation != Guid.Empty && command.Action is not
            (SWGlobalWarLedgerAction.Commit or SWGlobalWarLedgerAction.CommitTransfer))
            return SWGlobalWarFinanceFailure.InvalidRequest;
        var key = (command.Faction, command.Account);
        var balance = GetBalance(ledger, command.Faction, command.Account);
        if (command.Action is not SWGlobalWarLedgerAction.Release && command.Amount <= 0)
            return SWGlobalWarFinanceFailure.InvalidAmount;
        switch (command.Action)
        {
            case SWGlobalWarLedgerAction.Transfer:
                if (!Enum.IsDefined(command.Destination))
                    return SWGlobalWarFinanceFailure.InvalidRequest;
                if (command.Account == command.Destination)
                    return SWGlobalWarFinanceFailure.SameAccount;
                if (command.Amount > balance.Available)
                    return SWGlobalWarFinanceFailure.InsufficientFunds;
                var target = (command.Faction, command.Destination);
                if (!CanAdd(ledger.Balances[target], command.Amount))
                    return SWGlobalWarFinanceFailure.CapacityExceeded;
                ledger.Balances[key] -= command.Amount;
                ledger.Balances[target] += command.Amount;
                return SWGlobalWarFinanceFailure.None;

            case SWGlobalWarLedgerAction.Reserve:
                if (command.Amount > balance.Available)
                    return SWGlobalWarFinanceFailure.InsufficientFunds;
                if (string.IsNullOrWhiteSpace(command.Purpose) || command.Purpose.Length > 200)
                    return SWGlobalWarFinanceFailure.InvalidPurpose;
                Guid? appointment = null;
                if (command.Employee is { } user)
                {
                    if (!_appointments.TryGetAssignment(user, out var employee) ||
                        employee.Faction != command.Faction || !EmployeeAccount(employee.Role, command.Account))
                        return SWGlobalWarFinanceFailure.EmployeeChanged;
                    appointment = employee.Id;
                }
                reservation = Guid.NewGuid();
                ledger.Reservations.Add(reservation, new SWGlobalWarMoneyReservation(reservation,
                    command.Faction, command.Account, command.Amount,
                    command.Employee != null ? Now() + TimeSpan.FromMinutes(15) : null,
                    command.Employee, appointment, command.Purpose));
                return SWGlobalWarFinanceFailure.None;

            case SWGlobalWarLedgerAction.Commit:
            case SWGlobalWarLedgerAction.CommitTransfer:
                if (!FindReservation(ledger, command, out var spending) || command.Amount > spending.Remaining ||
                    !CanCommitAllocation(ledger, command, spending))
                    return SWGlobalWarFinanceFailure.OrderUnavailable;
                if (command.Action == SWGlobalWarLedgerAction.CommitTransfer)
                {
                    if (!Enum.IsDefined(command.Destination))
                        return SWGlobalWarFinanceFailure.InvalidRequest;
                    if (command.Account == command.Destination)
                        return SWGlobalWarFinanceFailure.SameAccount;
                    var receiving = (command.Faction, command.Destination);
                    if (!CanAdd(ledger.Balances[receiving], command.Amount))
                        return SWGlobalWarFinanceFailure.CapacityExceeded;
                    ledger.Balances[receiving] += command.Amount;
                }
                ledger.Balances[key] -= command.Amount;
                if (command.Allocation != Guid.Empty)
                    ledger.Allocations[command.Allocation] = ledger.Allocations[command.Allocation] with
                    {
                        Status = SWGlobalWarMoneyAllocationStatus.Committed,
                    };
                if (command.Amount == spending.Remaining)
                    ledger.Reservations.Remove(spending.Id);
                else
                    ledger.Reservations[spending.Id] = spending with { Remaining = spending.Remaining - command.Amount };
                reservation = spending.Id;
                return SWGlobalWarFinanceFailure.None;

            case SWGlobalWarLedgerAction.Release:
                if (command.Amount != 0)
                    return SWGlobalWarFinanceFailure.InvalidAmount;
                if (!FindReservation(ledger, command, out var releasing))
                    return SWGlobalWarFinanceFailure.OrderUnavailable;
                ledger.Reservations.Remove(releasing.Id);
                ReleaseAllocations(ledger, releasing.Id);
                reservation = releasing.Id;
                return SWGlobalWarFinanceFailure.None;

            case SWGlobalWarLedgerAction.Credit:
                if (string.IsNullOrWhiteSpace(command.Purpose))
                    return SWGlobalWarFinanceFailure.InvalidPurpose;
                if (!CanAdd(balance.Balance, command.Amount))
                    return SWGlobalWarFinanceFailure.CapacityExceeded;
                ledger.Balances[key] += command.Amount;
                return SWGlobalWarFinanceFailure.None;
            case SWGlobalWarLedgerAction.Debit:
                if (string.IsNullOrWhiteSpace(command.Purpose))
                    return SWGlobalWarFinanceFailure.InvalidPurpose;
                if (command.Amount > balance.Available)
                    return SWGlobalWarFinanceFailure.InsufficientFunds;
                ledger.Balances[key] -= command.Amount;
                return SWGlobalWarFinanceFailure.None;
            default:
                return SWGlobalWarFinanceFailure.InvalidRequest;
        }
    }

    private bool FindReservation(SWGlobalWarLedgerComponent ledger, SWGlobalWarLedgerCommand command,
        out SWGlobalWarMoneyReservation reservation)
    {
        reservation = default!;
        return ledger.Reservations.TryGetValue(command.Reservation, out reservation!) &&
               reservation.Faction == command.Faction && reservation.Account == command.Account;
    }

    private static bool CanAdd(long balance, long amount)
    {
        return amount > 0 && balance <= long.MaxValue - amount;
    }

    private static SWGlobalWarAuthority AccountAuthority(SWGlobalWarAccount account)
    {
        return account switch
        {
            SWGlobalWarAccount.Treasury => SWGlobalWarAuthority.Treasury,
            SWGlobalWarAccount.War => SWGlobalWarAuthority.WarBudget,
            SWGlobalWarAccount.Logistics => SWGlobalWarAuthority.LogisticsBudget,
            SWGlobalWarAccount.Industry => SWGlobalWarAuthority.IndustryBudget,
            _ => SWGlobalWarAuthority.None,
        };
    }

    private static bool EmployeeAccount(SWGlobalWarRole role, SWGlobalWarAccount account)
    {
        return account switch
        {
            SWGlobalWarAccount.Logistics => role == SWGlobalWarRole.LogisticsWorker,
            SWGlobalWarAccount.Industry => role == SWGlobalWarRole.IndustryWorker,
            SWGlobalWarAccount.War => role is SWGlobalWarRole.SeniorOfficer or SWGlobalWarRole.JuniorOfficer or
                SWGlobalWarRole.Pilot or SWGlobalWarRole.Soldier or SWGlobalWarRole.Intelligence or SWGlobalWarRole.ForceUser,
            _ => false,
        };
    }

    private void OnStarted(ref SWGlobalWarCampaignStartedEvent args)
    {
        var ledger = EnsureComp<SWGlobalWarLedgerComponent>(args.Rule);
        if (ledger.Generation == args.Generation)
            return;
        ledger.Generation = args.Generation;
        ledger.Revision = 0;
        ledger.Balances.Clear();
        ledger.Reservations.Clear();
        ledger.Requests.Clear();
        ledger.SystemBatches.Clear();
        ledger.Allocations.Clear();
        ledger.Journal.Clear();
        ledger.NextCheck = TimeSpan.Zero;
        foreach (var faction in new[] { SWGlobalWarFaction.Republic, SWGlobalWarFaction.Empire })
        foreach (var (account, amount) in new[]
                 {
                     (SWGlobalWarAccount.Treasury, 450_000L), (SWGlobalWarAccount.War, 180_000L),
                     (SWGlobalWarAccount.Logistics, 150_000L), (SWGlobalWarAccount.Industry, 120_000L),
                 })
        {
            ledger.Balances.Add((faction, account), amount);
            var opening = new SWGlobalWarLedgerCommand(args.Generation, Guid.NewGuid(), ledger.Revision,
                SWGlobalWarLedgerAction.Credit, faction, account, amount, Purpose: "campaign-opening");
            ledger.Journal.Add(new SWGlobalWarLedgerEntry(++ledger.Revision, TimeSpan.Zero, null, opening,
                Guid.Empty, amount, string.Empty, null, SWGlobalWarFinanceEntryKind.Opening));
        }
    }

    private void OnStopped(ref SWGlobalWarCampaignStoppedEvent args)
    {
        if (TryComp<SWGlobalWarLedgerComponent>(args.Rule, out var ledger) && ledger.Generation == args.Generation)
        {
            foreach (var reservation in ledger.Reservations.Keys)
                ReleaseAllocations(ledger, reservation);
            ledger.Reservations.Clear();
        }
    }

    private void OnAssignmentsChanged(ref SWGlobalWarAssignmentsChangedEvent args)
    {
        if (TryGetLedger(out var ledger) && ledger.Comp.Generation == args.Generation && Expire(ledger))
            Notify(ledger);
    }

    public override void Update(float frameTime)
    {
        if (!TryGetLedger(out var ledger) || Now() < ledger.Comp.NextCheck)
            return;
        ledger.Comp.NextCheck = Now() + TimeSpan.FromSeconds(1);
        if (Expire(ledger))
            Notify(ledger);
    }

    private bool Expire(Entity<SWGlobalWarLedgerComponent> ledger)
    {
        var now = Now();
        var changed = false;
        foreach (var reservation in ledger.Comp.Reservations.Values.ToArray())
        {
            if (!(reservation.Expires is { } expires && now >= expires) &&
                !(reservation.Employee is { } employee &&
                  (!_appointments.TryGetAssignment(employee, out var assignment) || assignment.Id != reservation.Assignment)))
                continue;
            if (!ledger.Comp.Reservations.Remove(reservation.Id))
                continue;
            ReleaseAllocations(ledger.Comp, reservation.Id);
            var release = new SWGlobalWarLedgerCommand(ledger.Comp.Generation, Guid.NewGuid(), ledger.Comp.Revision,
                SWGlobalWarLedgerAction.Release, reservation.Faction, reservation.Account,
                Reservation: reservation.Id, Purpose: "order-expired-or-appointment-ended");
            ledger.Comp.Journal.Add(new SWGlobalWarLedgerEntry(++ledger.Comp.Revision, now, null, release,
                reservation.Id, reservation.Remaining, string.Empty, null, SWGlobalWarFinanceEntryKind.OrderExpired));
            changed = true;
        }
        return changed;
    }

    private TimeSpan Now()
    {
        return _campaign.TryGetCampaign(out var campaign) ? _campaign.Elapsed(campaign) : TimeSpan.Zero;
    }

    private void Notify(Entity<SWGlobalWarLedgerComponent> ledger)
    {
        if (_notificationDepth > 0)
        {
            _pendingNotifications[ledger.Owner] = ledger;
            return;
        }
        if (!TryGetLedger(out var active) || active.Owner != ledger.Owner || active.Comp.Generation != ledger.Comp.Generation)
            return;
        var changed = new SWGlobalWarLedgerChangedEvent(ledger.Comp.Generation, ledger.Comp.Revision);
        RaiseLocalEvent(ref changed);
    }
}
