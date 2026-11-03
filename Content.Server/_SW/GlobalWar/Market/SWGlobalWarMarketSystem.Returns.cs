using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private readonly Dictionary<(NetUserId, Guid), ReturnRequestReceipt> _returnRequests = new();
    private readonly Dictionary<Guid, ReturnGrant> _returnGrants = new();
    private readonly List<SWGlobalWarReturnJournalEntry> _returnJournal = new();
    public IReadOnlyList<SWGlobalWarReturnJournalEntry> ReturnJournal => _returnJournal;

    private void ResetReturns()
    {
        _returnRequests.Clear();
        _returnGrants.Clear();
        _returnJournal.Clear();
    }

    internal static SWGlobalWarReturnSelection ReturnSelection(SWGlobalWarShipment part)
    {
        return new(part.Id, part.SupplyManifest?.Id ?? part.Batch!.Id,
            part.SupplyManifest?.Owner ?? part.Batch!.Owner, part.Route[part.Leg], part.Quantity,
            part.Destination, part.ReturnOperation, part.SupplyManifest?.DestinationRevision ?? Guid.Empty);
    }

    private SWGlobalWarReturnFailure ReturnGrantFailure(ReturnGrant grant)
    {
        if (grant.Used)
            return SWGlobalWarReturnFailure.GrantUsed;
        if (grant.Revoked)
            return SWGlobalWarReturnFailure.GrantRevoked;
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Generation != grant.Command.Generation)
            return SWGlobalWarReturnFailure.AuthorityChanged;
        if (_campaign.Elapsed(campaign) >= grant.Expires)
            return SWGlobalWarReturnFailure.GrantExpired;
        if (!_assignments.TryGetAssignment(grant.Issuer, out var issuer) || issuer.Id != grant.IssuerAssignment ||
            issuer.Faction != grant.Faction || !HasLogisticsAuthority(issuer, grant.Command.HeadDecision) ||
            !_assignments.TryGetAssignment(grant.Command.Employee!.Value, out var employee) ||
            employee.Id != grant.EmployeeAssignment)
            return SWGlobalWarReturnFailure.AuthorityChanged;
        return SWGlobalWarReturnFailure.None;
    }

    private SWGlobalWarReturnFailure ReturnAuthorityFailure(SWGlobalWarAssignment actor, SWGlobalWarReturnCommand command)
    {
        if (HasLogisticsAuthority(actor, command.HeadDecision))
            return SWGlobalWarReturnFailure.None;
        if (actor.Role == SWGlobalWarRole.Head)
            return SWGlobalWarReturnFailure.HeadDecisionRequired;
        if (actor.Role != SWGlobalWarRole.LogisticsWorker || command.Action != SWGlobalWarReturnAction.Redirect ||
            !_returnGrants.TryGetValue(command.Grant, out var grant) || grant.EmployeeAssignment != actor.Id ||
            grant.Command.Employee != actor.User || grant.Faction != actor.Faction ||
            grant.Command.Part != command.Part || grant.Command.Destination != command.Destination ||
            grant.Command.DestinationStore != command.DestinationStore)
            return SWGlobalWarReturnFailure.Denied;
        return ReturnGrantFailure(grant);
    }

    private SWGlobalWarReturnFailure ReturnPartFailure(SWGlobalWarAssignment actor, SWGlobalWarReturnCommand command)
    {
        var part = Simulation!.Shipments.FirstOrDefault(s => s.Id == command.Part.Shipment);
        if (part == null || part.Batch == null && part.SupplyManifest == null || ReturnSelection(part) != command.Part)
            return SWGlobalWarReturnFailure.Changed;
        if ((part.SupplyManifest?.Owner ?? part.Batch?.Owner) != actor.Faction)
            return SWGlobalWarReturnFailure.NotOwner;
        return Simulation.CheckReturn(new(command.Generation, command.Request, part.Id, actor.Faction,
            command.Destination, part.Quantity, command.DestinationStore is { } point ? GetEntity(point) : null));
    }

    /// <summary>Authorizes one exact waiting part. Historical replay never consumes a work grant twice.</summary>
    public bool TryConfigureReturn(ICommonSession session, SWGlobalWarReturnCommand command, out SWGlobalWarReturnResult result)
    {
        result = new(command?.Request ?? Guid.Empty, command?.Action ?? default, false, SWGlobalWarReturnFailure.Denied);
        if (command == null || command.Part == null || command.Request == Guid.Empty ||
            !Enum.IsDefined(command.Action) || string.IsNullOrEmpty(command.Destination) ||
            !CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out var actor) ||
            !HasEconomicOverview(actor))
            return false;
        var key = (actor.User, command.Request);
        // A current assignment may read its own accepted history after the physical cargo or grant has changed.
        if (_returnRequests.TryGetValue(key, out var previous))
        {
            if (previous.Assignment != actor.Id || previous.Command != command)
                return false;
            result = RefreshReturnReceipt(previous);
            return result.Success;
        }
        if (Simulation!.GetReturnReceipt(command.Request) != null)
        {
            result = result with { Failure = SWGlobalWarReturnFailure.InvalidRequest };
            return false;
        }
        var failure = ReturnAuthorityFailure(actor, command);
        if (failure != SWGlobalWarReturnFailure.None)
        {
            result = result with { Failure = failure };
            return false;
        }
        Update(0);
        if (!CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out actor) ||
            !_campaign.TryGetCampaign(out var campaign))
            return false;
        failure = ReturnAuthorityFailure(actor, command);
        if (failure == SWGlobalWarReturnFailure.None && command.Action != SWGlobalWarReturnAction.Revoke)
            failure = ReturnPartFailure(actor, command);
        var grantId = Guid.Empty;
        SWGlobalWarReturnReceiptView? receipt = null;
        if (failure == SWGlobalWarReturnFailure.None)
        {
            var target = ReturnTarget(command);
            switch (command.Action)
            {
                case SWGlobalWarReturnAction.Redirect:
                    if (!Simulation!.TryReturnShipment(new(command.Generation, command.Request, command.Part.Shipment,
                            actor.Faction, command.Destination, command.Part.Quantity,
                            command.DestinationStore is { } point ? GetEntity(point) : null), out var accepted))
                        failure = SWGlobalWarReturnFailure.Changed;
                    else
                    {
                        receipt = ReturnReceiptView(command, accepted, target);
                        if (!HasLogisticsAuthority(actor, command.HeadDecision))
                            _returnGrants[command.Grant] = _returnGrants[command.Grant] with { Used = true };
                    }
                    break;
                case SWGlobalWarReturnAction.Grant:
                    if (command.Employee is not { } user)
                        failure = SWGlobalWarReturnFailure.NoWorker;
                    else if (!_assignments.TryGetAssignment(user, out var employee) ||
                        employee.Role != SWGlobalWarRole.LogisticsWorker || employee.Faction != actor.Faction ||
                        employee.Revision != command.EmployeeRevision)
                        failure = SWGlobalWarReturnFailure.WorkerChanged;
                    else if (command.Minutes <= 0 ||
                        TimeSpan.FromMinutes(command.Minutes) > TimeSpan.MaxValue - _campaign.Elapsed(campaign))
                        failure = SWGlobalWarReturnFailure.InvalidDuration;
                    else
                    {
                        grantId = Guid.NewGuid();
                        _returnGrants.Add(grantId, new(command, actor.User, actor.Id, actor.Faction, employee.Id,
                            _campaign.Elapsed(campaign) + TimeSpan.FromMinutes(command.Minutes), target));
                    }
                    break;
                case SWGlobalWarReturnAction.Revoke:
                    if (!_returnGrants.TryGetValue(command.Grant, out var grant) || grant.Faction != actor.Faction ||
                        grant.Command.Generation != command.Generation || grant.Command.Part != command.Part ||
                        grant.Command.Destination != command.Destination ||
                        grant.Command.DestinationStore != command.DestinationStore)
                        failure = SWGlobalWarReturnFailure.Denied;
                    else
                        _returnGrants[command.Grant] = grant with { Revoked = true };
                    break;
            }
        }
        result = new(command.Request, command.Action, failure == SWGlobalWarReturnFailure.None, failure, grantId, receipt);
        _returnRequests.Add(key, new(actor.Id, command, result));
        if (!result.Success)
            return false;
        _returnJournal.Add(new(command, actor.User, actor.Id, actor.Faction, Simulation!.Time));
        if (actor.Role == SWGlobalWarRole.Head && command.HeadDecision && _assignments.TryGetRegistry(out var registry))
            foreach (var minister in registry.Comp.Assignments.Values.Where(a => a.Faction == actor.Faction &&
                         a.Role == SWGlobalWarRole.LogisticsMinister))
                if (TryComp<ActorComponent>(minister.Body, out var player))
                    _economyChat.DispatchServerMessage(player.PlayerSession, Loc.GetString("sw-global-war-return-head"));
        return true;
    }

    private SWGlobalWarReturnTarget? ReturnTarget(SWGlobalWarReturnCommand command)
    {
        if (command.DestinationStore is not { } point ||
            !_supplyStores.TryGetValue(GetEntity(point), out var binding) ||
            Simulation!.Shipments.FirstOrDefault(s => s.Id == command.Part.Shipment)?.SupplyManifest is not { } manifest ||
            !_supplyPurchases.TryGetValue(manifest.Purchase, out var purchase))
            return null;
        return new(point, binding.Name, manifest.Owner == purchase.Request.Faction && AllowedSupplyArsenal(purchase, binding));
    }

    private static SWGlobalWarReturnReceiptView ReturnReceiptView(SWGlobalWarReturnCommand command,
        SWGlobalWarMarketReturnReceipt receipt, SWGlobalWarReturnTarget? target)
    {
        return new(command.Request, command.Part, command.Destination, receipt.Commodity, receipt.RequestedAt,
            receipt.ReceivedQuantity, receipt.CapturedQuantity, receipt.RedirectedQuantity, target);
    }

    private SWGlobalWarReturnResult RefreshReturnReceipt(ReturnRequestReceipt previous)
    {
        return previous.Result.Receipt != null && Simulation?.GetReturnReceipt(previous.Command.Request) is { } receipt
            ? previous.Result with { Receipt = ReturnReceiptView(previous.Command, receipt, previous.Result.Receipt.Target) } : previous.Result;
    }

    internal List<SWGlobalWarReturnResult> GetReturnResults(SWGlobalWarAssignment actor)
    {
        var own = _returnRequests.Where(p => p.Key.Item1 == actor.User && p.Value.Assignment == actor.Id &&
            HasEconomicOverview(actor)).Select(p => p.Value).ToArray();
        var recent = own.TakeLast(16).ToList();
        // Service commands cannot evict the last accepted transport receipt from the bounded projection.
        if (own.LastOrDefault(p => p.Result.Receipt != null) is { } last && !recent.Contains(last))
            recent.Add(last);
        return recent.Select(RefreshReturnReceipt).ToList();
    }

    internal List<SWGlobalWarReturnCandidate> GetReturnCandidates(SWGlobalWarAssignment actor, IEnumerable<Guid> visibleParts)
    {
        if (!HasEconomicOverview(actor) || Simulation == null)
            return [];
        var visible = visibleParts.ToHashSet();
        return Simulation.Shipments.Where(s => visible.Contains(s.Id) &&
                (s.SupplyManifest?.Owner ?? s.Batch?.Owner) == actor.Faction)
            .Select(s => new SWGlobalWarReturnCandidate(ReturnSelection(s), s.Commodity, s.HoldReason,
                s.ArrivesAt != null ? SWGlobalWarReturnFailure.InTransit :
                s.ReturnOperation != null && !s.ReturnCaptured && s.SupplyManifest?.Stored != true ? SWGlobalWarReturnFailure.AlreadyReturning :
                SWGlobalWarReturnFailure.None,
                s.SupplyManifest != null ? SupplyReturnDestinations(actor, s) :
                Simulation.Markets.Values.Where(m => m.Owner == actor.Faction).Select(m =>
                    new SWGlobalWarReturnDestination(m.Planet, Simulation.CheckReturn(s, new(actor.Generation,
                        Guid.Empty, s.Id, actor.Faction, m.Planet, s.Quantity)))).ToList())).ToList();
    }

    internal List<SWGlobalWarReturnGrantView> GetReturnGrants(SWGlobalWarAssignment actor)
    {
        return _returnGrants.Where(p => p.Value.Command.Generation == actor.Generation && p.Value.Faction == actor.Faction &&
                (HasLogisticsAuthority(actor, true) || p.Value.EmployeeAssignment == actor.Id) &&
                Simulation!.Shipments.Any(s => s.Id == p.Value.Command.Part.Shipment &&
                    (s.SupplyManifest?.Owner ?? s.Batch?.Owner) == actor.Faction))
            .Select(p => new SWGlobalWarReturnGrantView(p.Key, p.Value.Command.Part, p.Value.Command.Destination,
                _assignments.TryGetAssignment(p.Value.Command.Employee!.Value, out var employee) ? Name(employee.Body) : string.Empty,
                p.Value.Expires, ReturnGrantFailure(p.Value), p.Value.Command.DestinationStore, p.Value.Target)).ToList();
    }

    private sealed record ReturnGrant(SWGlobalWarReturnCommand Command, NetUserId Issuer, Guid IssuerAssignment,
        SWGlobalWarFaction Faction, Guid EmployeeAssignment, TimeSpan Expires, SWGlobalWarReturnTarget? Target,
        bool Revoked = false, bool Used = false);
    private sealed record ReturnRequestReceipt(Guid Assignment, SWGlobalWarReturnCommand Command, SWGlobalWarReturnResult Result);
}

public sealed record SWGlobalWarReturnJournalEntry(SWGlobalWarReturnCommand Command, NetUserId Author,
    Guid Assignment, SWGlobalWarFaction Faction, TimeSpan At);
