using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private readonly Dictionary<Guid, SWGlobalWarHubWorkGrant> _hubWorkGrants = new();
    private readonly Dictionary<(NetUserId, Guid), (EntityUid Hub, Guid Assignment,
        SWGlobalWarHubGrantCommand Command, Guid Grant)> _hubGrantRequests = new();
    private readonly Dictionary<(NetUserId, Guid), SWGlobalWarHubRevokeReceipt> _hubRevokeRequests = new();

    private bool HasHubAuthority(SWGlobalWarAssignment actor, EntityUid hub, SWGlobalWarHubCommand command)
    {
        return TryComp<SWGlobalWarHubComponent>(hub, out var device) && device.Binding is { } binding &&
            binding.Generation == actor.Generation && TryComp<SWGlobalWarPlanetComponent>(binding.Planet, out var planet) &&
            planet.OrbitOwner == actor.Faction && _politics.CanAcquireMarketGoods(actor.Body, actor.Generation) &&
            (HasLogisticsAuthority(actor, command.HeadDecision) || ValidHubGrant(actor, hub, command));
    }

    private bool ValidHubGrant(SWGlobalWarAssignment actor, EntityUid hub, SWGlobalWarHubCommand command)
    {
        return actor.Role == SWGlobalWarRole.LogisticsWorker &&
            _hubWorkGrants.TryGetValue(command.WorkGrant, out var grant) && !grant.Revoked &&
            grant.Generation == actor.Generation && grant.Hub == hub && grant.Employee == actor.User &&
            grant.Assignment == actor.Id && grant.Faction == actor.Faction &&
            command.MoneyOrder == grant.MoneyOrder && HubGrantCoversNextStep(command.ExpectedCapacity, grant.MaximumCapacity) &&
            _campaign.TryGetCampaign(out var campaign) && _campaign.Elapsed(campaign) < grant.Expires &&
            _assignments.TryGetAssignment(grant.Issuer, out var issuer) && issuer.Id == grant.IssuerAssignment &&
            HasLogisticsAuthority(issuer, grant.HeadDecision);
    }

    private static bool HubGrantCoversNextStep(int current, int maximum)
    {
        var next = current switch { 25 => 50, 50 => 75, 75 => 100, 150 => 200, 200 => 250, _ => 0 };
        return next > current && next <= maximum;
    }

    private bool ValidHubMoneyOrder(SWGlobalWarAssignment actor, SWGlobalWarHubCommand command, long price)
    {
        // The actor's own allocation is deliberately included in Remaining. CanSpendOrder excludes it.
        return command.MoneyOrder != Guid.Empty &&
            _ledger.TryGetReservation(command.MoneyOrder, out var order) && order.Faction == actor.Faction &&
            order.Account == SWGlobalWarAccount.Logistics && order.Employee == actor.User && order.Assignment == actor.Id &&
            order.Remaining >= price && _campaign.TryGetCampaign(out var campaign) &&
            order.Expires > _campaign.Elapsed(campaign) &&
            _hubWorkGrants.TryGetValue(command.WorkGrant, out var grant) && grant.MoneyOrder == order.Id &&
            grant.MoneyPurpose == order.Purpose;
    }

    /// <summary>Grants work on one existing orbital device with one existing employee money order.</summary>
    public bool TryGrantHubWork(ICommonSession session, EntityUid hub, SWGlobalWarHubGrantCommand command, out Guid id)
    {
        id = Guid.Empty;
        if (command == null || command.Request == Guid.Empty || !CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out var actor) ||
            !HasLogisticsAuthority(actor, command.HeadDecision) ||
            !TryGetWorkingHub(hub, command.Generation, out var binding, out var market) ||
            Comp<SWGlobalWarPlanetComponent>(binding.Planet).OrbitOwner != actor.Faction)
            return false;
        if (_hubGrantRequests.TryGetValue((actor.User, command.Request), out var prior))
        {
            if (prior.Command != command || prior.Hub != hub || prior.Assignment != actor.Id)
                return false;
            id = prior.Grant;
            return true;
        }
        if (!_assignments.TryGetAssignment(command.Employee, out var employee) ||
            employee.Role != SWGlobalWarRole.LogisticsWorker || employee.Faction != actor.Faction ||
            employee.Revision != command.EmployeeRevision ||
            !_ledger.TryGetReservation(command.MoneyOrder, out var order) || order.Employee != employee.User ||
            order.Assignment != employee.Id || order.Faction != actor.Faction ||
            order.Account != SWGlobalWarAccount.Logistics ||
            !TryGetHubUpgrade(market, out var next, out var price) || command.MaximumCapacity < next ||
            command.MaximumCapacity > market.Profile!.MaximumHubCapacity || order.Remaining < price ||
            !_campaign.TryGetCampaign(out var campaign) || command.Duration <= TimeSpan.Zero ||
            command.Duration > TimeSpan.MaxValue - _campaign.Elapsed(campaign) ||
            order.Expires <= _campaign.Elapsed(campaign))
            return false;
        id = Guid.NewGuid();
        _hubWorkGrants.Add(id, new(command.Generation, hub, actor.Faction, actor.User, actor.Id, command.HeadDecision,
            employee.User, employee.Id, order.Id, order.Purpose, command.MaximumCapacity,
            _campaign.Elapsed(campaign) + command.Duration));
        _hubGrantRequests.Add((actor.User, command.Request), (hub, actor.Id, command, id));
        return true;
    }

    /// <summary>Revokes only the physical work grant; cancellation releases its allocation inside the C order.</summary>
    public bool TryRevokeHubWork(ICommonSession session, EntityUid hub, SWGlobalWarHubRevokeRequest request)
    {
        if (request.Request == Guid.Empty || !CanMutateCampaign(request.Generation) ||
            !_assignments.TryGetActor(session, request.Generation, request.Revision, out var actor) ||
            !HasLogisticsAuthority(actor, request.HeadDecision) || !_hubWorkGrants.TryGetValue(request.Grant, out var grant) ||
            grant.Hub != hub || grant.Faction != actor.Faction || grant.Generation != actor.Generation)
            return false;
        var expected = new SWGlobalWarHubRevokeReceipt(actor.Id, request.Generation, request.Revision, hub,
            request.Grant, request.HeadDecision);
        var key = (actor.User, request.Request);
        if (_hubRevokeRequests.TryGetValue(key, out var previous))
            return previous == expected;
        if (!TryRevokeHubWork(session, request.Generation, request.Revision, request.Grant, request.HeadDecision))
            return false;
        _hubRevokeRequests.Add(key, expected);
        return true;
    }

    /// <summary>Trusted server revocation, still authorized by the current actor.</summary>
    public bool TryRevokeHubWork(ICommonSession session, Guid generation, long revision, Guid grantId, bool headDecision)
    {
        if (!CanMutateCampaign(generation) || !_assignments.TryGetActor(session, generation, revision, out var actor) ||
            !HasLogisticsAuthority(actor, headDecision) || !_hubWorkGrants.TryGetValue(grantId, out var grant) ||
            grant.Generation != generation || grant.Faction != actor.Faction)
            return false;
        _hubWorkGrants[grantId] = grant with { Revoked = true };
        // Revocation must not finish another due attempt before the market catches up under its old capacity.
        _hubMutating = true;
        try
        {
            using var notifications = _ledger.DeferNotifications();
            foreach (var work in _hubWork.Values.Where(w => w.Command.WorkGrant == grantId).ToArray())
                CancelHubWork(work, SWGlobalWarHubFailure.AuthorityChanged);
        }
        finally
        {
            _hubMutating = false;
        }
        return true;
    }

    private bool CanReadHubWork(SWGlobalWarAssignment actor, SWGlobalWarHubWork work)
    {
        return actor.Generation == work.Command.Generation && actor.Id == work.Assignment && actor.User == work.Session.UserId &&
            actor.Faction == work.Faction &&
            (actor.Role == SWGlobalWarRole.LogisticsWorker || HasLogisticsAuthority(actor, true)) &&
            (work.Result.Status != SWGlobalWarHubStatus.Working ||
                TryComp<SWGlobalWarPlanetComponent>(work.Planet, out var planet) && planet.OrbitOwner == actor.Faction);
    }

    /// <summary>Recovers an addressed receipt after window closure, without restoring another assignment's financial data.</summary>
    public SWGlobalWarHubResult? GetHubWorkResult(ICommonSession session, EntityUid hub, Guid generation, long revision,
        Guid request)
    {
        if (!_assignments.TryGetActor(session, generation, revision, out var actor) ||
            !_hubWork.TryGetValue((actor.User, request), out var work) || work.Hub != hub || !CanReadHubWork(actor, work))
            return null;
        return work.Result;
    }
}

internal sealed record SWGlobalWarHubWorkGrant(Guid Generation, EntityUid Hub, SWGlobalWarFaction Faction,
    NetUserId Issuer, Guid IssuerAssignment, bool HeadDecision, NetUserId Employee, Guid Assignment,
    Guid MoneyOrder, string MoneyPurpose, int MaximumCapacity, TimeSpan Expires, bool Revoked = false);

internal sealed record SWGlobalWarHubRevokeReceipt(Guid Assignment, Guid Generation, long Revision, EntityUid Hub,
    Guid Grant, bool HeadDecision);
