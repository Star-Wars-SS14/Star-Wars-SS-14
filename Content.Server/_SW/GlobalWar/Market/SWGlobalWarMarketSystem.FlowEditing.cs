using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly IChatManager _economyChat = default!;
    private readonly Dictionary<(NetUserId, Guid), SWGlobalWarAuthorizedFlowReceipt> _flowRequests = new();
    private readonly Dictionary<Guid, SWGlobalWarFlowWorkGrant> _flowWorkGrants = new();
    private readonly Dictionary<(NetUserId, Guid), (SWGlobalWarFlowGrantCommand Command, Guid Grant)> _flowGrantRequests = new();
    private readonly List<SWGlobalWarFlowJournalEntry> _flowJournal = new();
    private readonly Dictionary<(NetUserId, Guid), (Guid Assignment, SWGlobalWarFlowRevokeCommand Command)> _flowRevokeRequests = new();
    public IReadOnlyList<SWGlobalWarFlowJournalEntry> FlowJournal => _flowJournal;

    private void ResetFlowEditing()
    {
        ResetGrowth();
        ResetReturns();
        _flowRequests.Clear();
        _flowWorkGrants.Clear();
        _flowGrantRequests.Clear();
        _flowJournal.Clear();
        _flowRevokeRequests.Clear();
    }

    private bool HasLogisticsAuthority(SWGlobalWarAssignment actor, bool headDecision)
    {
        return actor.Role == SWGlobalWarRole.LogisticsMinister || actor.Role == SWGlobalWarRole.Head &&
            (headDecision || (_assignments.GetAuthority(actor) & SWGlobalWarAuthority.LogisticsBudget) != 0);
    }

    private bool OwnsFlowEndpoints(SWGlobalWarFaction faction, string source, string destination)
    {
        return !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(destination) &&
            Simulation != null && Simulation.Markets.TryGetValue(source, out var from) &&
            Simulation.Markets.TryGetValue(destination, out var to) &&
            (from.Owner == faction ||
                from.Owner == SWGlobalWarFaction.Neutral && to.Owner == faction);
    }

    private bool CanViewFlow(SWGlobalWarAssignment actor, SWGlobalWarMarketFlow flow)
    {
        return HasEconomicOverview(actor) && flow.ContractFaction == actor.Faction;
    }

    private bool ValidFlowGrant(SWGlobalWarAssignment actor, Guid id, string? flow,
        SWGlobalWarFlowSettings settings)
    {
        return actor.Role == SWGlobalWarRole.LogisticsWorker &&
            _flowWorkGrants.TryGetValue(id, out var grant) && grant.Generation == actor.Generation &&
            !grant.Revoked && grant.Employee == actor.User && grant.Assignment == actor.Id &&
            grant.Faction == actor.Faction && _campaign.TryGetCampaign(out var campaign) &&
            _campaign.Elapsed(campaign) < grant.Expires &&
            _assignments.TryGetAssignment(grant.Issuer, out var issuer) && issuer.Id == grant.IssuerAssignment &&
            HasLogisticsAuthority(issuer, grant.HeadDecision) && grant.Flow == flow &&
            grant.Source == settings.Source.Id && grant.Destination == settings.Destination.Id &&
            grant.Commodity == settings.Commodity.Id && settings.Rate >= grant.MinimumRate &&
            settings.Rate <= grant.MaximumRate && settings.Order >= grant.MinimumOrder &&
            settings.Order <= grant.MaximumOrder;
    }

    /// <summary>Names the first identity check that refuses an economy command from this session.</summary>
    private SWGlobalWarEconomyFailure EconomyActorFailure(ICommonSession session, Guid generation, long revision,
        out SWGlobalWarAssignment actor)
    {
        actor = default!;
        if (!CanMutateCampaign(generation))
            return SWGlobalWarEconomyFailure.CampaignInactive;
        return _assignments.TryGetActor(session, generation, revision, out actor)
            ? SWGlobalWarEconomyFailure.None : SWGlobalWarEconomyFailure.AssignmentChanged;
    }

    private SWGlobalWarEconomyFailure LogisticsAuthorityFailure(SWGlobalWarAssignment actor, bool headDecision)
    {
        if (HasLogisticsAuthority(actor, headDecision))
            return SWGlobalWarEconomyFailure.None;
        return actor.Role == SWGlobalWarRole.Head
            ? SWGlobalWarEconomyFailure.HeadDecisionRequired : SWGlobalWarEconomyFailure.NoAuthority;
    }

    /// <summary>Same decision as office authority or <see cref="ValidFlowGrant"/>, but names the condition that failed.</summary>
    private SWGlobalWarEconomyFailure FlowAuthorityFailure(SWGlobalWarAssignment actor, bool headDecision, Guid id,
        string? flow, SWGlobalWarFlowSettings settings)
    {
        var failure = LogisticsAuthorityFailure(actor, headDecision);
        if (failure != SWGlobalWarEconomyFailure.NoAuthority)
            return failure;
        if (actor.Role != SWGlobalWarRole.LogisticsWorker || !_flowWorkGrants.TryGetValue(id, out var grant) ||
            grant.Generation != actor.Generation || grant.Employee != actor.User || grant.Assignment != actor.Id ||
            grant.Faction != actor.Faction)
            return SWGlobalWarEconomyFailure.NoAuthority;
        if (grant.Revoked)
            return SWGlobalWarEconomyFailure.GrantRevoked;
        if (!_campaign.TryGetCampaign(out var campaign) || _campaign.Elapsed(campaign) >= grant.Expires)
            return SWGlobalWarEconomyFailure.ExpiredGrant;
        if (!_assignments.TryGetAssignment(grant.Issuer, out var issuer) || issuer.Id != grant.IssuerAssignment ||
            !HasLogisticsAuthority(issuer, grant.HeadDecision))
            return SWGlobalWarEconomyFailure.AuthorityChanged;
        return ValidFlowGrant(actor, id, flow, settings)
            ? SWGlobalWarEconomyFailure.None : SWGlobalWarEconomyFailure.GrantOutOfRange;
    }

    /// <summary>Checks the sender, the flow's endpoints and the edited flow before any authority check.</summary>
    private SWGlobalWarEconomyFailure FlowEditFailure(ICommonSession session, SWGlobalWarFlowCommand command,
        out SWGlobalWarAssignment actor)
    {
        var failure = EconomyActorFailure(session, command.Generation, command.AssignmentRevision, out actor);
        if (failure != SWGlobalWarEconomyFailure.None)
            return failure;
        if (!OwnsFlowEndpoints(actor.Faction, command.Settings.Source.Id, command.Settings.Destination.Id))
            return SWGlobalWarEconomyFailure.NotOwned;
        if (command.Flow != null && (!Simulation!.Flows.TryGetValue(command.Flow, out var flow) || !CanViewFlow(actor, flow)))
            return SWGlobalWarEconomyFailure.FlowUnavailable;
        return SWGlobalWarEconomyFailure.None;
    }

    /// <summary>Money orders deliberately do not participate in this object-scoped work authorization.</summary>
    public bool TryGrantFlowWork(ICommonSession session, SWGlobalWarFlowGrantCommand command, out Guid grantId)
    {
        return TryGrantFlowWork(session, command, out grantId, out _);
    }

    /// <summary>Same as the overload without a failure, and names the first condition that refused the grant.</summary>
    public bool TryGrantFlowWork(ICommonSession session, SWGlobalWarFlowGrantCommand command, out Guid grantId,
        out SWGlobalWarEconomyFailure failure)
    {
        grantId = Guid.Empty;
        failure = SWGlobalWarEconomyFailure.Denied;
        if (command == null || command.Request == Guid.Empty || string.IsNullOrWhiteSpace(command.Commodity))
            return false;
        failure = EconomyActorFailure(session, command.Generation, command.AssignmentRevision, out var actor);
        if (failure == SWGlobalWarEconomyFailure.None)
            failure = LogisticsAuthorityFailure(actor, command.HeadDecision);
        if (failure != SWGlobalWarEconomyFailure.None)
            return false;
        var key = (session.UserId, command.Request);
        if (_flowGrantRequests.TryGetValue(key, out var receipt))
        {
            if (receipt.Command != command)
            {
                failure = SWGlobalWarEconomyFailure.InvalidRequest;
                return false;
            }
            grantId = receipt.Grant;
            return true;
        }
        if (!_assignments.TryGetAssignment(command.Employee, out var employee) ||
            employee.Faction != actor.Faction || employee.Role != SWGlobalWarRole.LogisticsWorker ||
            employee.Revision != command.EmployeeRevision)
            failure = SWGlobalWarEconomyFailure.WorkerChanged;
        else if (command.Duration <= TimeSpan.Zero)
            failure = SWGlobalWarEconomyFailure.InvalidDuration;
        else if (command.MinimumRate < 0 || command.MaximumRate < command.MinimumRate ||
                 command.MaximumOrder < command.MinimumOrder)
            failure = SWGlobalWarEconomyFailure.InvalidRange;
        else if (!OwnsFlowEndpoints(actor.Faction, command.Source, command.Destination))
            failure = SWGlobalWarEconomyFailure.NotOwned;
        else if (!Simulation!.Markets[command.Source].Goods.ContainsKey(command.Commodity) ||
                 !Simulation.Markets[command.Destination].Goods.ContainsKey(command.Commodity))
            failure = SWGlobalWarEconomyFailure.UnknownCommodity;
        else if (command.Flow != null && (!Simulation.Flows.TryGetValue(command.Flow, out var flow) || !CanViewFlow(actor, flow)))
            failure = SWGlobalWarEconomyFailure.FlowUnavailable;
        if (failure != SWGlobalWarEconomyFailure.None)
            return false;
        if (!_campaign.TryGetCampaign(out var campaign) || command.Duration > TimeSpan.MaxValue - _campaign.Elapsed(campaign))
        {
            failure = SWGlobalWarEconomyFailure.InvalidDuration;
            return false;
        }
        grantId = Guid.NewGuid();
        _flowWorkGrants.Add(grantId, new(grantId, actor.Generation, actor.Faction, actor.User, actor.Id,
            command.HeadDecision, employee.User, employee.Id, command.Flow, command.Source, command.Destination,
            command.Commodity, command.MinimumRate, command.MaximumRate, command.MinimumOrder, command.MaximumOrder,
            _campaign.Elapsed(campaign) + command.Duration));
        _flowGrantRequests.Add(key, (command, grantId));
        return true;
    }

    /// <summary>Revocation is immediate; it does not touch an unrelated C money reservation.</summary>
    public bool TryRevokeFlowWork(ICommonSession session, Guid generation, long revision, Guid grantId, bool headDecision)
    {
        if (!CanMutateCampaign(generation) || !_assignments.TryGetActor(session, generation, revision, out var actor) ||
            !HasLogisticsAuthority(actor, headDecision) || !_flowWorkGrants.TryGetValue(grantId, out var grant) ||
            grant.Generation != generation || grant.Faction != actor.Faction)
            return false;
        _flowWorkGrants[grantId] = grant with { Revoked = true };
        return true;
    }

    /// <summary>Replays an exact revocation only for the same current appointment and command.</summary>
    public bool TryRevokeFlowWork(ICommonSession session, SWGlobalWarFlowRevokeCommand command)
    {
        return TryRevokeFlowWork(session, command, out _);
    }

    /// <summary>Same as the overload without a failure, and names the first condition that refused the revocation.</summary>
    public bool TryRevokeFlowWork(ICommonSession session, SWGlobalWarFlowRevokeCommand command,
        out SWGlobalWarEconomyFailure failure)
    {
        failure = SWGlobalWarEconomyFailure.Denied;
        if (command == null || command.Request == Guid.Empty)
            return false;
        failure = EconomyActorFailure(session, command.Generation, command.AssignmentRevision, out var actor);
        if (failure == SWGlobalWarEconomyFailure.None)
            failure = LogisticsAuthorityFailure(actor, command.HeadDecision);
        if (failure != SWGlobalWarEconomyFailure.None)
            return false;
        var key = (session.UserId, command.Request);
        if (_flowRevokeRequests.TryGetValue(key, out var previous))
        {
            if (previous.Assignment == actor.Id && previous.Command == command)
                return true;
            failure = SWGlobalWarEconomyFailure.InvalidRequest;
            return false;
        }
        if (!TryRevokeFlowWork(session, command.Generation, command.AssignmentRevision, command.Grant, command.HeadDecision))
        {
            failure = SWGlobalWarEconomyFailure.Denied;
            return false;
        }
        _flowRevokeRequests.Add(key, (actor.Id, command));
        return true;
    }

    /// <summary>Authorizes current identity before replay or mutation; a per-flow revision detects actual conflicts.</summary>
    public bool TryEditFlow(ICommonSession session, SWGlobalWarFlowCommand command, out SWGlobalWarFlowResult result)
    {
        result = new(command?.Request ?? Guid.Empty, false, SWGlobalWarEconomyFailure.Denied);
        if (command == null || command.Settings == null || command.Request == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Settings.Source.Id) ||
            string.IsNullOrWhiteSpace(command.Settings.Destination.Id) ||
            string.IsNullOrWhiteSpace(command.Settings.Commodity.Id))
            return false;
        var failure = FlowEditFailure(session, command, out var actor);
        if (failure != SWGlobalWarEconomyFailure.None)
        {
            result = result with { Failure = failure };
            return false;
        }
        var key = (session.UserId, command.Request);
        _flowRequests.TryGetValue(key, out var previous);
        var authorizedFlow = command.Flow;
        if (previous != null && previous.Assignment == actor.Id && previous.Command == command && previous.Result.Success)
            authorizedFlow = previous.Result.Flow?.Id;
        failure = FlowAuthorityFailure(actor, command.HeadDecision, command.WorkGrant, authorizedFlow, command.Settings);
        if (failure != SWGlobalWarEconomyFailure.None)
        {
            result = result with { Failure = failure };
            return false;
        }
        if (previous != null)
        {
            if (previous.Assignment != actor.Id || previous.Command != command)
            {
                result = result with { Failure = SWGlobalWarEconomyFailure.InvalidRequest };
                return false;
            }
            result = previous.Result;
            return result.Success;
        }
        // Close elapsed seconds under the old contract before applying the new one.
        Update(0);
        failure = FlowEditFailure(session, command, out actor);
        if (failure == SWGlobalWarEconomyFailure.None)
            failure = FlowAuthorityFailure(actor, command.HeadDecision, command.WorkGrant, command.Flow, command.Settings);
        if (failure != SWGlobalWarEconomyFailure.None)
        {
            result = result with { Failure = failure };
            return false;
        }
        var flowId = command.Flow ?? $"user-{Guid.NewGuid():N}";
        var priority = FlowCategory(command.Settings);
        if (command.Flow != null && Simulation!.Flows.TryGetValue(command.Flow, out var unchanged) &&
            unchanged.Source == command.Settings.Source.Id && unchanged.Destination == command.Settings.Destination.Id &&
            unchanged.Commodity == command.Settings.Commodity.Id)
            priority = unchanged.Priority;
        var edit = new SWGlobalWarFlowEdit(command.Generation, Guid.NewGuid(), flowId,
            command.ExpectedRevision, command.Settings, priority, actor.Faction);
        var success = Simulation!.TryEditFlow(edit, out var receipt);
        result = new(command.Request, success, receipt.Failure, receipt.After);
        _flowRequests.Add(key, new(actor.Id, command, result));
        if (success)
        {
            if (command.Flow == null && actor.Role == SWGlobalWarRole.LogisticsWorker)
                _flowWorkGrants[command.WorkGrant] = _flowWorkGrants[command.WorkGrant] with { Flow = flowId };
            _flowJournal.Add(new(actor.Generation, command.Request, actor.User, actor.Id, actor.Faction,
                actor.Role, command.HeadDecision, Simulation.Time, receipt.Before, receipt.After!));
            // The completed revision and receipt exist before a minister can observe the intervention.
            if (actor.Role == SWGlobalWarRole.Head && command.HeadDecision && _assignments.TryGetRegistry(out var registry))
            {
                foreach (var minister in registry.Comp.Assignments.Values.Where(a =>
                             a.Faction == actor.Faction && a.Role == SWGlobalWarRole.LogisticsMinister))
                {
                    if (TryComp<ActorComponent>(minister.Body, out var player))
                        _economyChat.DispatchServerMessage(player.PlayerSession,
                            Loc.GetString("sw-global-war-economy-head-flow", ("flow", flowId)));
                }
            }
        }
        return success;
    }

    private SWGlobalWarFlowPriority FlowCategory(SWGlobalWarFlowSettings settings)
    {
        if (settings.Commodity.Id == "SWGlobalWarF")
            return SWGlobalWarFlowPriority.MandatoryFood;
        if (settings.Commodity.Id == "SWGlobalWarL")
            return SWGlobalWarFlowPriority.LuxuryAndExpansion;
        if (Simulation!.Markets[settings.Source.Id].Owner == SWGlobalWarFaction.Neutral ||
            Simulation.Markets[settings.Destination.Id].Owner == SWGlobalWarFaction.Neutral)
            return SWGlobalWarFlowPriority.NeutralIndustry;
        return SWGlobalWarFlowPriority.DomesticCivilian;
    }

    /// <summary>Private contracts and receipts are selected before the console serializes them.</summary>
    public List<SWGlobalWarFlowView> GetFlowViews(EntityUid? viewer)
    {
        if (!TryGetMarketObserver(viewer, out var actor) || Simulation == null)
            return [];
        return Simulation.Flows.Values.Where(f => CanViewFlow(actor, f)).OrderBy(f => f.Id, StringComparer.Ordinal)
            .Select(f => Simulation.FlowView(f, HasLogisticsAuthority(actor, true) ||
                _flowWorkGrants.Values.Any(g => ValidFlowGrant(actor, g.Id, f.Id, Simulation.FlowView(f).Settings))))
            .ToList();
    }

    internal List<SWGlobalWarFlowResult> GetFlowResults(SWGlobalWarAssignment actor)
    {
        return _flowRequests.Where(p => p.Key.Item1 == actor.User && p.Value.Assignment == actor.Id && HasEconomicOverview(actor))
            .Select(p => p.Value.Result).TakeLast(8).ToList();
    }

    internal bool CanManageFlowWork(SWGlobalWarAssignment actor) => HasLogisticsAuthority(actor, true);

    internal List<SWGlobalWarNeutralOfferView> GetNeutralOffers(SWGlobalWarAssignment actor)
    {
        if (!HasEconomicOverview(actor) || Simulation == null)
            return [];
        return Simulation.Markets.Values.Where(m => m.Owner == SWGlobalWarFaction.Neutral)
            .SelectMany(m => m.Goods.Keys.Select(commodity => new SWGlobalWarNeutralOfferView(m.Planet, commodity,
                Simulation.NeutralOffer(m.Planet, commodity, actor.Faction),
                Simulation.NeutralOrderedRate(m.Planet, commodity, actor.Faction)))).ToList();
    }

    internal List<SWGlobalWarFlowWorkerView> GetFlowWorkers(SWGlobalWarAssignment actor)
    {
        if (!CanManageFlowWork(actor) || !_assignments.TryGetRegistry(out var registry))
            return [];
        return registry.Comp.Assignments.Values.Where(a => a.Faction == actor.Faction &&
            a.Role == SWGlobalWarRole.LogisticsWorker).Select(a => new SWGlobalWarFlowWorkerView(a.User, a.Revision,
            Name(a.Body))).ToList();
    }

    internal List<SWGlobalWarFlowGrantView> GetFlowGrants(SWGlobalWarAssignment actor)
    {
        return _flowWorkGrants.Values.Where(g => g.Generation == actor.Generation && g.Faction == actor.Faction &&
                (CanManageFlowWork(actor) || g.Employee == actor.User && g.Assignment == actor.Id))
            .Select(g => new SWGlobalWarFlowGrantView(g.Id, g.Flow, g.Source, g.Destination, g.Commodity,
                g.MinimumRate, g.MaximumRate, g.MinimumOrder, g.MaximumOrder, g.Expires,
                _assignments.TryGetAssignment(g.Employee, out var employee) ? Name(employee.Body) : string.Empty,
                g.Revoked || !_assignments.TryGetAssignment(g.Issuer, out var issuer) ||
                issuer.Id != g.IssuerAssignment || !HasLogisticsAuthority(issuer, g.HeadDecision))).ToList();
    }
}

public sealed record SWGlobalWarFlowWorkGrant(Guid Id, Guid Generation, SWGlobalWarFaction Faction,
    NetUserId Issuer, Guid IssuerAssignment, bool HeadDecision, NetUserId Employee, Guid Assignment,
    string? Flow, string Source, string Destination, string Commodity, int MinimumRate, int MaximumRate,
    int MinimumOrder, int MaximumOrder, TimeSpan Expires, bool Revoked = false);

public sealed record SWGlobalWarAuthorizedFlowReceipt(Guid Assignment, SWGlobalWarFlowCommand Command,
    SWGlobalWarFlowResult Result);

public sealed record SWGlobalWarFlowJournalEntry(Guid Generation, Guid Request, NetUserId Author, Guid Assignment,
    SWGlobalWarFaction Faction, SWGlobalWarRole Role, bool HeadDecision, TimeSpan At,
    SWGlobalWarFlowView? Before, SWGlobalWarFlowView After);
