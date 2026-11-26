using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarLedgerSystem
{
    /// <summary>Reads only this session's receipt without exposing commands or another government's ledger.</summary>
    public SWGlobalWarFinanceResultView? GetOwnFinanceResult(ICommonSession session, Guid generation, Guid request)
    {
        if (!TryGetLedger(out var ledger) || ledger.Comp.Generation != generation ||
            !ledger.Comp.Requests.TryGetValue((session.UserId, request), out var receipt))
            return null;
        return new SWGlobalWarFinanceResultView(request, receipt.Success, receipt.Reservation,
            receipt.EffectAmount, receipt.Failure);
    }

    /// <summary>Projects only the current office's accounts and its own faction's eligible employees.</summary>
    public SWGlobalWarFinanceView GetFinanceView(ICommonSession? session, SWGlobalWarFaction faction)
    {
        if (!TryGetLedger(out var ledger))
            return EmptyFinance(SWGlobalWarViewAvailability.Unavailable);
        if (session == null || !_appointments.TryGetAssignment(session.UserId, out var assignment) ||
            !_appointments.TryGetActor(session, ledger.Comp.Generation, assignment.Revision, out assignment) ||
            assignment.Faction != faction)
            return EmptyFinance(SWGlobalWarViewAvailability.Forbidden, ledger.Comp.Generation, ledger.Comp.Revision);
        var authority = _appointments.GetAuthority(assignment);
        var head = assignment.Role == SWGlobalWarRole.Head;
        var budgets = new List<SWGlobalWarBudgetView>();
        foreach (var account in Enum.GetValues<SWGlobalWarAccount>())
        {
            var manage = (authority & AccountAuthority(account)) != 0;
            if (!head && !manage)
                continue;
            var balance = GetBalance(faction, account);
            budgets.Add(new SWGlobalWarBudgetView(account, balance.Balance, balance.Reserved, manage));
        }
        var employees = new List<SWGlobalWarEmployeeView>();
        if (_appointments.TryGetRegistry(out var registry))
            foreach (var employee in registry.Comp.Assignments.Values)
            foreach (var account in budgets.Where(b => b.CanManage))
                if (employee.Faction == faction && EmployeeAccount(employee.Role, account.Account))
                    employees.Add(new SWGlobalWarEmployeeView(employee.User, Name(employee.Body), account.Account));
        var results = ledger.Comp.Requests.Where(p => p.Key.Item1 == session.UserId &&
                (budgets.Any(b => b.Account == p.Value.Command.Account) || p.Value.Command.Employee == session.UserId))
            .TakeLast(32).Select(p => new SWGlobalWarFinanceResultView(p.Key.Item2, p.Value.Success,
                p.Value.Reservation, p.Value.EffectAmount, p.Value.Failure)).ToList();
        return new SWGlobalWarFinanceView(ledger.Comp.Generation, ledger.Comp.Revision, Now(),
            SWGlobalWarViewAvailability.Ready, assignment.Id, assignment.Revision,
            (authority & SWGlobalWarAuthority.Treasury) != 0, budgets, employees, results);
    }

    /// <summary>Returns at most twenty authorized orders or journal entries; cursors never grant access.</summary>
    public SWGlobalWarFinancePage GetFinancePage(ICommonSession? session, SWGlobalWarFaction faction,
        SWGlobalWarFinancePageRequest request)
    {
        var denied = new SWGlobalWarFinancePage(request.Generation, request.AssignmentRevision, request.Journal,
            request.BeforeRevision, request.BeforeOrder, false, false, [], []);
        if (session == null || !TryGetLedger(out var ledger) || ledger.Comp.Generation != request.Generation ||
            !_appointments.TryGetActor(session, request.Generation, request.AssignmentRevision, out var assignment) ||
            assignment.Faction != faction)
            return denied;
        var authority = _appointments.GetAuthority(assignment);
        bool AccountVisible(SWGlobalWarAccount account) => assignment.Role == SWGlobalWarRole.Head ||
            (authority & AccountAuthority(account)) != 0;
        if (request.Journal)
        {
            if (request.BeforeRevision != long.MaxValue && !ledger.Comp.Journal.Any(e =>
                    e.Revision == request.BeforeRevision && e.Command.Faction == faction &&
                    (AccountVisible(e.Command.Account) || IsTransfer(e.Command.Action) && AccountVisible(e.Command.Destination))))
                return denied;
            var entries = ledger.Comp.Journal.Where(e => e.Revision < request.BeforeRevision && e.Command.Faction == faction &&
                    (AccountVisible(e.Command.Account) || IsTransfer(e.Command.Action) && AccountVisible(e.Command.Destination)))
                .TakeLast(21).ToArray();
            return new SWGlobalWarFinancePage(request.Generation, request.AssignmentRevision, true,
                request.BeforeRevision, request.BeforeOrder, true, entries.Length > 20, [], entries.TakeLast(20)
                    .Select(e => new SWGlobalWarFinanceEntryView(e.Revision, e.At,
                        "sw-global-war-finance-operation-" + e.Command.Action.ToString().ToLowerInvariant(), e.Command.Account,
                        IsTransfer(e.Command.Action) ? e.Command.Destination : (SWGlobalWarAccount?) null,
                        e.EffectAmount, e.Kind == SWGlobalWarFinanceEntryKind.Ordinary ? e.Command.Purpose : string.Empty,
                        e.AuthorName, e.AuthorRole, e.Kind)).ToList());
        }
        var orders = ledger.Comp.Reservations.Values.Where(r => r.Faction == faction &&
            (AccountVisible(r.Account) || r.Employee == assignment.User)).ToList();
        var end = request.BeforeOrder == Guid.Empty ? orders.Count : orders.FindIndex(r => r.Id == request.BeforeOrder);
        if (end < 0)
            return denied;
        var start = Math.Max(0, end - 20);
        var page = new List<SWGlobalWarOrderView>();
        for (var i = start; i < end; i++)
        {
            var order = orders[i];
            var employeeName = order.Employee is { } user && _appointments.TryGetAssignment(user, out var employee)
                ? Name(employee.Body) : string.Empty;
            page.Add(new SWGlobalWarOrderView(order.Id, order.Account, order.Remaining, order.Expires,
                employeeName, order.Purpose, (authority & AccountAuthority(order.Account)) != 0));
        }
        return new SWGlobalWarFinancePage(request.Generation, request.AssignmentRevision, false,
            request.BeforeRevision, request.BeforeOrder, true, start > 0, page, []);
    }

    private static bool IsTransfer(SWGlobalWarLedgerAction action)
    {
        return action is SWGlobalWarLedgerAction.Transfer or SWGlobalWarLedgerAction.CommitTransfer;
    }

    private static SWGlobalWarFinanceView EmptyFinance(SWGlobalWarViewAvailability availability,
        Guid generation = default, long revision = 0)
    {
        return new SWGlobalWarFinanceView(generation, revision, TimeSpan.Zero, availability, Guid.Empty,
            0, false, [], [], []);
    }
}
