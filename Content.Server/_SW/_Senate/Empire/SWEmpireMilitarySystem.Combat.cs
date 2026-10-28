using System;
using System.Linq;
using System.Collections.Generic;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Empire;

namespace Content.Server._SW._Senate.Empire;

public sealed partial class SWEmpireMilitarySystem
{
    private bool Republic(string world) => _worlds.TryGetValue(world, out var w) && w.Faction == "republic";
    // Geography is public; this function never exposes fleet or economic state.
    private static string PublicWorld(string world) => world;

    private SWTransitLinkPrototype? BorderAt(string world, string target) => _links.FirstOrDefault(l =>
        (l.From == world && Republic(l.To) && PublicWorld(l.To) == target)
        || (l.To == world && Republic(l.From) && PublicWorld(l.From) == target));
    private bool CrossingOpen(string? id) => id != null && _fleets.Values.Any(f => f.Alive && f.EventId != 0
        && f.Permit == id && _defense.HasImperialEngagement(f.EventId));

    private List<string>? RepublicPath(string from, string sector, bool exact = false)
    {
        if (!Republic(from)) return null;
        var distance = new Dictionary<string, double> { [from] = 0 };
        var previous = new Dictionary<string, string>();
        var remaining = _worlds.Keys.Where(Republic).ToHashSet();
        while (remaining.Count > 0)
        {
            var current = remaining.Where(distance.ContainsKey).OrderBy(x => distance[x]).FirstOrDefault();
            if (current == null) break;
            remaining.Remove(current);
            if ((exact && current == sector) || (!exact && current != from && PublicWorld(current) == sector))
            {
                var path = new List<string>();
                while (current != from) { path.Add(current); current = previous[current]; }
                path.Reverse(); return path;
            }
            foreach (var edge in _links.Where(l => l.Accessible && (l.From == current || l.To == current)))
            {
                var next = edge.From == current ? edge.To : edge.From;
                if (!remaining.Contains(next)) continue;
                var cost = distance[current] + Math.Clamp(edge.TravelHours * .4, .1, .3);
                if (distance.TryGetValue(next, out var old) && old <= cost) continue;
                distance[next] = cost; previous[next] = current;
            }
        }
        return null;
    }

    private void ObserveBorder(Fleet fleet)
    {
        if (fleet.Order != SWEmpireMilitaryAction.Recon || fleet.Next != null || !Imperial(fleet.World)) return;
        foreach (var edge in _links.Where(l => l.From == fleet.World && Republic(l.To) || l.To == fleet.World && Republic(l.From)))
        {
            var sector = PublicWorld(edge.From == fleet.World ? edge.To : edge.From);
            var forces = _defense.GetObservedBorderFleets(edge.ID);
            _observations[sector] = new SWEmpireMilitaryObservationView { SectorId = sector, SeenHours = _time.ActiveGameHours,
                Summary = forces.Count == 0 ? "На границе соединения не обнаружены" : $"На границе обнаружено соединений: {forces.Count}", Current = true };
        }
    }

    private void TickEscort(Fleet escort)
    {
        if (escort.EscortLeader == 0 || escort.Next != null) return;
        if (!_fleets.TryGetValue(escort.EscortLeader, out var leader) || !leader.Alive
            || leader.Order == SWEmpireMilitaryAction.Return || escort.Supply < 10
            || Republic(escort.World) && !CrossingOpen(escort.Crossing))
        {
            escort.EscortLeader = 0;
            if (Republic(escort.World)) QueueRetreat(escort);
            else { escort.Path.Clear(); escort.Order = SWEmpireMilitaryAction.Guard; }
            return;
        }
        if (escort.World == leader.World && leader.Next != null)
        {
            var edge = Link(escort.World, leader.Next);
            if (!_defense.MinisterActive && (Republic(escort.World) || Republic(leader.Next))) return;
            var crossing = edge != null && edge.ID == leader.Crossing && leader.Permit == edge.ID && CrossingOpen(edge.ID);
            if (edge == null || !(Imperial(escort.World) && Imperial(leader.Next)
                || edge.Accessible && Republic(escort.World) && Republic(leader.Next) && leader.Permit != null
                || crossing)) return;
            escort.Crossing = leader.Crossing; escort.Permit = leader.Permit;
            escort.Next = leader.Next; escort.LegStartedAt = _time.ActiveGameHours;
            escort.Until = escort.LegStartedAt + Math.Clamp(edge.TravelHours * .4, .1, .3);
            escort.Path.Clear(); return;
        }
        if (escort.World == leader.World) return;
        if (Imperial(escort.World) && Republic(leader.World) && leader.Crossing != null && CrossingOpen(leader.Crossing))
        {
            var crossing = _links.FirstOrDefault(l => l.ID == leader.Crossing);
            if (crossing == null) return;
            var own = Imperial(crossing.From) ? crossing.From : crossing.To;
            var foreign = own == crossing.From ? crossing.To : crossing.From;
            var approach = OwnPath(escort.World, own);
            var follow = RepublicPath(foreign, leader.World, true);
            if (approach == null || follow == null) return;
            escort.Crossing = crossing.ID; escort.Permit = crossing.ID;
            escort.Path.Clear(); foreach (var step in approach) escort.Path.Enqueue(step);
            escort.Path.Enqueue(foreign); foreach (var step in follow) escort.Path.Enqueue(step);
            return;
        }
        var path = Imperial(escort.World) && Imperial(leader.World) ? OwnPath(escort.World, leader.World)
            : Republic(escort.World) && Republic(leader.World) ? RepublicPath(escort.World, leader.World, true) : null;
        if (path == null) return;
        escort.Permit = leader.Permit; escort.Crossing = leader.Crossing;
        escort.Path.Clear(); foreach (var step in path) escort.Path.Enqueue(step);
    }

    private void TickBorderBattle(Fleet fleet, double hours)
    {
        if (fleet.EventId == 0) return;
        if (!_defense.HasImperialEngagement(fleet.EventId))
        { fleet.EventId = 0; fleet.Permit = null; return; }
        if (!_defense.MinisterActive || !_defense.ImperialCrossingReady(fleet.EventId)) return;
        if (fleet.Supply < 10)
        {
            _defense.FinishImperialCrossing(fleet.EventId, false);
            fleet.EventId = 0; fleet.Permit = null;
            QueueRetreat(fleet); return;
        }
        var defenders = _defense.GetImperialDefenders(fleet.EventId);
        var crossing = _links.FirstOrDefault(l => l.ID == fleet.Crossing);
        var foreignEntry = crossing == null ? null : Republic(crossing.From) ? crossing.From : crossing.To;
        var holding = _fleets.Values.Any(f => f.Alive && f.Next == null && f.World == foreignEntry
            && (f.Id == fleet.Id || f.EscortLeader == fleet.Id || f.Permit == fleet.Crossing));
        if (fleet.Permit != null && defenders.Count > 0 && Republic(fleet.World) && fleet.World != foreignEntry && !holding)
        {
            _defense.FinishImperialCrossing(fleet.EventId, false);
            fleet.EventId = 0; fleet.Permit = null;
            return;
        }
        if (defenders.Count == 0)
        {
            if (fleet.Permit == null && _defense.FinishImperialCrossing(fleet.EventId, true)) fleet.Permit = fleet.Crossing;
            return;
        }
        var battleWorld = Republic(fleet.World) && fleet.World != foreignEntry ? foreignEntry : fleet.World;
        var participants = _fleets.Values.Where(f => f.Alive && f.Next == null && f.World == battleWorld &&
            (f.Id == fleet.Id || f.EscortLeader == fleet.Id || f.Permit == fleet.Crossing)).ToArray();
        var effective = participants.Sum(f => f.Strength * (1 - f.Damage / 125) * (.25 + f.Supply / 133.333) * (.75 + f.Training / 200));
        var opposition = defenders.Sum(d => d.Effective);
        foreach (var defender in defenders)
        {
            var damage = (float)(effective * .22 * hours * defender.Effective / Math.Max(1, opposition));
            _defense.ApplyImperialBattleDamage(fleet.EventId, defender.Id, damage * .55f, damage, (float)(hours * 10));
        }
        var received = opposition * .22 * hours;
        foreach (var participant in participants)
        {
            var part = received / Math.Max(1, participants.Length);
            participant.Strength = Math.Max(0, participant.Strength - part * .55);
            participant.Damage = Math.Min(100, participant.Damage + part);
            participant.Supply = Math.Max(0, participant.Supply - hours * 10);
            if (!participant.Alive)
            {
                if (participant.DepositPending != null) _economy.Runner?.CancelExternalTransfer(participant.DepositPending);
                if (participant.RaidPending != null) _republicEconomy.Runner?.CancelExternalTransfer(participant.RaidPending);
                participant.CargoGood = null; participant.CargoAmount = 0; participant.Path.Clear(); participant.Next = null;
            }
            if (participant.Id != fleet.Id && (participant.Strength < 25 || participant.Damage > 75 || participant.Supply < 10))
            { participant.EscortLeader = 0; QueueRetreat(participant); }
        }
        if (fleet.Strength >= 25 && fleet.Damage <= 75 && fleet.Supply >= 10) return;
        _defense.FinishImperialCrossing(fleet.EventId, false);
        fleet.EventId = 0; fleet.Permit = null;
        if (!fleet.Alive)
        { fleet.Path.Clear(); fleet.Next = null; fleet.WorkProject = null; fleet.CargoGood = null; fleet.CargoAmount = 0; }
        QueueRetreat(fleet);
    }

    private void QueueRetreat(Fleet fleet)
    {
        if (!fleet.Alive || !Republic(fleet.World) || fleet.Crossing == null || fleet.Next != null) return;
        var edge = _links.FirstOrDefault(l => l.ID == fleet.Crossing);
        if (edge == null) return;
        var foreign = Republic(edge.From) ? edge.From : edge.To;
        var own = foreign == edge.From ? edge.To : edge.From;
        var path = RepublicPath(fleet.World, foreign, true);
        if (path == null) return;
        fleet.Path.Clear(); foreach (var step in path) fleet.Path.Enqueue(step);
        fleet.Path.Enqueue(own); fleet.Target = own; fleet.Order = SWEmpireMilitaryAction.Return;
    }
}
