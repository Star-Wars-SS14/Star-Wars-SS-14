using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Chat.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Player;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>Uses current appointments and physical ship property for both manual helm and interplanetary control.</summary>
public sealed class SWGlobalWarShipControlSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarCoachSystem _coach = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarShipOwnershipSystem _ownership = default!;
    [Dependency] private readonly ShuttleConsoleSystem _consoles = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(ShuttleSystem));
        SubscribeLocalEvent<ShuttleConsoleComponent, ActivatableUIOpenAttemptEvent>(OnOpen);
        SubscribeLocalEvent<SWGlobalWarFleetShipComponent, SWGlobalWarFtlMassLimitEvent>(OnMassLimit);
        SubscribeLocalEvent<SWGlobalWarShipOwnershipChangedEvent>((ref SWGlobalWarShipOwnershipChangedEvent _) =>
            RevalidatePilots());
    }

    public bool CanOperate(EntityUid console, EntityUid? body, out string reason)
    {
        reason = Loc.GetString("sw-global-war-travel-denied");
        // Ordinary shuttles in other modes keep their existing authorization.
        if (!_campaign.TryGetCampaign(out var campaign)) return true;
        // Private training maps retain ordinary local helm behavior. They do not acquire campaign travel rights.
        if (!_planets.TryGetPhysicalLocation(console, out _, out _) &&
            !(TryComp<TransformComponent>(console, out var context) && context.GridUid is { } flying &&
              HasComp<FTLComponent>(flying))) return true;
        if (campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running || body is not { } actor ||
            !TryComp<ActorComponent>(actor, out var player) ||
            !_assignments.TryGetAssignment(player.PlayerSession.UserId, out var assignment) ||
            !_assignments.TryGetActor(player.PlayerSession, campaign.Comp.Generation, assignment.Revision, out assignment) ||
            !TryComp<TransformComponent>(console, out var transform) || transform.GridUid is not { } ship ||
            Transform(actor).GridUid != ship ||
            !_ownership.TryGetOwner(ship, campaign.Comp.Generation, out var owner, out _) ||
            owner != assignment.Faction) return false;
        reason = string.Empty;
        return true;
    }

    private void OnMassLimit(Entity<SWGlobalWarFleetShipComponent> ent, ref SWGlobalWarFtlMassLimitEvent args)
    {
        if (!_campaign.TryGetCampaign(out var campaign) || args.Limit <= 0 ||
            !_ownership.TryGetOwner(ent.Owner, campaign.Comp.Generation, out _, out _)) return;
        // Compact heavy hulls have real measured masses of roughly 830-1150. This never changes their physics.
        if (ent.Comp.Class is SWGlobalWarShipClass.Cruiser or SWGlobalWarShipClass.Battleship)
            args.Limit = Math.Max(args.Limit, ent.Comp.Class == SWGlobalWarShipClass.Cruiser ? 1200 : 1600);
    }

    private void OnOpen(Entity<ShuttleConsoleComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!CanOperate(ent.Owner, args.User, out _))
        {
            args.Cancel();
            return;
        }

        // The interplanetary route is a verb on this same console, and nothing in the helm window points to it. In peace the
        // coach says so, and in a war it has no lines. A silent attempt is the verb menu checking the console, and the helm
        // window covers a popup, so the hint goes to chat.
        if (!args.Silent && HasComp<SWGlobalWarTravelConsoleComponent>(ent) && _campaign.TryGetCampaign(out _) &&
            !_coach.IsAtPeace() && TryComp<ActorComponent>(args.User, out var actor))
            _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString("sw-global-war-fleet-navigation-hint"));
    }

    public override void Update(float frameTime)
    {
        RevalidatePilots();
    }

    private void RevalidatePilots()
    {
        if (!_campaign.TryGetCampaign(out _)) return;
        var remove = new List<EntityUid>();
        var query = EntityQueryEnumerator<PilotComponent>();
        while (query.MoveNext(out var body, out var pilot))
            if (pilot.Console is { } console && !CanOperate(console, body, out _)) remove.Add(body);
        foreach (var body in remove) _consoles.RemovePilot(body);
    }
}

/// <summary>Allows a registered campaign hull to supply its class limit while retaining every other FTL check.</summary>
[ByRefEvent]
public record struct SWGlobalWarFtlMassLimitEvent(float Limit);
