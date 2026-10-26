using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Server._SW._Senate.Economy.Simulation;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryDefenseSystem
{
    public List<SWMapCrisisEntry> BuildPublicMapCrises()
    {
        EnsureGroups();
        var entries = new List<SWMapCrisisEntry>();
        var restrictions = _incidents.Select(IncidentTransitRestriction).Where(r => r.HasValue)
            .Select(r => r!.Value).GroupBy(r => r.Edge).ToDictionary(g => g.Key, g => g.Min(r => r.Factor));
        foreach (var ev in _incidents.Where(e => !e.Resolved))
        {
            _worlds.TryGetValue(ev.WorldId, out var world);
            var sector = world?.SectorId ?? "";
            var harm = MinisterActive && sector.Length > 0 ? PlanetaryHarmFactor(ev.WorldId, ev.Kind) : 0;
            var security = -ev.Severity * .003f * harm;
            var defense = ev.Kind is SWDefenseEventKind.Incursion or SWDefenseEventKind.Blockade ? -ev.Severity * .002f * harm : 0;
            var entry = new SWMapCrisisEntry
            {
                Id = $"defense:{ev.Id}", Title = ev.Title, Description = IncidentTitle(ev.Kind),
                WorldId = ev.WorldId, EdgeId = ev.EdgeId, Severity = ev.Severity, Status = ev.Status,
                LocationLabel = world?.Name ?? ev.WorldId,
                SectorIds = sector.Length > 0 ? new() { sector } : new(),
                EffectsLabel = "Воздействие за игровой час",
                Consequences = MinisterActive ? $"По истечении угрозы: пограничное давление +{ev.Severity * .03f:0.###}, усталость от войны +{ev.Severity * .015f:0.###}."
                    : "Министр отсутствует: угроза деэскалирует без штрафов.",
            };
            if (security != 0 && _proto.TryIndex<SWGlobalStatPrototype>("SWGlobalSecurity", out var securityStat))
                entry.Effects.Add(new SWBillEffectEntry { StatName = securityStat.ShortName, Color = securityStat.Color, Delta = security });
            if (defense != 0 && _proto.TryIndex<SWGlobalStatPrototype>("SWGlobalDefense", out var defenseStat))
                entry.Effects.Add(new SWBillEffectEntry { StatName = defenseStat.ShortName, Color = defenseStat.Color, Delta = defense });
            if (IncidentTransitRestriction(ev) is { } restriction)
            {
                var link = _links.First(l => l.ID == restriction.Edge);
                var usage = _economy.Snapshot?.Edges.FirstOrDefault(e => e.Id == restriction.Edge);
                entry.Routes.Add(new SWMapCrisisRouteEntry
                {
                    EdgeId = restriction.Edge, CapacityMultiplier = restrictions[restriction.Edge],
                    BaseCapacity = link.CapacitySlotsPerHour,
                    HasUsage = usage != null, UsedSlotsPerHour = (usage?.UsedSlotsLastStep ?? 0) / RepublicEconomyModel.StepHours,
                });
            }
            entries.Add(entry);
        }
        return entries;
    }
}
