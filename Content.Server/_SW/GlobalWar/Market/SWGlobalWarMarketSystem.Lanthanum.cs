using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private readonly Dictionary<string, LanthanumFunding> _lanthanumFunding = new();
    private readonly Dictionary<(NetUserId, Guid), (Guid Assignment, SWGlobalWarLanthanumCommand Command,
        SWGlobalWarLanthanumResult Result)> _lanthanumRequests = new();
    private readonly List<SWGlobalWarLanthanumJournalEntry> _lanthanumJournal = new();
    public IReadOnlyList<SWGlobalWarLanthanumJournalEntry> LanthanumJournal => _lanthanumJournal;

    private void ResetLanthanum()
    {
        ReconcileLanthanumOrders(stop: true);
        _lanthanumRequests.Clear();
        _lanthanumJournal.Clear();
    }

    private void ReconcileLanthanumOrders(bool stop = false)
    {
        var wasSettling = _settling;
        _settling = true;
        try
        {
            using var notifications = _ledger.DeferNotifications();
            foreach (var (planet, order) in _lanthanumFunding.ToArray())
            {
                if (!stop && Simulation is { } model && model.Generation == order.Generation &&
                    model.Markets[planet].Owner == order.Faction && model.LanthanumDeposits[planet].Remaining > 0 &&
                    model.LanthanumDoctrines[order.Faction].Phase == SWGlobalWarLanthanumPhase.Releasing &&
                    model.Time < order.Expires && _ledger.TryGetReservation(order.Reservation, out _))
                    continue;
                _lanthanumFunding.Remove(planet);
                if (_ledger.TryGetReservation(order.Reservation, out _))
                    _ledger.TryExecuteSystem(new(order.Generation, Guid.NewGuid(), 0, SWGlobalWarLedgerAction.Release,
                        order.Faction, SWGlobalWarAccount.Treasury, Reservation: order.Reservation,
                        Purpose: "lanthanum-minute-order-closed"), out _);
            }
        }
        finally
        {
            _settling = wasSettling;
        }
    }

    private bool SettleLanthanum(SWGlobalWarLanthanumRelease plan, Action completeGoods)
    {
        if (!CanMutateCampaign(plan.Generation, _ownershipCatchUp) || !_ledger.TryGetLedger(out var ledger) ||
            Simulation is not { } model || model.Markets[plan.Planet].Owner != plan.Faction)
            return false;
        _settling = true;
        try
        {
            using var notifications = _ledger.DeferNotifications();
            if (!_lanthanumFunding.TryGetValue(plan.Planet, out var funding))
            {
                var good = model.Markets[plan.Planet].Goods["SWGlobalWarLn"];
                var space = ((long) good.Capacity - good.Stock - good.Incoming - good.ReservedIncoming) / model.UnitsPerItem;
                var units = Math.Min(Math.Min(12L, model.LanthanumDeposits[plan.Planet].Remaining),
                    Math.Min(space, _ledger.GetBalance(plan.Faction, SWGlobalWarAccount.Treasury).Available / 400));
                if (units <= 0 || !_ledger.TryExecuteSystem(new(plan.Generation, Guid.NewGuid(), ledger.Comp.Revision,
                        SWGlobalWarLedgerAction.Reserve, plan.Faction, SWGlobalWarAccount.Treasury, units * 400,
                        Purpose: "lanthanum-minute-order:" + plan.Planet), out var reservation))
                    return false;
                funding = new(plan.Generation, plan.Faction, reservation, plan.At + TimeSpan.FromMinutes(1));
                _lanthanumFunding.Add(plan.Planet, funding);
            }
            if (!_ledger.TryExecuteSystem(new(plan.Generation, plan.Operation, ledger.Comp.Revision,
                    SWGlobalWarLedgerAction.Commit, plan.Faction, SWGlobalWarAccount.Treasury, 400,
                    Reservation: funding.Reservation, Purpose: "lanthanum-release:" + plan.Planet), out _))
                return false;
            // The prevalidated goods mutation cannot fail and precedes every deferred monetary notification.
            completeGoods();
            _lanthanumJournal.Add(new(plan.Generation, plan.Operation, null, Guid.Empty, plan.Faction,
                plan.Planet, 1, 400, null, plan.At));
            if (!_ledger.TryGetReservation(funding.Reservation, out _))
                _lanthanumFunding.Remove(plan.Planet);
            return true;
        }
        finally
        {
            _settling = false;
        }
    }

    private bool CanFundLanthanum(string planet, SWGlobalWarFaction faction)
    {
        return _lanthanumFunding.TryGetValue(planet, out var funding) && funding.Faction == faction &&
            _ledger.TryGetReservation(funding.Reservation, out var order) && order.Remaining >= 400 ||
            _ledger.GetBalance(faction, SWGlobalWarAccount.Treasury).Available >= 400;
    }

    /// <summary>Only the current head may change the persistent doctrine; taking office does not reset it.</summary>
    public bool TryConfigureLanthanum(ICommonSession session, SWGlobalWarLanthanumCommand command,
        out SWGlobalWarLanthanumResult result)
    {
        result = new(command?.Request ?? Guid.Empty, false, false, -1);
        if (command == null || command.Request == Guid.Empty)
            return false;
        var reason = string.Empty;
        SWGlobalWarAssignment actor = default!;
        if (!CanMutateCampaign(command.Generation))
            reason = "sw-global-war-economy-failure-campaigninactive";
        else if (!_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out actor))
            reason = "sw-global-war-economy-failure-assignmentchanged";
        else if (actor.Role != SWGlobalWarRole.Head)
            reason = "sw-global-war-ln-denied-head";
        else if (Simulation!.LanthanumDeposits.Count == 0)
            reason = "sw-global-war-ln-denied-no-deposit";
        if (reason.Length != 0)
        {
            result = result with { Reason = reason };
            return false;
        }
        var key = (actor.User, command.Request);
        if (_lanthanumRequests.TryGetValue(key, out var previous))
        {
            if (previous.Assignment != actor.Id || previous.Command != command)
                return false;
            result = previous.Result;
            return result.Success;
        }
        Update(0);
        if (!CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out actor) ||
            actor.Role != SWGlobalWarRole.Head)
            return false;
        var doctrine = Simulation!.LanthanumDoctrines[actor.Faction];
        var conflict = doctrine.Revision != command.ExpectedRevision;
        var success = !conflict && Simulation.SetLanthanumDoctrine(actor.Faction, command.ExpectedRevision, command.Automatic);
        result = new(command.Request, success, conflict, doctrine.Revision);
        _lanthanumRequests.Add(key, (actor.Id, command, result));
        if (!success)
            return false;
        _lanthanumJournal.Add(new(command.Generation, command.Request, actor.User, actor.Id, actor.Faction,
            null, 0, 0, command.Automatic, Simulation.Time));
        ReconcileLanthanumOrders();
        return true;
    }

    internal SWGlobalWarLanthanumView? GetLanthanumView(SWGlobalWarAssignment actor)
    {
        if (!HasEconomicOverview(actor) || Simulation is not { } model || model.LanthanumDeposits.Count == 0)
            return null;
        var doctrine = model.LanthanumDoctrines[actor.Faction];
        return new(doctrine.Revision, doctrine.Automatic, doctrine.Phase,
            doctrine.StartedAt + TimeSpan.FromSeconds(120), model.HomeLanthanumStock(actor.Faction), doctrine.Demand, doctrine.CivilElectronics,
            SWGlobalWarMarketSimulation.EmergencyT2Rate(doctrine.CivilElectronics),
            model.LanthanumDeposits.Values.Where(d => model.Markets[d.Planet].Owner == actor.Faction)
                .Select(d => new SWGlobalWarLanthanumDepositView(d.Planet, d.Remaining, d.Released, d.Phase)).ToList(),
            doctrine.WaitingReason, model.HomeLanthanumIncoming(actor.Faction));
    }

    internal List<SWGlobalWarLanthanumResult> GetLanthanumResults(SWGlobalWarAssignment actor)
    {
        return actor.Role != SWGlobalWarRole.Head ? [] : _lanthanumRequests.Where(p => p.Key.Item1 == actor.User &&
            p.Value.Assignment == actor.Id).Select(p => p.Value.Result).TakeLast(8).ToList();
    }

    private sealed record LanthanumFunding(Guid Generation, SWGlobalWarFaction Faction, Guid Reservation, TimeSpan Expires);
}

public sealed record SWGlobalWarLanthanumJournalEntry(Guid Generation, Guid Operation, NetUserId? Author,
    Guid Assignment, SWGlobalWarFaction Faction, string? Planet, int Units, long Cost, bool? Automatic, TimeSpan At);
