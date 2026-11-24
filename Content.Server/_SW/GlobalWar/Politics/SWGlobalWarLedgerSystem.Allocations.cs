using System.Linq;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarLedgerSystem
{
    private static long Allocated(SWGlobalWarLedgerComponent ledger, Guid reservation)
    {
        return ledger.Allocations.Values.Where(a => a.Reservation == reservation &&
            a.Status == SWGlobalWarMoneyAllocationStatus.Active).Sum(a => a.Amount);
    }

    /// <summary>
    /// Server work systems authorize their actor and purpose before claiming a portion of a C order.
    /// This does not reserve the same money twice. A cancelled or completed operation cannot claim again.
    /// </summary>
    public bool TryAllocateReservation(Guid generation, Guid operation, Guid reservation, long amount, string purpose)
    {
        if (operation == Guid.Empty || amount <= 0 || string.IsNullOrWhiteSpace(purpose) || purpose.Length > 200 ||
            !TryGetLedger(out var ledger) || ledger.Comp.Generation != generation)
            return false;
        using var notifications = DeferNotifications();
        var expired = Expire(ledger);
        var request = new SWGlobalWarMoneyAllocation(operation, reservation, amount, purpose);
        bool success;
        if (ledger.Comp.Allocations.TryGetValue(operation, out var previous))
            success = previous == request;
        else
        {
            success = ledger.Comp.Reservations.TryGetValue(reservation, out var parent) &&
                parent.Remaining - Allocated(ledger.Comp, reservation) >= amount;
            if (success)
                ledger.Comp.Allocations.Add(operation, request);
        }
        if (expired)
            Notify(ledger);
        return success;
    }

    /// <summary>Checks a work claim against the current parent order without disclosing another account.</summary>
    public bool IsAllocationActive(Guid generation, Guid operation, Guid reservation, long amount)
    {
        return TryGetLedger(out var ledger) && ledger.Comp.Generation == generation &&
            ledger.Comp.Allocations.TryGetValue(operation, out var allocation) &&
            allocation.Status == SWGlobalWarMoneyAllocationStatus.Active && allocation.Reservation == reservation &&
            allocation.Amount == amount && ledger.Comp.Reservations.TryGetValue(reservation, out var parent) &&
            parent.Remaining >= amount && (parent.Expires == null || parent.Expires > Now()) &&
            (parent.Employee == null || _appointments.TryGetAssignment(parent.Employee.Value, out var employee) &&
                employee.Id == parent.Assignment);
    }

    /// <summary>Frees only this work claim. The parent's unspent balance remains reserved by the same C order.</summary>
    public bool TryReleaseAllocation(Guid generation, Guid operation)
    {
        if (!TryGetLedger(out var ledger) || ledger.Comp.Generation != generation ||
            !ledger.Comp.Allocations.TryGetValue(operation, out var allocation) ||
            allocation.Status == SWGlobalWarMoneyAllocationStatus.Committed)
            return false;
        ledger.Comp.Allocations[operation] = allocation with { Status = SWGlobalWarMoneyAllocationStatus.Released };
        return true;
    }

    private static void ReleaseAllocations(SWGlobalWarLedgerComponent ledger, Guid reservation)
    {
        foreach (var allocation in ledger.Allocations.Values.Where(a => a.Reservation == reservation &&
                     a.Status == SWGlobalWarMoneyAllocationStatus.Active).ToArray())
            ledger.Allocations[allocation.Operation] = allocation with { Status = SWGlobalWarMoneyAllocationStatus.Released };
    }

    private static bool CanCommitAllocation(SWGlobalWarLedgerComponent ledger, SWGlobalWarLedgerCommand command,
        SWGlobalWarMoneyReservation reservation)
    {
        if (command.Allocation == Guid.Empty)
            return command.Amount <= reservation.Remaining - Allocated(ledger, reservation.Id);
        return ledger.Allocations.TryGetValue(command.Allocation, out var allocation) &&
            allocation.Status == SWGlobalWarMoneyAllocationStatus.Active && allocation.Reservation == reservation.Id &&
            allocation.Amount == command.Amount;
    }
}
