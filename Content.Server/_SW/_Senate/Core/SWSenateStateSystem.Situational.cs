using System.Linq;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared._SW._Senate;

namespace Content.Server._SW._Senate.Core;

/// <summary>A raw contribution to one substat, either Republic-wide or specific to one sector.</summary>
public readonly record struct SWSituationalContribution(string? SectorId, string SubstatId, float Value);

/// <summary>One source's contribution visible in a sector query.</summary>
public readonly record struct SWSituationalContributionDetail(string Source, string? SectorId, float Value);

public sealed partial class SWSenateStateSystem
{
    public const double SituationalSubstatLimit = 15;
    private readonly Dictionary<string, Dictionary<(string? Sector, string Substat), float>> _situationalContributions = new();

    /// <summary>
    /// Atomically replaces one source's entire publication. An empty set clears that source.
    /// Invalid entries or duplicate keys reject the entire publication without changing it.
    /// </summary>
    public bool ReplaceSituationalContributions(string source, IReadOnlyCollection<SWSituationalContribution> contributions)
    {
        if (string.IsNullOrWhiteSpace(source) || contributions == null)
            return false;

        var next = new Dictionary<(string? Sector, string Substat), float>();
        foreach (var contribution in contributions)
        {
            if (string.IsNullOrWhiteSpace(contribution.SubstatId)
                || !_proto.HasIndex<SWStatPrototype>(contribution.SubstatId)
                || contribution.SectorId != null && (string.IsNullOrWhiteSpace(contribution.SectorId)
                    || !_proto.HasIndex<SWSectorPrototype>(contribution.SectorId))
                || !float.IsFinite(contribution.Value)
                || !next.TryAdd((contribution.SectorId, contribution.SubstatId), contribution.Value))
                return false;
        }

        foreach (var key in next.Where(pair => Math.Abs(pair.Value) < 0.001f).Select(pair => pair.Key).ToArray())
            next.Remove(key);

        if (next.Count == 0)
            _situationalContributions.Remove(source);
        else
            _situationalContributions[source] = next;
        return true;
    }

    public bool ClearSituationalContributions(string source) =>
        !string.IsNullOrWhiteSpace(source) && _situationalContributions.Remove(source);

    private void ClearAllSituationalContributions() => _situationalContributions.Clear();

    /// <summary>
    /// Republic-wide entries apply to every sector. A null sector queries only Republic-wide entries.
    /// Returned details are detached from the registry and sorted for stable presentation.
    /// </summary>
    public List<SWSituationalContributionDetail> GetSituationalBreakdown(string? sectorId, string substatId)
    {
        var result = new List<SWSituationalContributionDetail>();
        foreach (var (source, entries) in _situationalContributions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (entries.TryGetValue((null, substatId), out var global))
                result.Add(new SWSituationalContributionDetail(source, null, global));
            if (sectorId != null && entries.TryGetValue((sectorId, substatId), out var local))
                result.Add(new SWSituationalContributionDetail(source, sectorId, local));
        }
        return result;
    }

    /// <summary>Returns the unbounded raw sum; limits and presentation belong to the caller.</summary>
    public double GetSituationalSum(string? sectorId, string substatId)
    {
        var sum = 0d;
        foreach (var entries in _situationalContributions.Values)
        {
            if (entries.TryGetValue((null, substatId), out var global))
                sum += global;
            if (sectorId != null && entries.TryGetValue((sectorId, substatId), out var local))
                sum += local;
        }
        return sum;
    }

    /// <summary>Weight each bounded substat offset in its global category; baseline policy stays unchanged.</summary>
    public float GetSituationalGlobalDelta(string sectorId, string globalId)
    {
        double weighted = 0;
        double weights = 0;
        foreach (var stat in SubsOf(globalId))
        {
            weighted += AppliedSituationalDelta(stat, GetSituationalSum(sectorId, stat.ID)) * stat.Weight;
            weights += stat.Weight;
        }
        return weights > 0 ? (float) (weighted / weights) : 0f;
    }

    private double AppliedSituationalDelta(SWStatPrototype stat, double raw)
    {
        var baseline = _republicSubStats.GetValueOrDefault(stat.ID, stat.Default);
        return Math.Clamp(baseline + Math.Clamp(raw, -SituationalSubstatLimit, SituationalSubstatLimit), stat.Min, stat.Max) - baseline;
    }

    public List<SWSituationalStatView> BuildSituationalStats(string? sectorId)
    {
        var result = new List<SWSituationalStatView>();
        foreach (var stat in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            var details = GetSituationalBreakdown(sectorId, stat.ID);
            var weightTotal = _sectorWeight.Values.Sum();
            var applied = AppliedSituationalDelta(stat, GetSituationalSum(sectorId, stat.ID));
            var raw = GetSituationalSum(sectorId, stat.ID);
            if (sectorId == null && weightTotal > 0)
            {
                raw = 0;
                applied = 0;
                foreach (var (sector, weight) in _sectorWeight)
                {
                    var fraction = weight / weightTotal;
                    var sectorRaw = GetSituationalSum(sector, stat.ID);
                    raw += sectorRaw * fraction;
                    applied += AppliedSituationalDelta(stat, sectorRaw) * fraction;
                    foreach (var item in GetSituationalBreakdown(sector, stat.ID))
                        if (item.SectorId != null)
                            details.Add(item with { Value = item.Value * fraction });
                }
            }
            if (details.Count == 0) continue;
            var baseline = _republicSubStats.GetValueOrDefault(stat.ID, stat.Default);
            var view = new SWSituationalStatView { SubstatId = stat.ID, SubstatName = stat.Name,
                GlobalId = stat.Global.Id, SectorId = sectorId, BaseValue = baseline,
                RawTotal = raw, AppliedTotal = applied, EffectiveValue = baseline + applied };
            foreach (var detail in details)
                view.Sources.Add(new SWSituationalSourceView { Source = detail.Source, SourceName = SituationalSourceName(detail.Source),
                    SectorId = detail.SectorId,
                    SectorName = detail.SectorId != null && _proto.TryIndex<SWSectorPrototype>(detail.SectorId, out var sector) ? sector.Name : string.Empty,
                    RawValue = detail.Value });
            result.Add(view);
        }
        return result;
    }

    private string SituationalSourceName(string source) => source switch
    {
        "ministry.finance.treasury" => Loc.GetString("sw-situational-finance-treasury"),
        "ministry.finance.tax" => Loc.GetString("sw-situational-finance-tax"),
        "ministry.finance.inflation" => Loc.GetString("sw-situational-finance-inflation"),
        "ministry.finance.engagement" => Loc.GetString("sw-situational-finance-engagement"),
        "ministry.health.infection" => Loc.GetString("sw-situational-health-infection"),
        "ministry.defense.readiness" => Loc.GetString("sw-situational-defense-readiness"),
        "ministry.defense.doctrine" => Loc.GetString("sw-situational-defense-doctrine"),
        "ministry.defense.response" => Loc.GetString("sw-situational-defense-response"),
        "ministry.economy.production" => Loc.GetString("sw-situational-economy-production"),
        "ministry.economy.employment" => Loc.GetString("sw-situational-economy-employment"),
        "ministry.economy.trade" => Loc.GetString("sw-situational-economy-trade"),
        "ministry.economy.engagement" => Loc.GetString("sw-situational-economy-engagement"),
        _ => Loc.GetString("sw-situational-other"),
    };
}
