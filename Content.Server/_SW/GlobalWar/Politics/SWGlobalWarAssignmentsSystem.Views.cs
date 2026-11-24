using System.Linq;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Roles;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarAssignmentsSystem
{
    /// <summary>Projects the current attachment's service and allowed choices, excluding all foreign personnel.</summary>
    public SWGlobalWarCharacterView GetCharacterView(ICommonSession session, NetEntity netBody)
    {
        if (!TryGetRegistry(out var registry))
            return EmptyCharacter(netBody, Guid.Empty, SWGlobalWarViewAvailability.Unavailable);
        if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } body ||
            GetNetEntity(body) != netBody || !TryGetPersonnelActor(session, body, out var mind, out var faction))
            return EmptyCharacter(netBody, registry.Comp.Generation, SWGlobalWarViewAvailability.Forbidden);

        // Advance before capturing any projection fields. The network owner suppresses nested refreshes.
        SWGlobalWarSuccessionView? succession = null;
        if (TryGetSuccession(faction, session.UserId, out var vacancy, out var stage, out var remaining, out var eligible))
            succession = new(vacancy, stage, remaining, eligible);
        // Admin debugging: an aghost or foreign body of an admin sees the head's choices of the side it acts for.
        var debugging = TryGetAdminActor(session.UserId, out var actor);
        if (!debugging)
            registry.Comp.Assignments.TryGetValue(session.UserId, out actor);
        if (actor != null && (actor.Mind != mind || actor.Body != body || actor.Faction != faction))
            actor = null;
        var living = debugging || _mobs.IsAlive(body);
        var revision = actor?.Revision ?? 0;
        var offers = registry.Comp.Offers.Values.Where(o => o.Recipient == session.UserId && o.Mind == mind &&
                o.Body == body && o.RecipientRevision == revision)
            .TakeLast(32).Select(o => new SWGlobalWarAppointmentOfferView(o.Id, o.SenderName, o.SenderRole,
                o.Role, OfferFailure(o, session))).ToList();
        var outgoing = registry.Comp.CompletedOffers.Values.Where(o => o.Offer.Sender == session.UserId &&
                o.Offer.SenderBody == body && o.Offer.SenderMind == mind)
            .TakeLast(32).Select(o => new SWGlobalWarOutgoingOfferView(o.Offer.Id, o.Offer.RecipientName,
                o.Offer.Role, o.Status, SWGlobalWarAppointmentFailure.None)).ToList();
        foreach (var offer in registry.Comp.Offers.Values.Where(o => o.Sender == session.UserId &&
                     o.SenderBody == body && o.SenderMind == mind).TakeLast(32))
        {
            var failure = _players.TryGetSessionById(offer.Recipient, out var recipient)
                ? OfferFailure(offer, recipient, checkLocation: false) : SWGlobalWarAppointmentFailure.EligibilityChanged;
            outgoing.Add(new(offer.Id, offer.RecipientName, offer.Role, SWGlobalWarOfferStatus.Pending, failure));
        }
        var roles = new List<SWGlobalWarPersonnelRoleView>();
        var targets = new List<SWGlobalWarPersonnelTargetView>();
        if (living && actor != null && TryComp<SWGlobalWarSpawnConfigurationComponent>(registry.Owner, out var catalog))
        {
            foreach (var (job, entry) in catalog.Roles)
                if (entry.Faction == faction && entry.Role is { } role && MayAppoint(actor, role))
                    roles.Add(new(job, role, CanTake(faction, role)));
            var users = registry.Comp.Assignments.Values.Where(a => a.Faction == faction).Select(a => a.User)
                .Concat(_players.Sessions.Where(s => LivingActor(s, out _, out var f) && f == faction)
                    .Select(s => s.UserId)).Distinct();
            foreach (var user in users)
            {
                registry.Comp.Assignments.TryGetValue(user, out var target);
                var jobs = new List<ProtoId<JobPrototype>>();
                var targetBody = target?.Body;
                if (_players.TryGetSessionById(user, out var targetSession) &&
                    LivingActor(targetSession, out _, out var targetFaction) && targetFaction == faction)
                {
                    targetBody = targetSession.AttachedEntity;
                    foreach (var role in roles)
                        if (target?.Role != SWGlobalWarRole.Head && target?.Role != role.Role &&
                            CanAssumeCampaignJob(targetSession, targetBody!.Value, role.Job))
                            jobs.Add(role.Job);
                }
                var dismiss = target != null && MayAppoint(actor, target.Role);
                if (targetBody is { } knownBody && !TerminatingOrDeleted(knownBody) && (jobs.Count != 0 || dismiss))
                    targets.Add(new(user, Name(knownBody), target?.Revision ?? 0, target?.Role, dismiss, jobs));
            }
        }
        var head = registry.Comp.Assignments.Values.FirstOrDefault(a => a.Faction == faction && a.Role == SWGlobalWarRole.Head);
        TimeSpan? reserve = null;
        if (head != null && registry.Comp.HeadDisconnected.TryGetValue(head.Id, out var since) &&
            _campaign.TryGetCampaign(out var campaign))
            reserve = TimeSpan.FromTicks(Math.Max(0, (since + campaign.Comp.LeaderDisconnectReserve - CampaignNow()).Ticks));
        var receipts = registry.Comp.Requests.Where(p => p.Key.User == session.UserId && p.Value.Mind == mind &&
                p.Value.Body == body).TakeLast(32).Select(p => new SWGlobalWarAppointmentResultView(p.Key.Request,
                p.Value.Command.Action, p.Value.Success, p.Value.Failure)).ToList();
        return new(registry.Comp.Generation, registry.Comp.Revision, netBody, CampaignNow(),
            SWGlobalWarViewAvailability.Ready, faction, actor?.Id ?? Guid.Empty, revision, actor?.Role,
            living && actor != null ? GetAuthority(actor) : SWGlobalWarAuthority.None, living,
            head != null && !TerminatingOrDeleted(head.Body) ? Name(head.Body) : string.Empty, reserve,
            offers, succession, roles, targets, receipts, outgoing);
    }

    /// <summary>Clears all private projection fields on revocation or campaign shutdown.</summary>
    public static SWGlobalWarCharacterView EmptyCharacter(NetEntity body, Guid generation,
        SWGlobalWarViewAvailability availability)
    {
        return new(generation, 0, body, TimeSpan.Zero, availability, default, Guid.Empty, 0, null,
            SWGlobalWarAuthority.None, false, string.Empty, null, [], null, [], [], [], []);
    }
}
