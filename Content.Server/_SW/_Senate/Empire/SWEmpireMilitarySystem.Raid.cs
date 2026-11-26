using System;
using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Empire;

namespace Content.Server._SW._Senate.Empire;

public sealed partial class SWEmpireMilitarySystem
{
    private const double RaidCargoSlots = 8;
    // A narrow server-side catalogue avoids publishing enemy warehouses to the player.
    private static readonly string[] RaidGoods = { "military_supplies", "parts", "circuits", "ship_fuel" };

    private void EnsureStationaryContact(Fleet fleet)
    {
        if (!fleet.Alive || fleet.RaidEventId != 0 || fleet.Next != null || fleet.Path.Count != 0
            || fleet.Crossing == null || !Republic(fleet.World) || fleet.World == fleet.CivilianBase
            || fleet.Order == SWEmpireMilitaryAction.Raid && CrossingPassable(fleet.Crossing) || !_defense.ImperialOperationsAllowed) return;
        if (fleet.EscortLeader != 0 && _fleets.TryGetValue(fleet.EscortLeader, out var leader)
            && leader.Alive && leader.Order == SWEmpireMilitaryAction.Raid && leader.World == fleet.World && leader.Next == null
            && (leader.RaidEventId != 0 || CrossingPassable(leader.Crossing))) return;
        var crossing = _links.FirstOrDefault(l => l.ID == fleet.Crossing);
        var borderWorld = crossing == null ? null : Republic(crossing.From) ? crossing.From : crossing.To;
        if (borderWorld == fleet.World && _fleets.Values.Any(f => f.Alive && f.EventId != 0 && f.Crossing == fleet.Crossing
            && _defense.HasImperialEngagement(f.EventId))) return;
        if (!_raidTargets.TryGetValue(fleet.World, out var id) || !_defense.HasImperialEngagement(id))
        {
            if (!_defense.TryStartImperialRaid(fleet.Id.ToString(), fleet.World, out id, out _, true)) return;
            _raidTargets[fleet.World] = id;
        }
        fleet.RaidEventId = id; fleet.Contact = true; fleet.ContactWorld = fleet.World;
    }

    private bool RaidCombatVisible(Fleet fleet)
    {
        var leader = fleet.EscortLeader != 0 && _fleets.TryGetValue(fleet.EscortLeader, out var followed)
            && followed.World == fleet.World ? followed : fleet;
        var id = leader.RaidEventId;
        if (!_defense.ImperialOperationsAllowed) return false;
        if (id != 0 && !leader.RaidUsesBorder && _defense.ImperialRaidReady(id)
            && _defense.GetImperialDefenders(id).Count > 0) return true;
        return leader.EventId != 0 && _defense.ImperialCrossingReady(leader.EventId)
            && _defense.GetImperialDefenders(leader.EventId).Count > 0;
    }

    private bool RaidTargetSecured(Fleet fleet) => fleet.RaidEventId != 0
        && (fleet.RaidUsesBorder ? CrossingPassable(fleet.Crossing) && _defense.ImperialCrossingReady(fleet.RaidEventId)
            : _securedRaids.Contains(fleet.RaidEventId) && _defense.ImperialRaidReady(fleet.RaidEventId))
        && _defense.GetImperialDefenders(fleet.RaidEventId).Count == 0;

    private void LeaveRaidEngagement(Fleet fleet)
    {
        var id = fleet.RaidEventId;
        if (id == 0) return;
        var border = fleet.RaidUsesBorder;
        var contact = fleet.Contact;
        fleet.RaidEventId = 0; fleet.RaidUsesBorder = false; fleet.Contact = false; fleet.ContactWorld = null;
        if (border || _fleets.Values.Any(f => f.Alive && f.RaidEventId == id)) return;
        _defense.FinishImperialRaid(id, false, contact);
        _securedRaids.Remove(id);
        foreach (var world in _raidTargets.Where(p => p.Value == id).Select(p => p.Key).ToArray()) _raidTargets.Remove(world);
    }

    private void TickRaidBattle(Fleet fleet, double hours)
    {
        if (!fleet.Alive) { LeaveRaidEngagement(fleet); return; }
        if (fleet.RaidEventId != 0 && (fleet.Contact
                ? fleet.Next != null || fleet.Path.Count != 0 || fleet.World != fleet.ContactWorld
                : fleet.Next != null || fleet.Order != SWEmpireMilitaryAction.Raid && fleet.RaidPending == null
                    || fleet.Order == SWEmpireMilitaryAction.Raid && fleet.World != fleet.Target))
            LeaveRaidEngagement(fleet);
        if (fleet.RaidEventId == 0)
        {
            if (fleet.Order != SWEmpireMilitaryAction.Raid || fleet.Next != null || fleet.Path.Count != 0
                || fleet.World != fleet.Target || !Republic(fleet.World) || !_defense.ImperialOperationsAllowed || !CrossingPassable(fleet.Crossing)) return;
            var crossing = _links.FirstOrDefault(l => l.ID == fleet.Crossing);
            var borderWorld = crossing == null ? null : Republic(crossing.From) ? crossing.From : crossing.To;
            if (fleet.World == borderWorld)
            {
                var owner = _fleets.Values.FirstOrDefault(f => f.Alive && f.EventId != 0 && f.Permit == fleet.Crossing);
                if (owner == null) return;
                fleet.RaidEventId = owner.EventId; fleet.RaidUsesBorder = true;
            }
            else if (_raidTargets.TryGetValue(fleet.World, out var existing) && _defense.HasImperialEngagement(existing))
            { fleet.RaidEventId = existing; _defense.PromoteImperialRaid(existing); }
            else if (_defense.TryStartImperialRaid(fleet.Id.ToString(), fleet.World, out var id, out _))
            { fleet.RaidEventId = id; _raidTargets[fleet.World] = id; }
            return;
        }
        if (!_defense.HasImperialEngagement(fleet.RaidEventId))
        { LeaveRaidEngagement(fleet); return; }
        // The crossing battle already handles the border endpoint. Never tick it twice.
        if (fleet.RaidUsesBorder) return;
        if (!_defense.ImperialOperationsAllowed || !_defense.ImperialRaidReady(fleet.RaidEventId)) return;
        var active = _fleets.Values.Where(f => f.Alive && f.Next == null && f.World == fleet.World &&
            (f.Contact && f.Path.Count == 0 || !f.Contact && (f.Order == SWEmpireMilitaryAction.Raid || f.RaidPending != null))
            && f.RaidEventId == fleet.RaidEventId).OrderBy(f => f.Id).ToArray();
        if (active.Length == 0 || active[0].Id != fleet.Id || !_raidTickedThisUpdate.Add(fleet.RaidEventId)) return;
        var defenders = _defense.GetImperialDefenders(fleet.RaidEventId);
        if (defenders.Count == 0)
        {
            if (!_securedRaids.Contains(fleet.RaidEventId) && _defense.FinishImperialRaid(fleet.RaidEventId, true))
                _securedRaids.Add(fleet.RaidEventId);
            return;
        }
        _securedRaids.Remove(fleet.RaidEventId);
        _defense.MarkImperialRaidBattle(fleet.RaidEventId);
        foreach (var raider in active)
            if (raider.RaidPending != null) _republicEconomy.Runner?.CancelExternalTransfer(raider.RaidPending);
        var ids = active.Select(f => f.Id).ToHashSet();
        var participants = _fleets.Values.Where(f => f.Alive && f.Next == null && f.World == fleet.World
            && (ids.Contains(f.Id) || ids.Contains(f.EscortLeader))).ToArray();
        var effective = participants.Sum(f => f.Strength * (1 - f.Damage / 125) * (.25 + f.Supply / 133.333) * (.75 + f.Training / 200));
        var opposition = defenders.Sum(d => d.Effective);
        foreach (var defender in defenders)
        {
            var damage = (float)(effective * .22 * hours * defender.Effective / Math.Max(1, opposition));
            _defense.ApplyImperialBattleDamage(fleet.RaidEventId, defender.Id, damage * .55f, damage, (float)(hours * 10));
        }
        foreach (var participant in participants)
        {
            var damage = opposition * .22 * hours / Math.Max(1, participants.Length);
            participant.Strength = Math.Max(0, participant.Strength - damage * .55);
            participant.Damage = Math.Min(100, participant.Damage + damage);
            participant.Supply = Math.Max(0, participant.Supply - hours * 10);
            if (!participant.Alive)
            {
                if (participant.DepositPending != null) _economy.Runner?.CancelExternalTransfer(participant.DepositPending);
                if (participant.RaidPending != null) _republicEconomy.Runner?.CancelExternalTransfer(participant.RaidPending);
                participant.CargoGood = null; participant.CargoAmount = 0; participant.Path.Clear(); participant.Next = null;
            }
            if (participant.Strength >= 25 && participant.Damage <= 75 && participant.Supply >= 10) continue;
            participant.EscortLeader = 0;
            ReleaseCrossing(participant);
            LeaveRaidEngagement(participant);
            QueueRetreat(participant);
        }
    }

    private void TickRaidCargo(Fleet fleet)
    {
        var source = _republicEconomy.Runner;
        var destination = _economy.Runner;
        if (fleet.RaidPending != null && !CrossingPassable(fleet.Crossing)) source?.CancelExternalTransfer(fleet.RaidPending);
        if (fleet.RaidPending != null && source?.GetExternalTransferResult(fleet.RaidPending) is { } taken)
        {
            // A formation which left before the worker acknowledged the loading cannot receive it remotely.
            if (fleet.Next == null && fleet.World == fleet.RaidWorld && fleet.Alive && taken.Amount > 1e-9)
            { fleet.CargoGood = fleet.RaidGood; fleet.CargoAmount = taken.Amount; }
            fleet.RaidPending = null; fleet.RaidGood = null; fleet.RaidWorld = null;
        }
        if (fleet.DepositPending != null && destination?.GetExternalTransferResult(fleet.DepositPending) is { } unloaded)
        {
            fleet.CargoAmount = Math.Max(0, fleet.CargoAmount - unloaded.Amount);
            if (fleet.CargoAmount <= 1e-9) { fleet.CargoAmount = 0; fleet.CargoGood = null; }
            fleet.DepositPending = null;
        }
        if (fleet.CargoAmount > 1e-9 && fleet.CargoGood != null && fleet.DepositPending == null && destination != null
            && fleet.Next == null && Own(fleet.World))
        {
            var stock = destination.Snapshot?.Inventory.FirstOrDefault(i => i.World == fleet.World && i.Good == fleet.CargoGood);
            if (stock == null || stock.Capacity - stock.Stock - stock.Reserved <= 1e-9) return;
            var id = $"imperial-raid-unload-{_nextProject++}";
            if (destination.TryQueueExternalTransfer(new EconomyExternalTransfer(id, fleet.World, fleet.CargoGood, fleet.CargoAmount, true)))
                fleet.DepositPending = id;
            return;
        }
        if (HostilitiesBlocked(fleet) || fleet.Order != SWEmpireMilitaryAction.Raid || fleet.RaidAttempted || fleet.RaidPending != null
            || fleet.CargoAmount > 1e-9 || fleet.Next != null || fleet.Path.Count != 0 || fleet.World != fleet.Target
            || !Republic(fleet.World) || !_defense.ImperialOperationsAllowed || !CrossingPassable(fleet.Crossing) || source?.Snapshot == null)
        { fleet.RaidReadyAt = 0; return; }
        // A crossing victory alone does not establish control of the actual raid target.
        // Target engagement must authorize loading before any withdrawal can be queued.
        if (!RaidTargetSecured(fleet)) { fleet.RaidReadyAt = 0; return; }
        if (fleet.RaidReadyAt <= 0) { fleet.RaidReadyAt = _time.ActiveGameHours + .25; return; }
        if (_time.ActiveGameHours < fleet.RaidReadyAt) return;
        fleet.RaidAttempted = true;
        foreach (var good in RaidGoods)
        {
            var definition = source.Definition.Goods.FirstOrDefault(g => g.Id == good);
            if (definition == null || definition.CargoSlots <= 0 || !_economy.Definition!.Inventory.Any(i => i.World == fleet.Home && i.Good == good)) continue;
            var stock = source.Snapshot.Inventory.FirstOrDefault(i => i.World == fleet.World && i.Good == good)?.Stock ?? 0;
            var amount = Math.Min(stock, RaidCargoSlots / definition.CargoSlots);
            if (amount <= 1e-9) continue;
            var id = $"imperial-raid-take-{_nextProject++}";
            if (!source.TryQueueExternalTransfer(new EconomyExternalTransfer(id, fleet.World, good, amount, false))) continue;
            fleet.RaidPending = id; fleet.RaidGood = good; fleet.RaidWorld = fleet.World;
            break;
        }
    }
}
