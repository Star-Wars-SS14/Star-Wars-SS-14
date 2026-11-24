using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    // Offer definitions are the original public allocations, independent of editable contract rows.
    private readonly List<(SWGlobalWarEconomyFlowPrototype Definition, SWGlobalWarFaction Faction)> _neutralOfferDefinitions = new();
    private readonly Dictionary<(string Source, string Commodity, SWGlobalWarFaction Faction), decimal> _neutralOfferBudgets = new();

    internal decimal NeutralOffer(string source, string commodity, SWGlobalWarFaction faction)
    {
        if (!_markets.TryGetValue(source, out var market) || market.Owner != SWGlobalWarFaction.Neutral ||
            faction == SWGlobalWarFaction.Neutral)
            return 0;
        return _neutralOfferDefinitions.Where(o => o.Definition.Source.Id == source &&
            o.Definition.Commodity.Id == commodity && o.Faction == faction).Sum(o =>
            DefaultFlowRate(source, o.Definition.Destination.Id, commodity, o.Definition.RateMode, o.Definition.Share));
    }

    private decimal DefaultFlowRate(string sourceId, string destinationId, string commodity,
        SWGlobalWarFlowRateMode mode, decimal share)
    {
        var source = _markets[sourceId];
        var destination = _markets[destinationId];
        return Math.Max(0, mode switch
        {
            SWGlobalWarFlowRateMode.Fixed => share,
            SWGlobalWarFlowRateMode.RecipientMandatory => MandatoryRate(destination, commodity) * share,
            SWGlobalWarFlowRateMode.RecipientGrowth => MandatoryRate(destination, commodity) * destination.GrowthFoodFraction * share,
            SWGlobalWarFlowRateMode.ProductionInput => ProductionInputRate(destination, commodity) * share,
            SWGlobalWarFlowRateMode.ProducerSurplus => Math.Max(0, MaximumProduction(source, commodity) -
                MandatoryRate(source, commodity)) * share,
            SWGlobalWarFlowRateMode.NeutralLuxury => Math.Min(3, Math.Max(0,
                MaximumProduction(source, commodity) - MandatoryRate(source, commodity) -
                _neutralOfferDefinitions.Where(o => o.Definition.Source.Id == sourceId &&
                    o.Definition.Commodity.Id == commodity && o.Faction == SWGlobalWarFaction.Neutral &&
                    o.Definition.RateMode == SWGlobalWarFlowRateMode.RecipientMandatory).Sum(o =>
                    MandatoryRate(_markets[o.Definition.Destination.Id], commodity) * o.Definition.Share)) * share),
            _ => 0,
        });
    }

    private decimal DesiredFlowRate(SWGlobalWarMarketFlow flow)
    {
        return flow.ExplicitRate ?? DefaultFlowRate(flow.Source, flow.Destination, flow.Commodity, flow.RateMode, JoinedShare(flow));
    }

    /// <summary>
    /// Both powers split a neutral's upkeep. Once it has joined one of them the other side's contracts stop, so the
    /// owner's own contracts share the whole requirement. Output contracts keep their share: the owner has no more
    /// of a claim on them than before, and a minister can raise them.
    /// </summary>
    private decimal JoinedShare(SWGlobalWarMarketFlow flow)
    {
        var destination = _markets[flow.Destination];
        if (destination.OriginalOwner != SWGlobalWarFaction.Neutral || destination.Owner == SWGlobalWarFaction.Neutral ||
            destination.Owner != flow.ContractFaction || flow.RateMode is not (SWGlobalWarFlowRateMode.RecipientMandatory or
                SWGlobalWarFlowRateMode.RecipientGrowth or SWGlobalWarFlowRateMode.ProductionInput))
            return flow.Share;
        var total = 0m;
        foreach (var other in FlowsTo(flow.Destination, flow.Commodity))
        {
            if (other.RateMode == flow.RateMode && other.ContractFaction == flow.ContractFaction)
                total += other.Share;
        }
        return total > 0 ? flow.Share / total : flow.Share;
    }

    internal decimal NeutralOrderedRate(string source, string commodity, SWGlobalWarFaction faction)
    {
        return _flows.Values.Where(f => f.Source == source && f.Commodity == commodity &&
            f.ContractFaction == faction && f.Enabled).Sum(DesiredFlowRate);
    }

    private bool FlowDispatchAuthorized(SWGlobalWarMarketFlow flow)
    {
        var source = _markets[flow.Source].Owner;
        var destination = _markets[flow.Destination].Owner;
        // Neutrals' arrangement between themselves: it goes on under one owner, as that owner's internal delivery.
        if (flow.ContractFaction == SWGlobalWarFaction.Neutral)
            return source == destination;
        // A neutral recipient that joined the contract's own side is no capture and needs no new consent.
        if (destination != flow.ExpectedRecipient &&
            (flow.ExpectedRecipient != SWGlobalWarFaction.Neutral || destination != flow.ContractFaction))
            return false;
        if (source == flow.ContractFaction)
            return true;
        return source == SWGlobalWarFaction.Neutral && destination == flow.ContractFaction &&
            NeutralOffer(flow.Source, flow.Commodity, flow.ContractFaction) > 0;
    }

    private bool WithinNeutralOffer(SWGlobalWarFlowEdit request, SWGlobalWarMarketFlow? previous)
    {
        var settings = request.Settings;
        if (_markets[settings.Source.Id].Owner != SWGlobalWarFaction.Neutral)
            return true;
        var before = NeutralOrderedRate(settings.Source.Id, settings.Commodity.Id, request.Faction);
        var after = before;
        if (previous is { Enabled: true } && previous.Source == settings.Source.Id &&
            previous.Commodity == settings.Commodity.Id && previous.ContractFaction == request.Faction)
            after -= DesiredFlowRate(previous);
        if (settings.Enabled)
            after += settings.Rate / (decimal) UnitsPerItem;
        // A natural reduction in output must not trap an over-subscribed plan: incremental reductions are legal.
        return after <= NeutralOffer(settings.Source.Id, settings.Commodity.Id, request.Faction) || after <= before;
    }

    private void RefillNeutralOffers()
    {
        foreach (var key in _neutralOfferDefinitions.Where(o => o.Faction != SWGlobalWarFaction.Neutral)
                     .Select(o => (Source: o.Definition.Source.Id, Commodity: o.Definition.Commodity.Id, o.Faction)).Distinct())
        {
            var rate = NeutralOffer(key.Source, key.Commodity, key.Faction);
            _neutralOfferBudgets[key] = Math.Min(rate * UnitsPerItem * TimeSpan.TicksPerMinute,
                _neutralOfferBudgets.GetValueOrDefault(key) + rate * UnitsPerItem * StepDuration.Ticks);
        }
    }

    /// <summary>One source's exports of one good, totalled per contract side in first-seen order.</summary>
    private decimal PlannedExports(string source, string commodity, bool onlyWithDemand)
    {
        Span<SWGlobalWarFaction> sides = stackalloc SWGlobalWarFaction[3];
        Span<decimal> rates = stackalloc decimal[3];
        var count = 0;
        foreach (var flow in FlowsFrom(source, commodity))
        {
            if (onlyWithDemand && RecipientUse(flow) <= 0 && FlowNeed(flow) <= 0)
                continue;
            var side = 0;
            while (side < count && sides[side] != flow.ContractFaction)
                side++;
            if (side == count)
            {
                count++;
                sides[side] = flow.ContractFaction;
                rates[side] = 0;
            }
            rates[side] += flow.PerMinute;
        }
        var total = 0m;
        for (var i = 0; i < count; i++)
        {
            total += _markets[source].Owner == SWGlobalWarFaction.Neutral && sides[i] != SWGlobalWarFaction.Neutral
                ? Math.Min(rates[i], NeutralOffer(source, commodity, sides[i])) : rates[i];
        }
        return total;
    }
}
