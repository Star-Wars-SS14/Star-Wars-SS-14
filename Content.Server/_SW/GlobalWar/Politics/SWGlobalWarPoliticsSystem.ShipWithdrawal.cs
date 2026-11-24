using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Mind.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    private void OnShipOwnershipChanged(ref SWGlobalWarShipOwnershipChangedEvent args)
    {
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != args.Generation ||
            !_campaign.TryGetCampaign(out var campaign))
            return;
        if (politics.Comp.ShipWithdrawals.Remove(args.Ship))
            politics.Comp.Revision++;
        if (politics.Comp.Relations != SWGlobalWarRelations.War)
            BeginShipWithdrawals(politics, null, campaign.Comp.WithdrawalDuration);
    }

    private void BeginShipWithdrawals(Entity<SWGlobalWarPoliticsComponent> politics, EntityUid? sector, TimeSpan duration)
    {
        var query = AllEntityQuery<SWGlobalWarShipOwnershipComponent>();
        while (query.MoveNext(out var ship, out _))
        {
            if (!_shipOwnership.TryGetOwner(ship, politics.Comp.Generation, out var faction, out var revision) ||
                politics.Comp.ShipWithdrawals.ContainsKey(ship))
                continue;
            EntityUid planet;
            SWGlobalWarPlanetLocation location;
            if (TryComp<SWGlobalWarTravelComponent>(ship, out var flight) && flight.Generation == politics.Comp.Generation)
            {
                location = SWGlobalWarPlanetLocation.Orbit;
                planet = !ShipMayRemain(politics.Comp, faction, flight.DestinationPlanet, location)
                    ? flight.DestinationPlanet : flight.SourcePlanet;
            }
            else if (!_planets.TryGetPhysicalLocation(ship, out planet, out location))
                continue;
            if (sector != null && sector != planet || ShipMayRemain(politics.Comp, faction, planet, location))
                continue;
            politics.Comp.ShipWithdrawals.Add(ship, new SWGlobalWarShipWithdrawalOrder(Guid.NewGuid(),
                politics.Comp.Generation, ship, faction, revision, planet, Now() + duration,
                Reason: sector == null ? SWGlobalWarWithdrawalReason.Truce : SWGlobalWarWithdrawalReason.Integration));
            politics.Comp.Revision++;
        }
    }

    private bool ShipMayRemain(SWGlobalWarPoliticsComponent state, SWGlobalWarFaction faction,
        EntityUid planet, SWGlobalWarPlanetLocation location)
    {
        if (!TryComp<SWGlobalWarPlanetComponent>(planet, out var target))
            return false;
        if (state.Relations == SWGlobalWarRelations.War)
            return true;
        if (state.Neutrals.TryGetValue(target.Prototype, out var neutral) && neutral.Phase != SWGlobalWarNeutralPhase.Integrated)
            return CombatOpen(state, target);
        return (location == SWGlobalWarPlanetLocation.Orbit ? target.OrbitOwner : target.SurfaceOwner) == faction;
    }

    private void AdvanceShipWithdrawals(Entity<SWGlobalWarPoliticsComponent> politics, TimeSpan now)
    {
        foreach (var (ship, order) in politics.Comp.ShipWithdrawals.ToArray())
        {
            if (!_shipOwnership.TryGetOwner(ship, order.Generation, out var owner, out var revision) ||
                owner != order.Faction || revision != order.OwnershipRevision)
            {
                politics.Comp.ShipWithdrawals.Remove(ship);
                politics.Comp.Revision++;
                continue;
            }
            if (_planets.TryGetPhysicalLocation(ship, out var planet, out var location) &&
                ShipMayRemain(politics.Comp, owner, planet, location) && !HasComp<SWGlobalWarTravelComponent>(ship))
            {
                if (order.Status == SWGlobalWarWithdrawalStatus.Returning)
                {
                    foreach (var person in politics.Comp.Withdrawals.Values
                                 .Where(p => Transform(p.Body).GridUid == ship).ToArray())
                        CompleteWithdrawal(politics, person with
                        {
                            Destination = Comp<SWGlobalWarPlanetComponent>(planet).Prototype,
                            ShipReturned = true,
                        }, SWGlobalWarWithdrawalStatus.Returned);
                }
                politics.Comp.ShipWithdrawals.Remove(ship);
                politics.Comp.Revision++;
                continue;
            }
            if (now < order.Deadline || now < order.NextAttempt)
                continue;
            SeparateLawfulShipVisitors(politics, order);
            // Door/access callbacks during preparation may cancel the whole withdrawal or transfer the ship.
            // Never resurrect a removed order from this iteration's snapshot.
            if (politics.Comp.Relations == SWGlobalWarRelations.War ||
                !politics.Comp.ShipWithdrawals.TryGetValue(ship, out var stillCurrent) || stillCurrent.Id != order.Id)
                continue;
            var status = _travel.TryReturnWithdrawnShip(order, out var destination);
            if (status == SWGlobalWarWithdrawalStatus.Returned)
            {
                foreach (var person in politics.Comp.Withdrawals.Values
                             .Where(p => Transform(p.Body).GridUid == ship).ToArray())
                    CompleteWithdrawal(politics, person with { Destination = destination, ShipReturned = true }, status);
                politics.Comp.ShipWithdrawals.Remove(ship);
            }
            else
                politics.Comp.ShipWithdrawals[ship] = order with { NextAttempt = now + TimeSpan.FromSeconds(5), Status = status };
            if (status != order.Status)
                politics.Comp.Revision++;
        }
    }

    private void SeparateLawfulShipVisitors(Entity<SWGlobalWarPoliticsComponent> politics, SWGlobalWarShipWithdrawalOrder order)
    {
        if (!_planets.TryGetPhysicalLocation(order.Ship, out var planet, out var location))
            return;
        var visitors = new List<(EntityUid Body, EntityUid Mind, SWGlobalWarFaction Faction)>();
        var query = AllEntityQuery<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var body, out var container, out var transform))
        {
            if (!container.HasMind || transform.GridUid != order.Ship ||
                !_identity.TryGetOwnedBodyIdentity(body, out var mind, out var faction) || faction == order.Faction ||
                !IsLawfulPresence(politics.Comp, body, mind, faction, planet, location))
                continue;
            visitors.Add((body, mind, faction));
        }
        foreach (var (body, mind, faction) in visitors)
        {
            if (Comp<MindComponent>(mind).UserId is not { } user)
                continue;
            var localOrder = new SWGlobalWarWithdrawalOrder(Guid.NewGuid(), order.Generation, user, mind, body,
                faction, planet, order.Deadline, Reason: order.Reason);
            // This is an independent local group. Failure leaves its carrier and links intact; no foreign travel rights.
            if (TryReturnBody(localOrder, out _, planet, location, order) == SWGlobalWarWithdrawalStatus.Returned)
            {
                politics.Comp.WithdrawalHistory[user] = localOrder with { Status = SWGlobalWarWithdrawalStatus.MayRemain };
                politics.Comp.Revision++;
            }
        }
    }

    /// <summary>
    /// Exact server withdrawal permission used by Travel before detachment and FTL arrival. Unknown or foreign
    /// passengers retain their carrier; independently returnable people are processed by their own orders.
    /// </summary>
    public bool ValidateShipWithdrawal(SWGlobalWarShipWithdrawalOrder order, EntityUid destination)
    {
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != order.Generation ||
            politics.Comp.Relations == SWGlobalWarRelations.War ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            !politics.Comp.ShipWithdrawals.TryGetValue(order.Ship, out var current) || current.Id != order.Id ||
            Now() < current.Deadline || !_shipOwnership.TryGetOwner(order.Ship, order.Generation, out var owner, out var revision) ||
            owner != order.Faction || revision != order.OwnershipRevision ||
            !TryComp<SWGlobalWarPlanetComponent>(destination, out var target) || target.GlobalWarGamerule != politics.Owner ||
            target.OrbitOwner != owner || target.SurfaceOwner != owner)
            return false;
        var passengers = AllEntityQuery<MindContainerComponent, TransformComponent>();
        while (passengers.MoveNext(out var body, out var mind, out var transform))
        {
            if (!mind.HasMind || transform.GridUid != order.Ship || HasComp<GhostComponent>(body))
                continue;
            if (!_identity.TryGetOwnedBodyIdentity(body, out _, out var faction) || faction != owner)
                return false;
        }
        return true;
    }
}
