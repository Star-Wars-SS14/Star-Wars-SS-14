using System.Linq;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryDefenseSystem
{
    private static string DoctrineStat(string global) => global switch
    {
        "SWGlobalDefense" => "SWStatFleet", "SWGlobalSecurity" => "SWStatPolice",
        "SWGlobalEconomy" => "SWStatEmployment", _ => "SWStatMorale"
    };

    // Doctrine expresses policy direction; physical readiness and threats are separate.
    private void PublishDefenseConditions()
    {
        var doctrine = Config(_stance).Nudges.Select(n => new SWSituationalContribution(null,
            DoctrineStat(n.Stat), n.Delta * 6f)).ToArray();
        _senate.ReplaceSituationalContributions("ministry.defense.doctrine", doctrine);
        var effects = new Dictionary<(string Sector, string Stat), float>();
        void Add(string sector, string stat, float value)
        {
            if (sector.Length == 0) return;
            var key = (sector, stat); effects[key] = effects.GetValueOrDefault(key) + value;
        }
        foreach (var group in _groups.Where(g => g.Alive && g.Phase == SWDefenseOperationPhase.Standing
                     && g.Order is SWDefenseFleetOrder.Guard or SWDefenseFleetOrder.Patrol))
            if (_worlds.TryGetValue(group.World, out var world))
                Add(world.SectorId, "SWStatPolice", Math.Clamp(Effective(group) / 100f * 3f, 0, 3));
        foreach (var incident in _incidents.Where(e => !e.Resolved))
        {
            if (!_worlds.TryGetValue(incident.WorldId, out var world)) continue;
            var severity = Math.Clamp(incident.Severity / 100f * PlanetaryHarmFactor(incident.WorldId, incident.Kind), 0, 1);
            Add(world.SectorId, "SWStatPolice", -6f * severity);
            if (incident.Kind is SWDefenseEventKind.Incursion or SWDefenseEventKind.Blockade)
                Add(world.SectorId, "SWStatFleet", -4f * severity);
        }
        _senate.ReplaceSituationalContributions("ministry.defense.readiness", effects.Select(p =>
            new SWSituationalContribution(p.Key.Sector, p.Key.Stat, Math.Clamp(p.Value, -8f, 8f))).ToArray());
    }
}
