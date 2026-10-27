using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryFinanceSystem
{
    // Accounting values, not market prices. Stable across rounds and independent of current output.
    private const double AssessmentScale = 0.6;
    private static double AssessmentValue(string category) => AssessmentScale * (category switch
    {
        "resources" => 100, "food" => 150, "materials" => 200, "energy" => 180,
        "components" => 300, "civil" => 250, "health" => 300, "defense" => 350,
        "culture" => 250, "controlled" => 350, _ => 200,
    });
    private readonly Dictionary<string, (string Name, string Category, double Value)> _taxGoods = new();
    private readonly Dictionary<string, string> _taxSectors = new();
    private readonly Dictionary<string, string> _taxCategories = new();
    private readonly Dictionary<string, int> _additionalTax = new();
    private readonly Dictionary<string, int> _relief = new();
    private readonly Dictionary<string, SWFinanceSectorRevenue> _revenue = new();
    private double _lastTaxHour = -1;
    private double _creditFraction;
    private int _taxDay = -1;
    private readonly Queue<(double Hour, double Tax)> _recentTax = new();
    private readonly Queue<Dictionary<string, (double Assessed, double Tax)>> _sectorTaxSteps = new();
    private double _firstTaxHour = -1;
    private double _recentTaxSum;
    public bool HasTaxHistory => _lastTaxHour - _firstTaxHour >= 2 && _firstTaxHour >= 0;
    public double RecentTaxEstimate => HasTaxHistory ? _recentTaxSum / Math.Min(4, _lastTaxHour - _firstTaxHour) * 14 : 0;
    public string TaxResultRequestId { get; private set; } = string.Empty;
    public string TaxResult { get; private set; } = string.Empty;

    private void InitializeTaxCatalog()
    {
        foreach (var good in _proto.EnumeratePrototypes<SWCommodityPrototype>())
            _taxGoods[good.ID] = (good.Name, good.Category.Id, AssessmentValue(good.Category.Id));
        foreach (var category in _proto.EnumeratePrototypes<SWCommodityCategoryPrototype>())
            _taxCategories[category.ID] = category.Name;
        var scenario = _proto.Index<SWRepublicEconomyScenarioPrototype>(SWRepublicEconomyFactory.ScenarioId);
        foreach (var world in _proto.Index(scenario.Map).Worlds)
            if (world.Sector is { } sector && _proto.TryIndex<SWPlanetEconomyPrototype>(world.Id, out var planet) && planet.ActiveEconomy)
                _taxSectors[world.Id] = sector.Id;
    }

    private void ResetTaxAccounting()
    {
        _additionalTax.Clear(); _relief.Clear(); _revenue.Clear();
        _lastTaxHour = -1; _creditFraction = 0; _taxDay = -1;
        _recentTax.Clear(); _sectorTaxSteps.Clear(); _recentTaxSum = 0; _firstTaxHour = -1;
        TaxResultRequestId = string.Empty; TaxResult = string.Empty;
    }

    public bool SetCommodityTax(string id, int rate, bool relief)
    {
        if (string.IsNullOrWhiteSpace(id) || rate < 0 || rate > 100 || (relief ? !_taxCategories.ContainsKey(id) : !_taxGoods.ContainsKey(id))) return false;
        (relief ? _relief : _additionalTax)[id] = rate;
        return true;
    }

    public void ReportTaxResult(string requestId, bool success)
    {
        TaxResultRequestId = requestId ?? string.Empty;
        TaxResult = success ? "Изменение применено" : "Изменение отклонено: проверьте товар и ставку";
    }

    public List<SWFinanceTaxEntry> TaxGoods() => _taxGoods.OrderBy(x => x.Value.Name)
        .Select(x => new SWFinanceTaxEntry { Id = x.Key, Name = x.Value.Name, Rate = _additionalTax.GetValueOrDefault(x.Key) }).ToList();
    public List<SWFinanceTaxEntry> TaxReliefs() => _taxCategories.OrderBy(x => x.Value)
        .Select(x => new SWFinanceTaxEntry { Id = x.Key, Name = x.Value, Rate = _relief.GetValueOrDefault(x.Key) }).ToList();
    public List<SWFinanceSectorRevenue> SectorRevenue() => _revenue.Values.OrderBy(x => x.Name).ToList();

    private void RefreshFinanceSituational()
    {
        if (!_budget.HasCurrentDay) return;
        var baseline = Math.Max(1, _budget.BaselinePublicBudget);
        var netRatio = (_budget.ConsolidatedPublicBalance - (double) CreditDebt) / baseline;
        var treasury = (float) (Math.Clamp(netRatio - 1, -1, 1) * 10);
        _senate.ReplaceSituationalContributions("ministry.finance.treasury", new[]
        {
            new SWSituationalContribution(null, "SWStatTreasury", treasury),
        });
        var inflation = Math.Clamp((_inflation - 45f) / 55f, 0f, 1f);
        _senate.ReplaceSituationalContributions("ministry.finance.inflation", new[]
        {
            new SWSituationalContribution(null, "SWStatLiving", -10f * inflation),
            new SWSituationalContribution(null, "SWStatMorale", -6f * inflation),
        });
    }

    // Called only when the worker publishes a newly accepted completed step.
    public void AcceptProductionSnapshot(EconomySnapshot snapshot, EconomyDefinition? definition, int day)
    {
        if (definition == null || snapshot.Hour <= _lastTaxHour || snapshot.Hour <= 0) return;
        _lastTaxHour = snapshot.Hour;
        if (_firstTaxHour < 0) _firstTaxHour = Math.Max(0, snapshot.Hour - SWRepublicEconomyRunner.StepHours);
        if (_taxDay != day) { _taxDay = day; _revenue.Clear(); }
        var tax = 0d;
        var sectorTax = new Dictionary<string, (double Assessed, double Tax)>();
        foreach (var activity in snapshot.Goods)
        {
            if (!_taxSectors.TryGetValue(activity.World, out var sector)
                || !_taxGoods.TryGetValue(activity.Good, out var good)) continue;
            if (!_revenue.TryGetValue(sector, out var row))
            {
                var name = _proto.TryIndex<SWSectorPrototype>(sector, out var proto) ? proto.Name : sector;
                _revenue[sector] = row = new SWFinanceSectorRevenue { SectorId = sector, Name = name };
            }
            if (activity.Required > activity.Consumed + 0.001)
            {
                var reason = $"{good.Name}: потребность {activity.Required:0.##}, обеспечено {activity.Consumed:0.##}";
                if (!row.Reasons.Contains(reason) && row.Reasons.Count < 5) row.Reasons.Add(reason);
            }
            if (activity.Produced <= 0) continue;
            var value = activity.Produced * good.Value;
            var baseRate = _taxLevel switch { 0 => 10, 2 => 30, _ => 20 };
            var rate = Math.Clamp(baseRate + _additionalTax.GetValueOrDefault(activity.Good) - _relief.GetValueOrDefault(good.Category), 0, 100);
            var collected = value * rate / 100d;
            var sectorTotals = sectorTax.GetValueOrDefault(sector);
            sectorTax[sector] = (sectorTotals.Assessed + value, sectorTotals.Tax + collected);
            row.ProducedValue += (float) value;
            row.Tax += (float) collected;
            tax += collected;
        }
        foreach (var production in snapshot.Production)
        {
            if (production.BaseCyclesPerHour <= 0 || production.CompletedCyclesLastStep > 0
                || !_taxSectors.TryGetValue(production.World, out var sector) || !_revenue.TryGetValue(sector, out var row)) continue;
            if (!row.Reasons.Contains("Есть остановленные производственные линии") && row.Reasons.Count < 5)
                row.Reasons.Add("Есть остановленные производственные линии");
        }
        _creditFraction += tax;
        _recentTax.Enqueue((snapshot.Hour, tax));
        _recentTaxSum += tax;
        while (_recentTax.TryPeek(out var old) && old.Hour <= snapshot.Hour - 4)
        {
            _recentTax.Dequeue();
            _recentTaxSum -= old.Tax;
        }
        var whole = (int) Math.Min(int.MaxValue, Math.Floor(_creditFraction));
        _creditFraction -= whole;
        _budget.CreditTreasury(whole);
        _sectorTaxSteps.Enqueue(sectorTax);
        while (_sectorTaxSteps.Count > 16) _sectorTaxSteps.Dequeue();
        var sectorWindow = new Dictionary<string, (double Assessed, double Tax)>();
        foreach (var step in _sectorTaxSteps)
            foreach (var (sector, values) in step)
            {
                var sum = sectorWindow.GetValueOrDefault(sector);
                sectorWindow[sector] = (sum.Assessed + values.Assessed, sum.Tax + values.Tax);
            }
        var taxEffects = sectorWindow.Where(x => x.Value.Assessed > 0).SelectMany(x =>
        {
            var effect = (float) -Math.Clamp((x.Value.Tax / x.Value.Assessed - 0.2) * 50, -10, 10);
            return new[] { new SWSituationalContribution(x.Key, "SWStatLiving", effect),
                new SWSituationalContribution(x.Key, "SWStatTaxes", effect) };
        }).ToArray();
        _senate.ReplaceSituationalContributions("ministry.finance.tax", taxEffects);
        RefreshFinanceSituational();
    }
}
