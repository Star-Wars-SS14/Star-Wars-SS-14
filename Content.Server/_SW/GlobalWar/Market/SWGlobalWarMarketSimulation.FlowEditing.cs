using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    private readonly Dictionary<Guid, SWGlobalWarFlowEditReceipt> _flowEdits = new();

    /// <summary>
    /// Applies a server-authorized contract revision. Existing cargo, provenance and destination obligations
    /// remain unchanged. A new route or rate starts with no accumulated dispatch credit from the old contract.
    /// </summary>
    internal bool TryEditFlow(SWGlobalWarFlowEdit request, out SWGlobalWarFlowEditReceipt receipt)
    {
        receipt = new(request, false, SWGlobalWarEconomyFailure.InvalidRequest, null, null);
        if (request.Settings == null || string.IsNullOrWhiteSpace(request.Flow) ||
            string.IsNullOrWhiteSpace(request.Settings.Source.Id) ||
            string.IsNullOrWhiteSpace(request.Settings.Destination.Id) ||
            string.IsNullOrWhiteSpace(request.Settings.Commodity.Id) ||
            _advancing || Generation != request.Generation || Generation == null || request.Operation == Guid.Empty ||
            IsActive?.Invoke() == false)
            return false;
        if (_flowEdits.TryGetValue(request.Operation, out var previous))
        {
            if (previous.Request != request)
                return false;
            receipt = previous;
            return previous.Success;
        }
        var settings = request.Settings;
        var failure = SWGlobalWarEconomyFailure.None;
        _flows.TryGetValue(request.Flow, out var flow);
        string[]? route = null;
        if (string.IsNullOrWhiteSpace(request.Flow) || request.Flow.Length > 128 || !Enum.IsDefined(request.Priority) ||
            !_markets.TryGetValue(settings.Source.Id, out var source) ||
            !_markets.TryGetValue(settings.Destination.Id, out var destination))
            failure = SWGlobalWarEconomyFailure.InvalidRequest;
        else if (settings.Rate < 0)
            failure = SWGlobalWarEconomyFailure.NegativeRate;
        else if (settings.Source == settings.Destination)
            failure = SWGlobalWarEconomyFailure.SameNode;
        else if (!_commodities.ContainsKey(settings.Commodity.Id) ||
                 !source.Goods.ContainsKey(settings.Commodity.Id) || !destination.Goods.ContainsKey(settings.Commodity.Id))
            failure = SWGlobalWarEconomyFailure.UnknownCommodity;
        else if (flow == null ? request.ExpectedRevision != -1 : request.ExpectedRevision != flow.Revision)
            failure = SWGlobalWarEconomyFailure.Conflict;
        else if (flow != null && flow.ContractFaction != request.Faction)
            failure = SWGlobalWarEconomyFailure.FlowUnavailable;
        else if (!WithinNeutralOffer(request, flow))
            failure = SWGlobalWarEconomyFailure.OfferExceeded;
        else if ((route = FindRoute(settings.Source.Id, settings.Destination.Id,
                     _commodities[settings.Commodity.Id].CargoVolume, false)) == null)
            failure = SWGlobalWarEconomyFailure.NoRoute;
        if (failure != SWGlobalWarEconomyFailure.None)
        {
            receipt = receipt with { Failure = failure };
            _flowEdits.Add(request.Operation, receipt);
            return false;
        }

        var before = flow == null ? null : FlowView(flow);
        var newRecipient = flow == null || flow.Destination != settings.Destination.Id;
        if (flow == null)
        {
            flow = new SWGlobalWarMarketFlow(request.Flow, settings.Source.Id, settings.Destination.Id,
                settings.Commodity.Id, request.Priority);
            _flows.Add(flow.Id, flow);
            flow.ContractFaction = request.Faction;
        }
        flow.Source = settings.Source.Id;
        flow.Destination = settings.Destination.Id;
        if (newRecipient)
            flow.ExpectedRecipient = _markets[flow.Destination].Owner;
        flow.Commodity = settings.Commodity.Id;
        flow.Priority = request.Priority;
        flow.ExplicitRate = settings.Rate / (decimal) UnitsPerItem;
        flow.Order = settings.Order;
        flow.Enabled = settings.Enabled;
        flow.Route = route;
        flow.DispatchBudget = 0;
        flow.WaitReason = string.Empty;
        flow.Revision++;
        _flowLookupsStale = true;
        UpdateFlowRates();
        receipt = new(request, true, SWGlobalWarEconomyFailure.None, before, FlowView(flow));
        _flowEdits.Add(request.Operation, receipt);
        return true;
    }

    internal SWGlobalWarFlowView FlowView(SWGlobalWarMarketFlow flow, bool canEdit = false)
    {
        return new(flow.Id, flow.Revision,
            new(flow.Source, flow.Destination, flow.Commodity,
                (int) decimal.Min(int.MaxValue, decimal.Floor(DesiredFlowRate(flow) * UnitsPerItem)),
                flow.Order, flow.Enabled), flow.Priority, (double) flow.PerMinute, flow.WaitReason, canEdit,
            _markets[flow.Source].Owner == SWGlobalWarFaction.Neutral
                ? (double) NeutralOffer(flow.Source, flow.Commodity, flow.ContractFaction) : null, DesiredFlowRate(flow));
    }
}

public sealed record SWGlobalWarFlowEdit(Guid Generation, Guid Operation, string Flow, long ExpectedRevision,
    SWGlobalWarFlowSettings Settings, SWGlobalWarFlowPriority Priority,
    SWGlobalWarFaction Faction = SWGlobalWarFaction.Republic);

public sealed record SWGlobalWarFlowEditReceipt(SWGlobalWarFlowEdit Request, bool Success,
    SWGlobalWarEconomyFailure Failure, SWGlobalWarFlowView? Before, SWGlobalWarFlowView? After);
