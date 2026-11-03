using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared._SW.Vehicle;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    [Dependency] private readonly SWGlobalWarSpawnSystem _spawn = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;
    [Dependency] private readonly SWVehicleSystem _vehicles = default!;
    [Dependency] private readonly SWGlobalWarShipOwnershipSystem _shipOwnership = default!;
    [Dependency] private readonly SWGlobalWarTravelSystem _travel = default!;

    /// <summary>Own addressed status, including the last confirmed result of this generation after reconnect.</summary>
    public SWGlobalWarWithdrawalOrder? GetWithdrawal(ICommonSession session)
    {
        if (!TryGetPolitics(out var politics))
            return null;
        return politics.Comp.WithdrawalHistory.GetValueOrDefault(session.UserId);
    }

    /// <summary>Rechecks current identity before exposing the last result; historic bodies grant no visibility.</summary>
    public SWGlobalWarWithdrawalView? GetWithdrawalView(ICommonSession session)
    {
        if (session.AttachedEntity is not { } body || !_identity.TryGetActor(session, body, out var mind, out _) ||
            GetWithdrawal(session) is not { } order || order.Body != body || order.Mind != mind ||
            !TryGetPolitics(out var politics) || order.Generation != politics.Comp.Generation)
            return null;
        var active = politics.Comp.Withdrawals.TryGetValue(body, out var current) && current.Id == order.Id;
        var status = order.Status;
        if (active && Transform(body).GridUid is { } ship &&
            politics.Comp.ShipWithdrawals.TryGetValue(ship, out var group) && group.Faction == order.Faction &&
            group.Status == SWGlobalWarWithdrawalStatus.Returning)
            status = group.Status;
        var sector = TryComp<SWGlobalWarPlanetComponent>(order.Sector, out var planet)
            ? _prototypes.Index(planet.Prototype).Name : string.Empty;
        return new(order.Id, order.Reason, status, TimeSpan.FromTicks(Math.Max(0, (order.Deadline - Now()).Ticks)),
            sector, order.Destination is { } destination ? _prototypes.Index(destination).Name : null,
            active, order.ShipReturned, order.WarResumed);
    }

    /// <summary>D uses this in addition to normal spending/stock checks; withdrawal grants no new market supplies.</summary>
    public bool CanAcquireMarketGoods(EntityUid actor, Guid generation)
    {
        return TryGetPolitics(out var politics) && politics.Comp.Generation == generation &&
               _campaign.TryGetCampaign(out var campaign) && campaign.Comp.Phase == SWGlobalWarCampaignPhase.Running &&
               !politics.Comp.Withdrawals.ContainsKey(actor) && _identity.TryGetIdentity(actor, out _, out _);
    }

    private void BeginWithdrawals(Entity<SWGlobalWarPoliticsComponent> politics, EntityUid? sector = null)
    {
        if (politics.Comp.Relations == SWGlobalWarRelations.War || !_campaign.TryGetCampaign(out var campaign))
            return;
        BeginShipWithdrawals(politics, sector, campaign.Comp.WithdrawalDuration);
        // Enumerate physical owned bodies, not sessions; SSD and a temporarily visiting mind still have a body here.
        var query = AllEntityQuery<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var body, out _, out _))
        {
            if (!_identity.TryGetOwnedBodyIdentity(body, out var mind, out var faction) ||
                Comp<MindComponent>(mind).UserId is not { } user ||
                !TryGetWithdrawalLocation(politics.Comp, body, mind, faction, out var planet, out var location) ||
                sector != null && planet != sector || IsLawfulPresence(politics.Comp, body, mind, faction, planet, location) ||
                politics.Comp.Withdrawals.ContainsKey(body))
                continue;
            var order = new SWGlobalWarWithdrawalOrder(Guid.NewGuid(), politics.Comp.Generation, user, mind, body,
                faction, planet, Now() + campaign.Comp.WithdrawalDuration,
                Reason: sector == null ? SWGlobalWarWithdrawalReason.Truce : SWGlobalWarWithdrawalReason.Integration);
            politics.Comp.Withdrawals.Add(body, order);
            politics.Comp.WithdrawalHistory[user] = order;
            politics.Comp.Revision++;
        }
    }

    private bool IsLawfulPresence(SWGlobalWarPoliticsComponent state, EntityUid body, EntityUid mind,
        SWGlobalWarFaction faction, EntityUid planet, SWGlobalWarPlanetLocation location)
    {
        return IsLawfulPresence(state, body, mind, faction, planet, location, GetBodyRole(body, mind));
    }

    private bool IsLawfulPresence(SWGlobalWarPoliticsComponent state, EntityUid body, EntityUid mind,
        SWGlobalWarFaction faction, EntityUid planet, SWGlobalWarPlanetLocation location, SWGlobalWarRole? role)
    {
        if (!TryComp<SWGlobalWarPlanetComponent>(planet, out var target))
            return false;
        if (!IsForceCarrier(body, mind, state.Generation) && (role == SWGlobalWarRole.Intelligence || !IsMilitary(role)))
            return true;
        if (state.Neutrals.TryGetValue(target.Prototype, out var neutral) && neutral.Phase != SWGlobalWarNeutralPhase.Integrated)
            return CombatOpen(state, target);
        return (location == SWGlobalWarPlanetLocation.Surface ? target.SurfaceOwner : target.OrbitOwner) == faction ||
               state.Relations == SWGlobalWarRelations.War;
    }

    private void AdvanceWithdrawals(Entity<SWGlobalWarPoliticsComponent> politics, TimeSpan now)
    {
        AdvanceShipWithdrawals(politics, now);
        foreach (var (body, initial) in politics.Comp.Withdrawals.ToArray())
        {
            if (!_identity.TryGetOwnedBodyIdentity(body, out var mind, out var faction) || mind != initial.Mind ||
                faction != initial.Faction)
            {
                CompleteWithdrawal(politics, initial, SWGlobalWarWithdrawalStatus.Cancelled);
                continue;
            }
            if (_planets.TryGetPhysicalLocation(body, out var planet, out var location) &&
                IsLawfulPresence(politics.Comp, body, mind, faction, planet, location))
            {
                CompleteWithdrawal(politics, initial, planet == initial.Sector
                    ? SWGlobalWarWithdrawalStatus.MayRemain : SWGlobalWarWithdrawalStatus.Departed);
                continue;
            }
            if (now < initial.Deadline || now < initial.NextAttempt)
                continue;
            if (Transform(body).GridUid is { } ship &&
                politics.Comp.ShipWithdrawals.TryGetValue(ship, out var group) &&
                TryComp<SWGlobalWarTravelComponent>(ship, out var flight) &&
                HasComp<Content.Shared.Shuttles.Components.FTLComponent>(ship) &&
                flight.Withdrawal?.Id == group.Id && flight.Generation == group.Generation &&
                ValidateShipWithdrawal(group, flight.DestinationPlanet))
                continue;
            var status = TryReturnBody(initial, out var destination);
            if (status == SWGlobalWarWithdrawalStatus.Returned)
            {
                CompleteWithdrawal(politics, initial with { Destination = destination }, status);
                continue;
            }
            var waiting = initial with { Status = status, NextAttempt = now + TimeSpan.FromSeconds(5) };
            politics.Comp.Withdrawals[body] = waiting;
            politics.Comp.WithdrawalHistory[initial.User] = waiting;
            if (status != initial.Status)
                politics.Comp.Revision++;
        }
    }

    private SWGlobalWarWithdrawalStatus TryReturnBody(SWGlobalWarWithdrawalOrder order,
        out Robust.Shared.Prototypes.ProtoId<SWGlobalWarPlanetPrototype>? destination,
        EntityUid? localPlanet = null, SWGlobalWarPlanetLocation localLocation = SWGlobalWarPlanetLocation.Surface,
        SWGlobalWarShipWithdrawalOrder? carrierOrder = null)
    {
        destination = null;
        var body = order.Body;
        if (TerminatingOrDeleted(body))
            return SWGlobalWarWithdrawalStatus.WaitingForSeparation;
        var parent = Transform(body).ParentUid;
        var originalBuckle = CompOrNull<BuckleComponent>(body)?.BuckledTo;
        _containers.TryGetContainingContainer(body, out var container);
        if (!_spawn.TryPrepareBodyReturn(body, order.Generation, out var plan, localPlanet, localLocation))
            return SWGlobalWarWithdrawalStatus.WaitingForPoint;
        try
        {
            if (!_spawn.ValidateBodyReturn(plan))
                return SWGlobalWarWithdrawalStatus.WaitingForPoint;
            // An independently controlled passenger hidden in personal contents never inherits this return permission.
            var minds = AllEntityQuery<MindContainerComponent>();
            while (minds.MoveNext(out var passenger, out var passengerMind))
            {
                if (passenger == body || !passengerMind.HasMind)
                    continue;
                var ancestor = Transform(passenger).ParentUid;
                while (ancestor.IsValid() && TryComp<TransformComponent>(ancestor, out var transform))
                {
                    if (ancestor == body)
                        return SWGlobalWarWithdrawalStatus.WaitingForSeparation;
                    ancestor = transform.ParentUid;
                }
            }
            if (Transform(body).ParentUid != parent || CompOrNull<BuckleComponent>(body)?.BuckledTo != originalBuckle ||
                container != null && !container.Contains(body) ||
                !ValidateWithdrawalCommit(order, localPlanet, localLocation, carrierOrder))
                return SWGlobalWarWithdrawalStatus.WaitingForSeparation;

            var incoming = CompOrNull<PullableComponent>(body)?.PullJointId;
            var cargo = CompOrNull<PullerComponent>(body)?.Pulling;
            var outgoing = CompOrNull<PullableComponent>(cargo)?.PullJointId;
            if (TryComp<JointComponent>(body, out var joints) &&
                joints.GetJoints.Keys.Any(id => id != incoming && id != outgoing))
                return SWGlobalWarWithdrawalStatus.WaitingForSeparation;
            // Vehicle exit owns role occupancy, movement relay, actions and virtual control items.
            // Calling only Unbuckle would leave the returned person remotely controlling the old vehicle.
            if (TryComp<SWInVehicleComponent>(body, out var inVehicle) && !_vehicles.TryDeoccupy((body, inVehicle)))
                return SWGlobalWarWithdrawalStatus.WaitingForSeparation;

            // All failures above preserve links. The server's prepared automatic return deliberately separates
            // body from a chair/container/puller, retaining its inventory, cuffs, damage and mind unchanged.
            if (incoming != null)
                _joints.RemoveJoint(body, incoming);
            if (cargo != null && outgoing != null)
                _joints.RemoveJoint(cargo.Value, outgoing);
            _buckle.Unbuckle((body, CompOrNull<BuckleComponent>(body)), null);
            if (container != null)
            {
                if (!_containers.Remove(body, container, force: true, destination: plan.Coordinates, localRotation: plan.Rotation))
                    return SWGlobalWarWithdrawalStatus.WaitingForSeparation;
            }
            else
                _transform.SetCoordinates(body, Transform(body), plan.Coordinates, rotation: plan.Rotation);
            if (TryComp<PhysicsComponent>(body, out var physics))
                _physics.SetLinearVelocity(body, Vector2.Zero, body: physics);
            destination = Comp<SWGlobalWarPlanetComponent>(plan.Planet).Prototype;
            return SWGlobalWarWithdrawalStatus.Returned;
        }
        finally
        {
            _spawn.ReleaseBodyReturn(plan);
        }
    }

    private bool ValidateWithdrawalCommit(SWGlobalWarWithdrawalOrder order, EntityUid? localPlanet,
        SWGlobalWarPlanetLocation localLocation, SWGlobalWarShipWithdrawalOrder? carrierOrder)
    {
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != order.Generation ||
            politics.Comp.Relations == SWGlobalWarRelations.War || Now() < order.Deadline ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            !_identity.TryGetOwnedBodyIdentity(order.Body, out var mind, out var faction) ||
            mind != order.Mind || faction != order.Faction)
            return false;
        if (localPlanet is { } local)
            return carrierOrder is { } ship && ship.Generation == order.Generation &&
                   politics.Comp.ShipWithdrawals.TryGetValue(ship.Ship, out var current) && current.Id == ship.Id &&
                   Now() >= current.Deadline && Transform(order.Body).GridUid == ship.Ship &&
                   _shipOwnership.TryGetOwner(ship.Ship, ship.Generation, out var owner, out var revision) &&
                   owner == ship.Faction && revision == ship.OwnershipRevision &&
                   _planets.TryGetPhysicalLocation(order.Body, out var actual, out var location) &&
                   actual == local && location == localLocation &&
                   IsLawfulPresence(politics.Comp, order.Body, mind, faction, local, localLocation);
        return politics.Comp.Withdrawals.TryGetValue(order.Body, out var bodyOrder) && bodyOrder.Id == order.Id &&
               TryGetWithdrawalLocation(politics.Comp, order.Body, mind, faction, out var planet, out var currentLocation) &&
               !IsLawfulPresence(politics.Comp, order.Body, mind, faction, planet, currentLocation);
    }

    private static void CompleteWithdrawal(Entity<SWGlobalWarPoliticsComponent> politics,
        SWGlobalWarWithdrawalOrder order, SWGlobalWarWithdrawalStatus status)
    {
        politics.Comp.Withdrawals.Remove(order.Body);
        politics.Comp.WithdrawalHistory[order.User] = order with { Status = status };
        politics.Comp.Revision++;
    }

    private static void CancelWithdrawals(Entity<SWGlobalWarPoliticsComponent> politics)
    {
        politics.Comp.ShipWithdrawals.Clear();
        foreach (var order in politics.Comp.Withdrawals.Values.ToArray())
            CompleteWithdrawal(politics, order with { WarResumed = true }, SWGlobalWarWithdrawalStatus.Cancelled);
    }

    private bool TryGetWithdrawalLocation(SWGlobalWarPoliticsComponent state, EntityUid body, EntityUid mind,
        SWGlobalWarFaction faction, out EntityUid planet, out SWGlobalWarPlanetLocation location)
    {
        if (_planets.TryGetPhysicalLocation(body, out planet, out location))
            return true;
        // Hyperspace is not a sector. Keep the actual flight context, including owned bodies whose minds are visiting.
        if (!TryComp<SWGlobalWarTravelComponent>(Transform(body).GridUid, out var flight) ||
            flight.Generation != state.Generation)
            return false;
        location = SWGlobalWarPlanetLocation.Orbit;
        planet = !IsLawfulPresence(state, body, mind, faction, flight.DestinationPlanet, location)
            ? flight.DestinationPlanet : flight.SourcePlanet;
        return true;
    }
}
