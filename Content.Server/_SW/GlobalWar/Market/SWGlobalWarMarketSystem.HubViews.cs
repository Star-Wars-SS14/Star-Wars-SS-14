using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    /// <summary>Projects only the current orbital owner's work data; surface inventories are never part of this view.</summary>
    public SWGlobalWarHubView GetHubView(ICommonSession session, EntityUid hub)
    {
        var denied = new SWGlobalWarHubView(false, Guid.Empty, Guid.Empty, 0, TimeSpan.Zero, string.Empty,
            SWGlobalWarFaction.Neutral, SWGlobalWarFaction.Neutral, 0, 0, 0, false, false, false,
            SWGlobalWarHubFailure.Denied, string.Empty, null, [], [], []);
        if (!_campaign.TryGetCampaign(out var campaign) || !CanMutateCampaign(campaign.Comp.Generation) ||
            !_assignments.TryGetAssignment(session.UserId, out var actor) ||
            !_assignments.TryGetActor(session, campaign.Comp.Generation, actor.Revision, out actor) ||
            !(actor.Role == SWGlobalWarRole.LogisticsWorker || HasLogisticsAuthority(actor, true)))
            return denied;
        var ownResults = _hubWork.Values.Where(w => w.Hub == hub && CanReadHubWork(actor, w))
            .TakeLast(8).Select(w => w.Result).ToList();
        var time = _campaign.Elapsed(campaign);
        if (!TryComp<SWGlobalWarHubComponent>(hub, out var device) || device.Binding is not { } binding ||
            binding.Generation != campaign.Comp.Generation ||
            !TryComp<SWGlobalWarPlanetComponent>(binding.Planet, out var planet) || planet.OrbitOwner != actor.Faction ||
            Simulation == null || !Simulation.Markets.TryGetValue(planet.Prototype.Id, out var market))
            return denied with
            {
                Generation = actor.Generation, Assignment = actor.Id, AssignmentRevision = actor.Revision,
                Time = time, OwnResults = ownResults.Where(r => r.Status != SWGlobalWarHubStatus.Working).ToList(),
            };
        var manage = HasLogisticsAuthority(actor, true);
        var hasNext = TryGetHubUpgrade(market, out var next, out var price);
        var active = _hubWork.Values.FirstOrDefault(w => w.Hub == hub && w.Faction == actor.Faction &&
            w.Result.Status == SWGlobalWarHubStatus.Working);
        var failure = !TryGetWorkingHub(hub, actor.Generation, out _, out _) ? SWGlobalWarHubFailure.Unavailable :
            !CanReachHub(actor.Body, hub, binding) ? SWGlobalWarHubFailure.OutOfRange :
            active != null ? SWGlobalWarHubFailure.Busy : !hasNext ? SWGlobalWarHubFailure.MaximumCapacity :
            !_politics.CanAcquireMarketGoods(actor.Body, actor.Generation) ? SWGlobalWarHubFailure.Denied :
            manage && _ledger.GetBalance(actor.Faction, SWGlobalWarAccount.Logistics).Available < price
                ? SWGlobalWarHubFailure.Funding : SWGlobalWarHubFailure.None;
        var grants = new List<SWGlobalWarHubGrantView>();
        foreach (var (id, grant) in _hubWorkGrants.Where(p => p.Value.Hub == hub && p.Value.Faction == actor.Faction &&
                     (manage || p.Value.Employee == actor.User && p.Value.Assignment == actor.Id)).TakeLast(40))
        {
            var current = !grant.Revoked && time < grant.Expires &&
                _assignments.TryGetAssignment(grant.Issuer, out var issuer) && issuer.Id == grant.IssuerAssignment &&
                HasLogisticsAuthority(issuer, grant.HeadDecision);
            var command = new SWGlobalWarHubCommand(actor.Generation, Guid.Empty, actor.Revision,
                market.HubCapacity, grant.MoneyOrder, id);
            var funded = _ledger.TryGetReservation(grant.MoneyOrder, out var order);
            var grantFailure = !current ? SWGlobalWarHubFailure.AuthorityChanged :
                !HubGrantCoversNextStep(market.HubCapacity, grant.MaximumCapacity) ? SWGlobalWarHubFailure.MaximumCapacity :
                !funded || order.Remaining < price ? SWGlobalWarHubFailure.Funding :
                grant.Expires <= time + HubUpgradeDuration || order.Expires <= time + HubUpgradeDuration
                    ? SWGlobalWarHubFailure.InsufficientDuration :
                !manage && !ValidHubGrant(actor, hub, command) ? SWGlobalWarHubFailure.Denied : SWGlobalWarHubFailure.None;
            grants.Add(new(id, grant.MoneyOrder,
                _assignments.TryGetAssignment(grant.Employee, out var employee) ? Name(employee.Body) : string.Empty,
                grant.MaximumCapacity, grant.Expires, funded ? order.Expires : null, funded ? order.Remaining : 0, grant.MoneyPurpose,
                !current, grantFailure));
        }
        var orders = new List<SWGlobalWarHubOrderView>();
        if (manage && _ledger.TryGetLedger(out var ledger))
        {
            foreach (var order in ledger.Comp.Reservations.Values.Where(o => o.Faction == actor.Faction &&
                         o.Account == SWGlobalWarAccount.Logistics && o.Employee != null && o.Expires > time).TakeLast(40))
            {
                if (!_assignments.TryGetAssignment(order.Employee!.Value, out var employee) ||
                    employee.Id != order.Assignment || employee.Role != SWGlobalWarRole.LogisticsWorker)
                    continue;
                orders.Add(new(order.Id, employee.User, employee.Revision, Name(employee.Body), order.Remaining,
                    order.Expires!.Value, order.Purpose));
            }
        }
        return new(true, actor.Generation, actor.Id, actor.Revision, time, _prototypes.Index(planet.Prototype).Name,
            planet.OrbitOwner, planet.SurfaceOwner, market.HubCapacity, next, price, manage,
            actor.Role == SWGlobalWarRole.Head, actor.Role == SWGlobalWarRole.Head &&
                (_assignments.GetAuthority(actor) & SWGlobalWarAuthority.LogisticsBudget) == 0,
            failure, active == null ? string.Empty : Name(active.Body),
            active != null && (manage || active.Assignment == actor.Id) ? active.Result : null,
            ownResults,
            grants, orders);
    }
}
