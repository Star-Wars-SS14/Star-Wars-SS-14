using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Empire;

public sealed partial class SWEmpireMilitarySystem
{
    // A viewer sees the opposing participants only once they have actually joined
    // the engagement. Travelling reinforcements are private to their owner.
    public List<SWStrategicBattleView> BuildBattles(bool imperialViewer, SWDefenseMapState defense)
    {
        var battles = new List<SWStrategicBattleView>();
        foreach (var ev in defense.Events.Where(e => !e.Resolved))
        {
            if (!ev.ImperialCrossing && !ev.ImperialRaid)
            {
                if (imperialViewer) continue;
                var engaged = defense.Fleets.Where(f => f.EventId == ev.Id && f.Order == SWDefenseFleetOrder.Respond
                    && f.Phase == SWDefenseOperationPhase.Execution).ToArray();
                if (engaged.Length == 0) continue;
                var normal = new SWStrategicBattleView { EventId = ev.Id, WorldId = ev.WorldId, WorldName = defense.Worlds.FirstOrDefault(w => w.Id == ev.WorldId)?.Name ?? ev.WorldId, Title = ev.Title, Phase = "Бой" };
                normal.Republic.AddRange(engaged.Select(RepublicFleet));
                normal.Opponent.Add(new SWStrategicBattleFleetView { Name = "Силы противника", Strength = ev.EnemyStrength });
                AddRepublicReinforcements(normal, defense);
                battles.Add(normal);
                continue;
            }
            var owner = _fleets.Values.FirstOrDefault(f => f.Alive && f.EventId == ev.Id);
            var raiders = _fleets.Values.Where(f => f.Alive && f.RaidEventId == ev.Id && !f.RaidUsesBorder
                && f.Next == null && f.World == ev.WorldId && (f.Contact && f.Path.Count == 0
                    || !f.Contact && (f.Order == SWEmpireMilitaryAction.Raid || f.RaidPending != null))).ToArray();
            Fleet[] participants;
            if (owner != null)
            {
                var crossing = _links.FirstOrDefault(l => l.ID == owner.Crossing);
                var foreign = crossing == null ? null : Republic(crossing.From) ? crossing.From : crossing.To;
                var own = crossing == null ? null : foreign == crossing.From ? crossing.To : crossing.From;
                participants = _fleets.Values.Where(f => f.Alive && f.Next == null &&
                    (f.World == foreign && (f.Id == owner.Id || f.EscortLeader == owner.Id || f.Permit == owner.Crossing)
                    || f.World == own && (f.Id == owner.Id || f.EscortLeader == owner.Id))).ToArray();
            }
            else
            {
                var ids = raiders.Select(f => f.Id).ToHashSet();
                participants = _fleets.Values.Where(f => f.Alive && f.Next == null && f.World == ev.WorldId
                    && (ids.Contains(f.Id) || ids.Contains(f.EscortLeader))).ToArray();
            }
            if (owner == null && raiders.Length == 0) continue;
            var defenders = _defense.GetImperialDefenders(ev.Id);
            var ready = ev.ImperialRaid ? _defense.ImperialRaidReady(ev.Id) : _defense.ImperialCrossingReady(ev.Id);
            var blocked = owner != null ? HostilitiesBlocked(owner) : raiders.All(HostilitiesBlocked);
            var fighting = _defense.ImperialOperationsAllowed && !blocked && ready && defenders.Count > 0 && participants.Length > 0;
            var battle = new SWStrategicBattleView { EventId = ev.Id, WorldId = ev.WorldId, WorldName = defense.Worlds.FirstOrDefault(w => w.Id == ev.WorldId)?.Name ?? ev.WorldId, Title = ev.ImperialBlockade ? "Имперская блокада" : ev.Title,
                Phase = !_defense.ImperialOperationsAllowed ? "Приостановлено" : blocked ? "Приостановлено перемирием" : fighting ? "Бой" : ev.Status };
            if (fighting)
                battle.Republic.AddRange(defenders.Select(d => new SWStrategicBattleFleetView { Id = d.Id, Name = $"{d.Id}-й флот", Strength = d.Strength, Damage = d.Damage, Supply = d.Supply, HasCondition = true }));
            if (fighting)
                battle.Opponent.AddRange(participants.Select(ImperialFleet));
            if (!imperialViewer) AddRepublicReinforcements(battle, defense);
            else
            {
                var leaders = participants.Select(p => p.Id).ToHashSet();
                foreach (var escort in _fleets.Values.Where(f => f.Alive && leaders.Contains(f.EscortLeader)
                             && !participants.Contains(f) && (f.Next != null || f.Path.Count > 0)))
                {
                    var view = ImperialFleet(escort);
                    var at = escort.Next ?? escort.World;
                    var eta = escort.Next != null ? escort.Until : _time.ActiveGameHours;
                    foreach (var next in escort.Path)
                    {
                        var edge = Link(at, next);
                        if (edge != null) eta += Math.Clamp(edge.TravelHours * .4, .1, .3);
                        at = next;
                    }
                    view.EtaHours = eta; battle.Reinforcements.Add(view);
                }
            }
            battles.Add(battle);
        }
        return battles;
    }

    private static SWStrategicBattleFleetView RepublicFleet(SWDefenseFleetView f) => new()
        { Id = f.Id, Name = f.Name, Strength = f.Strength, Damage = f.Damage, Supply = f.Supply, HasCondition = true };
    private static SWStrategicBattleFleetView ImperialFleet(Fleet f) => new()
        { Id = f.Id, Name = f.Name, Strength = (float) f.Strength, Damage = (float) f.Damage, Supply = (float) f.Supply, HasCondition = true };
    private static void AddRepublicReinforcements(SWStrategicBattleView battle, SWDefenseMapState defense)
    {
        foreach (var fleet in defense.Fleets.Where(f => f.EventId == battle.EventId && f.Order == SWDefenseFleetOrder.Respond
                     && f.Phase is SWDefenseOperationPhase.Preparation or SWDefenseOperationPhase.Travel))
        {
            var view = RepublicFleet(fleet); view.EtaHours = fleet.DestinationEtaHours; battle.Reinforcements.Add(view);
        }
    }
}
