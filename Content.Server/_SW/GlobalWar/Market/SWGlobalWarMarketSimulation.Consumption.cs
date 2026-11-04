using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    // The owning system supplies a single composition boundary: money first, then the no-fail goods commit,
    // then notifications. A missing adapter must never silently consume campaign inventory without accounting.
    internal Func<SWGlobalWarConsumptionPlan, Action<bool>, bool>? SettleConsumption;
    internal readonly Dictionary<string, int> TaxRates = new();
    private readonly Dictionary<Guid, SWGlobalWarConsumptionReceipt> _consumptionReceipts = new();

    public int ConsumptionReceiptCount => _consumptionReceipts.Count;
    public int FailedConsumptionReceiptCount { get; private set; }

    private void StepCampaignConsumption()
    {
        if (SettleConsumption == null || Generation is not { } generation)
            return;
        var operation = Guid.NewGuid();
        var lines = new List<SWGlobalWarConsumptionLine>();
        foreach (var market in _markets.Values)
        foreach (var (commodity, good) in market.Goods)
        {
            good.ConsumptionPerMinute = (int) decimal.Floor(good.BaseConsumptionPerMinute * market.Population);
            good.CampaignConsumptionProgress += good.BaseConsumptionPerMinute * market.Population * StepDuration.Ticks;
            var required = (int) decimal.Floor(good.CampaignConsumptionProgress / TimeSpan.TicksPerMinute);
            good.CampaignConsumptionProgress -= (decimal) required * TimeSpan.TicksPerMinute;
            if (required == 0)
                continue;
            var quantity = Math.Min(good.Available, required);
            var reservation = Guid.NewGuid();
            if (quantity > 0)
            {
                var request = new SWGlobalWarStockRequest(generation, reservation, market.Planet, commodity,
                    quantity, market.Owner, "mandatory-consumption");
                if (!TryReserveStock(request, _consumptionReservations, out _))
                    throw new InvalidOperationException("Validated mandatory consumption could not reserve inventory.");
            }
            var taxMultiplier = market.CapturedAt is { } captured && Time - captured < TimeSpan.FromMinutes(10) ? 1 : 2;
            lines.Add(new SWGlobalWarConsumptionLine(market.Planet, commodity, reservation, quantity, required,
                market.Owner, TaxRates.GetValueOrDefault(commodity), taxMultiplier));
            if (commodity != "SWGlobalWarF" || market.Profile == null)
                continue;
            good.GrowthDemandPerMinute = (int) decimal.Floor(good.BaseConsumptionPerMinute * market.Population *
                                                           market.GrowthFoodFraction);
            good.GrowthProgress += good.BaseConsumptionPerMinute * market.Population * market.GrowthFoodFraction *
                                   StepDuration.Ticks;
            var extraRequired = (int) decimal.Floor(good.GrowthProgress / TimeSpan.TicksPerMinute);
            good.GrowthProgress -= (decimal) extraRequired * TimeSpan.TicksPerMinute;
            var extra = quantity == required ? Math.Min(good.Available, extraRequired) : 0;
            var extraReservation = Guid.NewGuid();
            if (extra > 0 && !TryReserveStock(new SWGlobalWarStockRequest(generation, extraReservation, market.Planet,
                    commodity, extra, market.Owner, "growth-consumption"), _consumptionReservations, out _))
                throw new InvalidOperationException("Validated growth consumption could not reserve inventory.");
            lines.Add(new SWGlobalWarConsumptionLine(market.Planet, commodity, extraReservation, extra,
                extraRequired, market.Owner, 0, 0, true));
        }
        if (lines.Count == 0)
            return;
        var plan = new SWGlobalWarConsumptionPlan(generation, operation, Time, lines.AsReadOnly());
        bool? completed = null;
        void Complete(bool success)
        {
            if (completed != null)
                return;
            foreach (var line in lines)
            {
                if (line.Quantity > 0)
                {
                    var changed = success ? TryCommitStock(generation, line.Reservation, _consumptionReservations, false, out _)
                        : TryCancelStock(generation, line.Reservation, _consumptionReservations);
                    if (!changed)
                        throw new InvalidOperationException("A prevalidated consumption reservation was invalidated during completion.");
                }
                var good = _markets[line.Planet].Goods[line.Commodity];
                var actual = success ? line.Quantity : 0;
                if (line.Growth)
                    good.GrowthConsumed += actual;
                else
                {
                    good.Consumed += actual;
                    good.UnmetConsumption += line.Required - actual;
                }
            }
            AdvanceDemography(lines, success);
            _consumptionReceipts.Add(operation, new SWGlobalWarConsumptionReceipt(plan, success));
            if (!success)
                FailedConsumptionReceiptCount++;
            // Only this private plan's finalized reservations expire here. External replay and audit receipts remain.
            foreach (var line in lines)
            {
                if (line.Quantity == 0)
                    continue;
                if (_consumptionReservations[line.Reservation].Status == SWGlobalWarStockReservationStatus.Pending)
                    throw new InvalidOperationException("A consumption reservation must finish before release.");
                _consumptionReservations.Remove(line.Reservation);
            }
            completed = success;
        }
        var result = SettleConsumption(plan, Complete);
        if (completed != result)
            throw new InvalidOperationException("The accounting adapter must complete both successful and refused goods accounting before publishing.");
    }

    private void AdvanceDemography(IReadOnlyList<SWGlobalWarConsumptionLine> lines, bool consumed)
    {
        foreach (var line in lines.Where(l => l.Commodity == "SWGlobalWarF"))
        {
            var market = _markets[line.Planet];
            if (line.Growth)
                market.MinuteGrowthConsumed += consumed ? line.Quantity : 0;
            else
            {
                market.MinuteFoodRequired += line.Required;
                market.MinuteFoodConsumed += consumed ? line.Quantity : 0;
            }
        }
        if (Time.Ticks % TimeSpan.TicksPerMinute != 0)
            return;
        foreach (var market in _markets.Values)
        {
            if (DemographyEnabled && market.Profile != null && market.MinuteFoodRequired > 0)
            {
                var satisfaction = Math.Clamp((decimal) market.MinuteFoodConsumed / market.MinuteFoodRequired, 0, 1);
                var extra = Math.Clamp(4m * market.MinuteGrowthConsumed / market.MinuteFoodRequired, 0, 1);
                var population = market.Population;
                var change = satisfaction >= 1
                    ? 0.02m * Math.Max(0, 1 - population) + 0.02m * extra * (1 - population / 1.5m)
                    : -0.02m * (1 - satisfaction) * population;
                market.Population = Math.Clamp(population + change, 0.5m, 1.5m);
            }
            market.MinuteFoodRequired = market.MinuteFoodConsumed = market.MinuteGrowthConsumed = 0;
        }
    }
}

/// <summary>Tax recipient and half-rate are captured at consumption time, not at notification or later ownership.</summary>
public sealed record SWGlobalWarConsumptionLine(string Planet, string Commodity, Guid Reservation, int Quantity,
    int Required, SWGlobalWarFaction TaxRecipient, int TaxRate, int TaxMultiplier, bool Growth = false);
public sealed record SWGlobalWarConsumptionPlan(Guid Generation, Guid Operation, TimeSpan At,
    IReadOnlyList<SWGlobalWarConsumptionLine> Lines);
public sealed record SWGlobalWarConsumptionReceipt(SWGlobalWarConsumptionPlan Plan, bool Success);
