using Content.Server._SW.GlobalWar.Access;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    [Dependency] private readonly MobStateSystem _mobs = default!;

    private void OnEvaluateAppointment(ref SWGlobalWarEvaluateAppointmentEvent args)
    {
        args.Allowed = CanAcceptRole(args.Body, args.Mind, args.Faction, args.Role, args.Generation);
    }

    private bool CanAcceptRole(EntityUid body, EntityUid mind, SWGlobalWarFaction faction, SWGlobalWarRole role,
        Guid generation)
    {
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != generation)
            return false;
        // Civilian appointments remain voluntary even when an independent Force status still requires withdrawal.
        if (!IsMilitary(role))
            return true;
        if (_planets.TryGetPhysicalLocation(body, out var planet, out var location))
            return IsLawfulPresence(politics.Comp, body, mind, faction, planet, location, role);
        // Hyperspace has no physical sector. Apply the existing flight context to the proposed status at both ends.
        return TryComp<SWGlobalWarTravelComponent>(Transform(body).GridUid, out var flight) &&
               flight.Generation == generation &&
               IsLawfulPresence(politics.Comp, body, mind, faction, flight.SourcePlanet,
                   SWGlobalWarPlanetLocation.Orbit, role) &&
               IsLawfulPresence(politics.Comp, body, mind, faction, flight.DestinationPlanet,
                   SWGlobalWarPlanetLocation.Orbit, role);
    }

    private void OnEvaluateAccess(ref SWGlobalWarEvaluateAccessEvent args)
    {
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != args.Generation)
            return;
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            !TryComp<SWGlobalWarPlanetComponent>(args.DestinationPlanet, out var destination))
        {
            args.Allowed = false;
            return;
        }
        var owner = args.DestinationLocation == SWGlobalWarPlanetLocation.Orbit
            ? destination.OrbitOwner : destination.SurfaceOwner;
        var role = GetBodyRole(args.Actor, args.Mind);
        if (politics.Comp.Withdrawals.TryGetValue(args.Actor, out var withdrawal) &&
            withdrawal.Generation == args.Generation && withdrawal.Mind == args.Mind &&
            withdrawal.Sector == args.SourcePlanet && args.SourcePlanet == args.DestinationPlanet &&
            args.DestinationLocation == SWGlobalWarPlanetLocation.Orbit)
        {
            // Only this existing person's exit from the recorded sector; no reinforcement may borrow this exception.
            args.Allowed = true;
            return;
        }
        // Identity and route were checked by B. An intelligence officer's exception applies to this body only.
        if (!IsForceCarrier(args.Actor, args.Mind, args.Generation) &&
            (role == SWGlobalWarRole.Intelligence || !IsMilitary(role)))
        {
            args.Allowed = true;
            return;
        }
        if (politics.Comp.Neutrals.TryGetValue(destination.Prototype, out var neutral) &&
            neutral.Phase != SWGlobalWarNeutralPhase.Integrated)
        {
            args.Allowed = CombatOpen(politics.Comp, destination);
            return;
        }
        args.Allowed = owner == args.Faction || politics.Comp.Relations == SWGlobalWarRelations.War;
    }

    /// <summary>Political capture gate for F; F separately checks objective, physical presence and contesting defenders.</summary>
    public bool CanCapture(EntityUid actor, EntityUid planet, SWGlobalWarPlanetLocation location, Guid generation)
    {
        if (!Enum.IsDefined(location) || !TryGetPolitics(out var politics) || politics.Comp.Generation != generation ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            politics.Comp.Withdrawals.ContainsKey(actor) ||
            !_identity.TryGetIdentity(actor, out var mind, out var faction) || !_mobs.IsAlive(actor) ||
            !HasCombatRole(actor, mind, politics.Comp.Relations) ||
            !TryComp<SWGlobalWarPlanetComponent>(planet, out var target) ||
            target.GlobalWarGamerule != politics.Owner || !CombatOpen(politics.Comp, target))
            return false;
        if (location == SWGlobalWarPlanetLocation.Orbit)
            return target.OrbitOwner != faction;
        return target.SurfaceOwner != faction && target.OrbitOwner == faction &&
               (!politics.Comp.Neutrals.TryGetValue(target.Prototype, out var neutral) ||
                neutral.Phase == SWGlobalWarNeutralPhase.Integrated);
    }

    /// <summary>
    /// Political gate for lethal attacks, explosions and production sabotage in F; this does not apply damage.
    /// Permitted nonlethal escape actions use their own action-specific gate, not this broad combat permission.
    /// </summary>
    public bool CanConductHostility(EntityUid actor, EntityUid planet, Guid generation)
    {
        return TryGetPolitics(out var politics) && politics.Comp.Generation == generation &&
               _campaign.TryGetCampaign(out var campaign) && campaign.Comp.Phase == SWGlobalWarCampaignPhase.Running &&
               !politics.Comp.Withdrawals.ContainsKey(actor) &&
               _identity.TryGetIdentity(actor, out var mind, out _) && _mobs.IsAlive(actor) &&
               HasCombatRole(actor, mind, politics.Comp.Relations) &&
               TryComp<SWGlobalWarPlanetComponent>(planet, out var target) &&
               target.GlobalWarGamerule == politics.Owner && CombatOpen(politics.Comp, target);
    }

    private SWGlobalWarRole? GetBodyRole(EntityUid body, EntityUid mind)
    {
        if (!TryComp<MindComponent>(mind, out var data) || data.UserId is not { } user ||
            !_appointments.TryGetAssignment(user, out var assignment) || assignment.Body != body || assignment.Mind != mind)
            return null;
        return assignment.Role;
    }

    private bool IsForceCarrier(EntityUid body, EntityUid mind, Guid generation)
    {
        return TryComp<MindComponent>(mind, out var data) && data.UserId is { } user &&
               _appointments.TryGetForceCarrier(user, out var carrier) && carrier.Generation == generation &&
               carrier.Body == body && carrier.Mind == mind && carrier.User == user;
    }

    private bool HasCombatRole(EntityUid body, EntityUid mind, SWGlobalWarRelations relations)
    {
        var role = GetBodyRole(body, mind);
        return IsMilitary(role) && (role != SWGlobalWarRole.Intelligence || relations == SWGlobalWarRelations.War);
    }

    private static bool IsMilitary(SWGlobalWarRole? role)
    {
        return role is SWGlobalWarRole.SeniorOfficer or SWGlobalWarRole.JuniorOfficer or SWGlobalWarRole.Pilot or
            SWGlobalWarRole.Soldier or SWGlobalWarRole.ForceUser or SWGlobalWarRole.Intelligence;
    }

    private static bool CombatOpen(SWGlobalWarPoliticsComponent state, SWGlobalWarPlanetComponent planet)
    {
        if (state.Neutrals.TryGetValue(planet.Prototype, out var neutral) &&
            neutral.Phase == SWGlobalWarNeutralPhase.CivilWar)
            return false;
        return state.Relations == SWGlobalWarRelations.War ||
               state.Relations == SWGlobalWarRelations.Peace && neutral is
                   { Phase: SWGlobalWarNeutralPhase.Annexing, BorderConflict: not null };
    }
}
