using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Economy;

/// <summary>Demand inputs from existing Senate conditions, with no random growth or new currencies.</summary>
public static class SWRepublicNeedsPolicy
{
    public const double MaximumMedicalIncrease = .25;

    public static double MilitaryMultiplier(SWDefenseStance stance) => stance switch
    {
        SWDefenseStance.Peace => .8,
        SWDefenseStance.Vigilance => .9,
        SWDefenseStance.Alert => 1,
        SWDefenseStance.Mobilization => 1.15,
        SWDefenseStance.War => 1.25,
        _ => 1,
    };

    /// <summary>
    /// Rebuilt from base inputs, never from previous effective multipliers. Manual modifiers compose
    /// with current conditions; the existing administrator input limit remains 0..5 in total.
    /// The default Alert stance and zero infection reproduce the validated baseline exactly.
    /// </summary>
    public static EconomyDemandModifier[] BuildModifiers(
        IReadOnlyList<EconomyDemand> demands,
        IReadOnlyDictionary<string, double> infectionByWorld,
        SWDefenseStance stance,
        IReadOnlyList<EconomyDemandModifier> manual)
    {
        var overrides = manual.ToDictionary(m => (m.World, m.Group), m => m.Multiplier);
        var result = new List<EconomyDemandModifier>();
        foreach (var (world, group) in demands.Select(d => (d.World, d.Group)).Distinct())
        {
            if (group == "orders")
                continue;
            var infection = infectionByWorld.GetValueOrDefault(world);
            if (!double.IsFinite(infection))
                infection = 0;
            var automatic = group switch
            {
                "care_drugs" or "care_supplies" => 1 + MaximumMedicalIncrease * Math.Clamp(infection / 100, 0, 1),
                // Regeneration and special assignments follow injuries/operational load, not infections.
                // A peaceful stance does not erase their ordinary civilian clinical demand.
                "regenerative_care" or "special_medicine" => Math.Max(1, MilitaryMultiplier(stance)),
                "military" => MilitaryMultiplier(stance),
                _ => 1,
            };
            var multiplier = Math.Clamp(automatic * overrides.GetValueOrDefault((world, group), 1), 0, 5);
            if (Math.Abs(multiplier - 1) > 1e-9)
                result.Add(new EconomyDemandModifier(world, group, multiplier));
        }
        return result.ToArray();
    }
}

public sealed record EconomySupplyPenalty(string Sector, string Global, float Offset);

/// <summary>
/// Reversible target offsets, sampled only from completed economy steps. The Senate's existing
/// stat drift applies them; this class never accumulates permanent policy changes or edits stocks.
/// </summary>
public sealed class SWRepublicSupplyFeedback
{
    public const double HalfLifeHours = 1;
    private static readonly string[] EssentialGroups = { "water", "nutrition", "care_drugs", "care_supplies", "closed_habitats" };
    private readonly Dictionary<string, string> _worldSectors;
    private readonly Dictionary<string, double> _population;
    private readonly Dictionary<string, double> _sectorPopulation;
    private readonly Dictionary<(string World, string Group), double> _baseDemand;
    private readonly Dictionary<(string Sector, string Global), double> _offsets = new();
    private double _lastHour;
    public IReadOnlyList<EconomySupplyPenalty> Penalties { get; private set; } = Array.Empty<EconomySupplyPenalty>();

    public SWRepublicSupplyFeedback(IReadOnlyDictionary<string, string> worldSectors,
        IEnumerable<EconomyDemand>? demands = null, IReadOnlyDictionary<string, double>? population = null)
    {
        _worldSectors = new Dictionary<string, string>(worldSectors);
        _population = worldSectors.Keys.ToDictionary(w => w, w => population == null ? 1d
            : population.TryGetValue(w, out var value) && double.IsFinite(value) ? Math.Max(0, value) : 0);
        _sectorPopulation = _worldSectors.GroupBy(w => w.Value)
            .ToDictionary(g => g.Key, g => g.Sum(w => _population[w.Key]));
        _baseDemand = (demands ?? Array.Empty<EconomyDemand>()).Where(d => d.Group != "orders")
            .GroupBy(d => (d.World, d.Group)).ToDictionary(g => g.Key, g => g.Sum(d => d.Rate));
    }

    public void Reset()
    {
        _lastHour = 0;
        _offsets.Clear();
        Penalties = Array.Empty<EconomySupplyPenalty>();
    }

    /// <returns>True only when a new active-time sample was applied.</returns>
    public bool Update(EconomySnapshot snapshot, bool running)
    {
        if (!running || snapshot.Hour <= _lastHour)
            return false;
        // A missing sample is not evidence that the latest shortage lasted for the entire gap.
        var elapsed = Math.Min(snapshot.Hour - _lastHour, SWRepublicEconomyRunner.StepHours);
        _lastHour = snapshot.Hour;
        var fraction = 1 - Math.Pow(.5, elapsed / HalfLifeHours);
        var service = new Dictionary<(string Sector, string Group), (double Required, double Served)>();
        var worldService = new Dictionary<(string World, string Group), EconomyServiceState>();
        foreach (var item in snapshot.Services)
        {
            if (item.Group == "orders" || item.LastRequired <= 0
                || !_worldSectors.TryGetValue(item.World, out var sector))
                continue;
            var key = (sector, item.Group);
            worldService[(item.World, item.Group)] = item;
            var total = service.GetValueOrDefault(key);
            service[key] = (total.Required + item.LastRequired, total.Served + item.LastServed);
        }

        foreach (var sector in _worldSectors.Values.Distinct())
        {
            // Required demand already contains population and current condition multipliers.
            // Weight by that demand, not by world count or by mixing unlike goods together.
            double Shortage(string group)
            {
                var total = service.GetValueOrDefault((sector, group));
                return total.Required > 1e-9 ? Math.Clamp(1 - total.Served / total.Required, 0, 1) : 0;
            }

            // Optional services benefit only the population actually served. Their absence is
            // not a deficit, and a tiny outpost cannot supply a bonus to an entire constituency.
            double Coverage(string group, bool harmful = false, bool missing = false)
            {
                var sectorPopulation = _sectorPopulation.GetValueOrDefault(sector);
                if (sectorPopulation <= 0) return 0;
                var total = 0d;
                foreach (var (world, owner) in _worldSectors)
                {
                    if (owner != sector || !worldService.TryGetValue((world, group), out var current)
                        || current.LastRequired <= 0)
                        continue;
                    var baseline = _baseDemand.GetValueOrDefault((world, group)) * SWRepublicEconomyRunner.StepHours;
                    var reference = harmful && baseline > 0 ? baseline : Math.Max(baseline, current.LastRequired);
                    var served = Math.Clamp(current.LastServed / reference, 0, 1);
                    var contribution = missing ? 1 - Math.Clamp(current.LastServed / current.LastRequired, 0, 1) : served;
                    if (!harmful && !missing)
                        contribution *= EssentialCoverage(world);
                    total += _population[world] * contribution;
                }
                return Math.Clamp(total / sectorPopulation, 0, 1);
            }

            double EssentialCoverage(string world)
            {
                var result = 1d;
                foreach (var group in EssentialGroups)
                {
                    if (worldService.TryGetValue((world, group), out var value) && value.LastRequired > 0)
                        result = Math.Min(result, Math.Clamp(value.LastServed / value.LastRequired, 0, 1));
                }
                return result;
            }

            double AssetBenefit(string good)
            {
                var sectorPopulation = _sectorPopulation.GetValueOrDefault(sector);
                if (sectorPopulation <= 0) return 0;
                var total = 0d;
                foreach (var asset in snapshot.Assets)
                {
                    if (asset.Good != good || asset.Operational <= 0 || asset.EffectiveBonus <= 0
                        || !double.IsFinite(asset.EffectiveBonus)
                        || !_worldSectors.TryGetValue(asset.World, out var owner) || owner != sector)
                        continue;
                    // The core's commissioned-asset profile owns per-unit strength and caps.
                    // Use the same effective value as the asset card, never a second formula.
                    total += _population[asset.World] * asset.EffectiveBonus
                        * EssentialCoverage(asset.World);
                }
                return total / sectorPopulation;
            }

            double DietEffect()
            {
                var sectorPopulation = _sectorPopulation.GetValueOrDefault(sector);
                if (sectorPopulation <= 0) return 0;
                var total = 0d;
                foreach (var diet in snapshot.Diets)
                {
                    if (diet.Consumed <= 0 || !double.IsFinite(diet.Quality)
                        || !_worldSectors.TryGetValue(diet.World, out var owner) || owner != sector)
                        continue;
                    var quality = Math.Clamp(diet.Quality, 0, 1);
                    // The baseline menu was solved at quality >= 0.65. Score only the already
                    // consumed diet: no second food withdrawal or appetite created by this metric.
                    var effect = quality < .65 ? 2 * (quality - .65) / .65 : (quality - .65) / .35;
                    var fed = worldService.TryGetValue((diet.World, "nutrition"), out var nutrition) && nutrition.LastRequired > 0
                        ? Math.Clamp(nutrition.LastServed / nutrition.LastRequired, 0, 1) : 1;
                    total += _population[diet.World] * effect * (effect > 0 ? EssentialCoverage(diet.World) : fed);
                }
                return total / sectorPopulation;
            }

            var water = Shortage("water");
            var food = Shortage("nutrition");
            // Medical supplies and drugs are one atomic treatment bundle in the model.
            // Do not charge twice for the same untreated patient.
            var care = Math.Max(Shortage("care_drugs"), Shortage("care_supplies"));
            var household = Shortage("household");
            var maintenance = Shortage("maintenance");
            var energyCells = Shortage("other");
            var military = Shortage("military");
            var habitats = Coverage("closed_habitats", missing: true);
            var treatment = 3 * Coverage("regenerative_care") + 2 * Coverage("implant_care") + Coverage("special_medicine");
            var luxury = Coverage("luxury");
            // Harm follows consumed narcotics, never unmet demand for them.
            var shadow = Coverage("shadow", harmful: true);
            var diet = DietEffect();
            var medicalEquipment = AssetBenefit("medical_equipment");
            var education = AssetBenefit("education_kits") + AssetBenefit("lab_equipment");
            var entertainment = AssetBenefit("entertainment");
            var defenseAssets = AssetBenefit("military_ships") + AssetBenefit("shield_generators")
                + AssetBenefit("blasters") + AssetBenefit("armor");

            Smooth("SWGlobalHealthcare", Math.Clamp(treatment + medicalEquipment + diet - 8 * water - 6 * food - 12 * care - 6 * habitats - 3 * shadow, -20, 6));
            Smooth("SWGlobalSociety", Math.Clamp(3 * luxury + entertainment + diet - 8 * water - 10 * food - 8 * household - 4 * shadow, -20, 4));
            Smooth("SWGlobalIndustry", -Math.Min(12, 10 * maintenance + 4 * energyCells + 4 * habitats));
            Smooth("SWGlobalDefense", Math.Clamp(defenseAssets - 12 * military, -12, 4));
            Smooth("SWGlobalEducation", Math.Min(6, education));
            Smooth("SWGlobalSecurity", -3 * shadow);

            void Smooth(string global, double target)
            {
                var key = (sector, global);
                var previous = _offsets.GetValueOrDefault(key);
                var next = previous + (target - previous) * fraction;
                _offsets[key] = Math.Abs(next) < .001 ? 0 : next;
            }
        }

        // Include zero offsets so restored supply clears the previous target penalty.
        Penalties = Array.AsReadOnly(_offsets.Select(p => new EconomySupplyPenalty(p.Key.Sector, p.Key.Global, (float) p.Value)).ToArray());
        return true;
    }
}
