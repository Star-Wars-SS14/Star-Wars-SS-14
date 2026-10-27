using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryHealthSystem
{
    /// <summary>Read-only projection of the same health state used by the minister.</summary>
    public List<SWMapCrisisEntry> BuildPublicMapCrises()
    {
        var result = new List<SWMapCrisisEntry>();
        // Public maps need only incidents. Do not rebuild every world's program/material list per observer.
        foreach (var w in _worlds.Values.Where(w => w.Infection > 1f))
            result.Add(new SWMapCrisisEntry { Id = "health:outbreak:" + w.Map.Id, Title = "Очаг: " + w.Map.Name,
                WorldId = w.Map.Id, SectorIds = new() { w.Map.Sector!.Value.Id }, Status = "Очаг инфекции",
                LocationLabel = w.Map.Name, Description = $"Заражение {w.Infection:0}%; динамика {w.Trend:+0.0;-0.0;0}% в ч",
                Consequences = $"Охват клиник {_programs.GetHealthClinicCoverage(w.Map.Id) * 100:0}%; медицинская помощь {Care(w) * 100:0}%", Severity = w.Infection });
        foreach (var q in BuildQuarantines())
        foreach (var id in q.WorldIds)
        {
            var world = _worlds.GetValueOrDefault(id);
            result.Add(new SWMapCrisisEntry { Id = $"health:quarantine:{q.Id}:{id}", Title = q.Label,
                WorldId = id, SectorIds = world == null ? new() : new() { world.Map.Sector!.Value.Id },
                LocationLabel = world?.Map.Name ?? id, Status = q.Pending ? "Ожидает одобрения" : "Действующий карантин",
                Description = $"Срок: {SWSenateDuration.FormatHours(q.HoursLeft)}; гуманитарный коридор: {(q.Humanitarian ? "разрешён" : "закрыт")}",
                Consequences = q.Pending ? "Перевозки ещё не ограничены. " + q.Consequences : q.Consequences,
                Routes = q.Routes.Select(r => new SWMapCrisisRouteEntry { EdgeId = r.EdgeId,
                    CapacityMultiplier = r.CapacityMultiplier, BaseCapacity = r.BaseCapacity,
                    UsedSlotsPerHour = r.UsedSlotsPerHour, HasUsage = r.HasUsage }).ToList(),
                Severity = q.Pending ? 0 : 70 });
        }
        return result;
    }
}


