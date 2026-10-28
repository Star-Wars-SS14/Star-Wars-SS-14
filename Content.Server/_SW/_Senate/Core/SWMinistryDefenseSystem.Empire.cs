using System.Linq;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Core;

public readonly record struct SWEmpireRepublicFleetSnapshot(int Id, float Strength, float Damage, float Supply, float Training, float Effective);

public sealed partial class SWMinistryDefenseSystem
{
    private sealed record ImperialCrossing(string FleetId, string LinkId, double ReadyAt, bool Raid = false);
    private readonly Dictionary<int, ImperialCrossing> _imperialCrossings = new();
    private readonly Dictionary<int, string> _imperialBlockades = new();

    public List<SWEmpireRepublicFleetSnapshot> GetObservedBorderFleets(string linkId)
    {
        EnsureGroups();
        var border = _borders.FirstOrDefault(b => b.Id == linkId && b.Faction == "empire");
        if (border == null) return new();
        return _groups.Where(g => g.Alive && g.World == border.RepublicWorldId && g.Phase == SWDefenseOperationPhase.Standing)
            .Select(g => new SWEmpireRepublicFleetSnapshot(g.Id, g.Strength, g.Damage, g.Supply, g.Training, Effective(g))).ToList();
    }

    public bool TrySetImperialBlockade(int eventId, bool enabled)
    {
        if (!enabled) return SetImperialBlockade(eventId, string.Empty, false);
        var incident = _incidents.FirstOrDefault(e => e.Id == eventId && !e.Resolved);
        if (incident == null || !_imperialCrossings.ContainsKey(eventId)) return false;
        var link = _links.Where(l => l.Accessible && (l.From == incident.WorldId || l.To == incident.WorldId)
            && _worlds[l.From].Faction == "republic" && _worlds[l.To].Faction == "republic")
            .OrderBy(l => l.ID).FirstOrDefault();
        return link != null && SetImperialBlockade(eventId, link.ID, true);
    }

    public bool SetImperialBlockade(int eventId, string republicLinkId, bool enabled)
    {
        if (!HasImperialEngagement(eventId)) return false;
        if (!enabled) { _imperialBlockades.Remove(eventId); ApplyRouteRestrictions(); return true; }
        var incident = _incidents.First(e => e.Id == eventId);
        var link = _links.FirstOrDefault(l => l.ID == republicLinkId && l.Accessible && (l.From == incident.WorldId || l.To == incident.WorldId));
        if (link == null || _worlds[link.From].Faction != "republic" || _worlds[link.To].Faction != "republic") return false;
        _imperialBlockades[eventId] = republicLinkId; ApplyRouteRestrictions(); return true;
    }

    public bool TryStartImperialCrossing(string fleetId, string linkId, out int eventId, out string reason)
    {
        eventId = 0;
        reason = "Пограничная операция недоступна";
        EnsureGroups();
        if (string.IsNullOrWhiteSpace(fleetId) || fleetId.Length > 128 || !_time.ModeActive || !_time.DayActive || !MinisterActive || !ImperialThreatsEnabled)
            return false;
        var border = _borders.FirstOrDefault(b => b.Id == linkId && b.Faction == "empire");
        if (border == null || _imperialCrossings.Any(p => p.Value.FleetId == fleetId && _incidents.Any(e => e.Id == p.Key && !e.Resolved)))
            return false;
        if (!CreateIncident(border.RepublicWorldId, border.Id, SWDefenseEventKind.Incursion, 55, 1, 24, false, "Имперское пограничное вторжение"))
            return false;
        eventId = _nextEventId - 1;
        _imperialCrossings.Add(eventId, new ImperialCrossing(fleetId, linkId, _time.ActiveGameHours + .5));
        var createdId = eventId;
        var incident = _incidents.First(e => e.Id == createdId);
        incident.Status = "Пограничное предупреждение: подготовка ответа";
        incident.Risk = "Обнаружен имперский флот; исход зависит от реальных сил на границе.";
        reason = string.Empty;
        return true;
    }

    public bool TryStartImperialRaid(string fleetId, string worldId, out int eventId, out string reason)
    {
        eventId = 0; reason = "Рейд недоступен";
        EnsureGroups();
        if (string.IsNullOrWhiteSpace(fleetId) || fleetId.Length > 128 || !_time.ModeActive || !_time.DayActive || !MinisterActive || !ImperialThreatsEnabled ||
            !_worlds.TryGetValue(worldId, out var world) || world.Faction != "republic") return false;
        var existing = _incidents.FirstOrDefault(e => !e.Resolved && e.WorldId == worldId && _imperialCrossings.TryGetValue(e.Id, out var op) && op.Raid);
        if (existing != null) { eventId = existing.Id; reason = string.Empty; return true; }
        if (!CreateIncident(worldId, "", SWDefenseEventKind.Raid, 55, 1, 24, false, "Имперский рейд")) return false;
        eventId = _nextEventId - 1;
        var createdId = eventId;
        _imperialCrossings.Add(eventId, new ImperialCrossing(fleetId, "", _time.ActiveGameHours + .5, true));
        var incident = _incidents.First(e => e.Id == createdId);
        incident.Status = "Предупреждение: имперский флот у цели";
        incident.Risk = "Исход зависит от реальных сил на атакованном мире.";
        reason = string.Empty; return true;
    }

    public bool ImperialRaidReady(int eventId) => _imperialCrossings.TryGetValue(eventId, out var operation) && operation.Raid && ImperialCrossingReady(eventId);
    public bool FinishImperialRaid(int eventId, bool empireWon)
    {
        if (!_imperialCrossings.TryGetValue(eventId, out var operation) || !operation.Raid) return false;
        var incident = _incidents.FirstOrDefault(e => e.Id == eventId && !e.Resolved);
        if (incident == null || empireWon && (!MinisterActive || !ImperialRaidReady(eventId) || GetImperialDefenders(eventId).Count != 0)) return false;
        if (empireWon) { incident.Status = "Имперский флот проводит рейд"; return true; }
        ResolveIncident(incident, !empireWon);
        incident.Status = "Имперский рейд завершён";
        foreach (var group in _groups.Where(g => g.Alive && g.EventId == eventId).ToArray()) BeginReturn(group);
        return true;
    }

    public bool ImperialCrossingReady(int eventId) => HasImperialEngagement(eventId)
        && _time.ModeActive && _time.DayActive && _time.ActiveGameHours >= _imperialCrossings[eventId].ReadyAt;
    public double? ImperialCrossingReadyAt(int eventId) => HasImperialEngagement(eventId) ? _imperialCrossings[eventId].ReadyAt : null;
    public bool HasImperialEngagement(int eventId) => _imperialCrossings.ContainsKey(eventId)
        && _incidents.Any(e => e.Id == eventId && !e.Resolved);

    public List<SWEmpireRepublicFleetSnapshot> GetImperialDefenders(int eventId)
    {
        var incident = _incidents.FirstOrDefault(e => e.Id == eventId && !e.Resolved);
        if (incident == null || !_imperialCrossings.ContainsKey(eventId)) return new();
        return _groups.Where(g => g.Alive && g.World == incident.WorldId &&
            (g.EventId == eventId && g.Order == SWDefenseFleetOrder.Respond && g.Phase == SWDefenseOperationPhase.Execution ||
             (g.Order is SWDefenseFleetOrder.Border or SWDefenseFleetOrder.Guard or SWDefenseFleetOrder.Patrol) && g.Phase == SWDefenseOperationPhase.Standing))
            .Select(g => new SWEmpireRepublicFleetSnapshot(g.Id, g.Strength, g.Damage, g.Supply, g.Training, Effective(g))).ToList();
    }

    public bool ApplyImperialBattleDamage(int eventId, int republicFleetId, float strengthLoss, float damage, float supplyLoss)
    {
        if (!float.IsFinite(strengthLoss) || !float.IsFinite(damage) || !float.IsFinite(supplyLoss) ||
            strengthLoss < 0 || damage < 0 || supplyLoss < 0 || !GetImperialDefenders(eventId).Any(g => g.Id == republicFleetId)) return false;
        var group = _groups.First(g => g.Id == republicFleetId);
        group.Strength = Math.Max(0, group.Strength - strengthLoss);
        group.Damage = Math.Min(100, group.Damage + damage);
        group.Supply = Math.Max(0, group.Supply - supplyLoss);
        if (group.Strength <= 0 || group.Damage >= 100)
        { group.Strength = 0; group.Phase = SWDefenseOperationPhase.Lost; group.EventId = 0; group.Path.Clear(); group.Next = null; }
        else if (group.Strength < 25 || group.Damage > 75 || group.Supply < 10)
        { group.Retreated = true; BeginReturn(group); }
        return true;
    }

    public bool FinishImperialCrossing(int eventId, bool empireWon)
    {
        var incident = _incidents.FirstOrDefault(e => e.Id == eventId && !e.Resolved);
        if (incident == null || !_imperialCrossings.ContainsKey(eventId) ||
            (empireWon && (!MinisterActive || !ImperialCrossingReady(eventId) || GetImperialDefenders(eventId).Count != 0))) return false;
        if (empireWon)
        {
            incident.Status = "Имперский флот удерживает пограничный переход";
            return true;
        }
        ResolveIncident(incident, !empireWon);
        _imperialBlockades.Remove(eventId); ApplyRouteRestrictions();
        incident.Status = "Имперский флот отступил";
        foreach (var group in _groups.Where(g => g.Alive && g.EventId == eventId).ToArray()) BeginReturn(group);
        return true;
    }
}
