using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private readonly Dictionary<(NetUserId, Guid), GrowthReceipt> _growthRequests = new();
    private readonly Dictionary<Guid, GrowthGrant> _growthGrants = new();
    private readonly List<SWGlobalWarGrowthJournalEntry> _growthJournal = new();
    public IReadOnlyList<SWGlobalWarGrowthJournalEntry> GrowthJournal => _growthJournal;
    internal bool RequiresLogisticsHeadDecision(SWGlobalWarAssignment actor) =>
        actor.Role == SWGlobalWarRole.Head && !HasLogisticsAuthority(actor, false);

    private void ResetGrowth()
    {
        _growthRequests.Clear();
        _growthGrants.Clear();
        _growthJournal.Clear();
    }

    private bool CurrentGrowthGrant(GrowthGrant grant)
    {
        return !grant.Revoked && _campaign.TryGetCampaign(out var campaign) &&
            campaign.Comp.Generation == grant.Command.Generation && _campaign.Elapsed(campaign) < grant.Expires &&
            _assignments.TryGetAssignment(grant.Issuer, out var issuer) && issuer.Id == grant.IssuerAssignment &&
            issuer.Faction == grant.Faction &&
            HasLogisticsAuthority(issuer, grant.Command.HeadDecision) &&
            _assignments.TryGetAssignment(grant.Command.Employee!.Value, out var employee) &&
            employee.Id == grant.EmployeeAssignment;
    }

    private SWGlobalWarGrowthFailure GrowthGrantFailure(GrowthGrant grant)
    {
        if (grant.Revoked)
            return SWGlobalWarGrowthFailure.GrantRevoked;
        if (_campaign.TryGetCampaign(out var campaign) && _campaign.Elapsed(campaign) >= grant.Expires)
            return SWGlobalWarGrowthFailure.GrantExpired;
        return CurrentGrowthGrant(grant) ? SWGlobalWarGrowthFailure.None : SWGlobalWarGrowthFailure.AuthorityChanged;
    }

    private SWGlobalWarGrowthFailure GrowthAuthorityFailure(SWGlobalWarAssignment actor, SWGlobalWarGrowthCommand command)
    {
        if (HasLogisticsAuthority(actor, command.HeadDecision))
            return SWGlobalWarGrowthFailure.None;
        if (actor.Role == SWGlobalWarRole.Head)
            return SWGlobalWarGrowthFailure.HeadDecisionRequired;
        if (actor.Role != SWGlobalWarRole.LogisticsWorker || command.Action != SWGlobalWarGrowthAction.Set)
            return SWGlobalWarGrowthFailure.Denied;
        if (!_growthGrants.TryGetValue(command.Grant, out var grant) || grant.EmployeeAssignment != actor.Id ||
            grant.Command.Employee != actor.User || grant.Command.Planet != command.Planet)
            return SWGlobalWarGrowthFailure.NoGrant;
        var failure = GrowthGrantFailure(grant);
        if (failure != SWGlobalWarGrowthFailure.None)
            return failure;
        return command.Percent < grant.Command.Minimum || command.Percent > grant.Command.Maximum
            ? SWGlobalWarGrowthFailure.OutsideRange : SWGlobalWarGrowthFailure.None;
    }

    /// <summary>Settles elapsed consumption under the old fraction before changing future discretionary food.</summary>
    public bool TryConfigureGrowth(ICommonSession session, SWGlobalWarGrowthCommand command, out SWGlobalWarGrowthResult result)
    {
        result = new(command?.Request ?? Guid.Empty, command?.Action ?? default, false, SWGlobalWarGrowthFailure.Denied);
        if (command == null || command.Request == Guid.Empty || string.IsNullOrEmpty(command.Planet) ||
            !Enum.IsDefined(command.Action))
            return false;
        if (!CanMutateCampaign(command.Generation))
        {
            result = result with { Failure = SWGlobalWarGrowthFailure.CampaignInactive };
            return false;
        }
        if (!_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out var actor))
        {
            result = result with { Failure = SWGlobalWarGrowthFailure.AssignmentChanged };
            return false;
        }
        if (!Simulation!.Markets.TryGetValue(command.Planet, out var market) || market.Profile == null)
            return false;
        var authorization = market.Owner != actor.Faction ? SWGlobalWarGrowthFailure.SurfaceChanged : GrowthAuthorityFailure(actor, command);
        if (authorization != SWGlobalWarGrowthFailure.None)
        {
            result = result with { Failure = authorization };
            return false;
        }
        var key = (actor.User, command.Request);
        if (_growthRequests.TryGetValue(key, out var previous))
        {
            if (previous.Assignment != actor.Id || previous.Command != command)
                return false;
            result = previous.Result;
            return result.Success;
        }
        Update(0);
        if (!CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out actor) ||
            market.Owner != actor.Faction || GrowthAuthorityFailure(actor, command) != SWGlobalWarGrowthFailure.None || !_campaign.TryGetCampaign(out var campaign))
            return false;
        var before = (int) (market.GrowthFoodFraction * 100);
        var failure = SWGlobalWarGrowthFailure.None;
        switch (command.Action)
        {
            case SWGlobalWarGrowthAction.Set:
                if (command.Percent is < 0 or > 25)
                    failure = SWGlobalWarGrowthFailure.InvalidRequest;
                else if (command.ExpectedRevision != market.GrowthRevision)
                    failure = SWGlobalWarGrowthFailure.Conflict;
                else
                {
                    market.GrowthFoodFraction = command.Percent / 100m;
                    market.GrowthRevision++;
                }
                break;
            case SWGlobalWarGrowthAction.Grant:
                if (command.Employee is not { } user || !_assignments.TryGetAssignment(user, out var employee) ||
                    employee.Role != SWGlobalWarRole.LogisticsWorker || employee.Faction != actor.Faction ||
                    employee.Revision != command.EmployeeRevision || command.Minimum < 0 || command.Maximum > 25 ||
                    command.Maximum < command.Minimum || command.Minutes <= 0)
                    failure = SWGlobalWarGrowthFailure.InvalidRequest;
                else
                    _growthGrants.Add(Guid.NewGuid(), new(command, actor.User, actor.Id, actor.Faction, employee.Id,
                        _campaign.Elapsed(campaign) + TimeSpan.FromMinutes(command.Minutes)));
                break;
            case SWGlobalWarGrowthAction.Revoke:
                if (!_growthGrants.TryGetValue(command.Grant, out var grant) || grant.Command.Planet != command.Planet ||
                    grant.Command.Generation != command.Generation || grant.Faction != actor.Faction)
                    failure = SWGlobalWarGrowthFailure.Denied;
                else
                    _growthGrants[command.Grant] = grant with { Revoked = true };
                break;
        }
        result = new(command.Request, command.Action, failure == SWGlobalWarGrowthFailure.None, failure,
            new(command.Planet, market.GrowthRevision, (int) (market.GrowthFoodFraction * 100)));
        _growthRequests.Add(key, new(actor.Id, command, result));
        if (!result.Success)
            return false;
        _growthJournal.Add(new(command, actor.User, actor.Id, actor.Faction, before, result.View!.Percent, Simulation.Time));
        if (actor.Role == SWGlobalWarRole.Head && command.HeadDecision && _assignments.TryGetRegistry(out var registry))
            foreach (var minister in registry.Comp.Assignments.Values.Where(a => a.Faction == actor.Faction && a.Role == SWGlobalWarRole.LogisticsMinister))
                if (TryComp<ActorComponent>(minister.Body, out var player))
                    _economyChat.DispatchServerMessage(player.PlayerSession, Loc.GetString("sw-global-war-growth-head",
                        ("planet", command.Planet), ("before", before), ("after", result.View.Percent)));
        return true;
    }

    internal List<SWGlobalWarGrowthView> GetGrowthViews(SWGlobalWarAssignment actor) => !HasEconomicOverview(actor) || Simulation == null
        ? [] : Simulation.Markets.Values.Where(m => m.Owner == actor.Faction && m.Profile != null)
            .Select(m => new SWGlobalWarGrowthView(m.Planet, m.GrowthRevision, (int) (m.GrowthFoodFraction * 100))).ToList();

    internal List<SWGlobalWarGrowthGrantView> GetGrowthGrants(SWGlobalWarAssignment actor) => _growthGrants
        .Where(p => p.Value.Command.Generation == actor.Generation && p.Value.Faction == actor.Faction &&
            Simulation!.Markets[p.Value.Command.Planet].Owner == actor.Faction &&
            (HasLogisticsAuthority(actor, true) || p.Value.EmployeeAssignment == actor.Id))
        .Select(p => new SWGlobalWarGrowthGrantView(p.Key, p.Value.Command.Planet, p.Value.Command.Minimum, p.Value.Command.Maximum,
            _assignments.TryGetAssignment(p.Value.Command.Employee!.Value, out var employee) ? Name(employee.Body) : string.Empty,
            p.Value.Expires, p.Value.Revoked, GrowthGrantFailure(p.Value))).ToList();

    internal List<SWGlobalWarGrowthResult> GetGrowthResults(SWGlobalWarAssignment actor) => _growthRequests
        .Where(p => p.Key.Item1 == actor.User && p.Value.Assignment == actor.Id && HasEconomicOverview(actor))
        .Select(p => p.Value.Result).TakeLast(8).ToList();

    private sealed record GrowthGrant(SWGlobalWarGrowthCommand Command, NetUserId Issuer, Guid IssuerAssignment, SWGlobalWarFaction Faction,
        Guid EmployeeAssignment, TimeSpan Expires, bool Revoked = false);
    private sealed record GrowthReceipt(Guid Assignment, SWGlobalWarGrowthCommand Command, SWGlobalWarGrowthResult Result);
}

public sealed record SWGlobalWarGrowthJournalEntry(SWGlobalWarGrowthCommand Command, NetUserId Author,
    Guid Assignment, SWGlobalWarFaction Faction, int Before, int After, TimeSpan At);
