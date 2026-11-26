using System.Linq;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Consoles;

/// <summary>Public geography and own operations are visible; foreign infrastructure remains private.</summary>
public sealed class SWStrategicMapDisclosure
{
    private readonly HashSet<string> _hidden;

    public SWStrategicMapDisclosure(IPrototypeManager prototypes)
    {
        var map = prototypes.Index<SWGalacticMapPrototype>("SenateRepublic");
        _hidden = map.Worlds.Where(w => map.GetWorldFaction(w) == "empire").Select(w => w.Id).ToHashSet();
    }

    public string World(string id) => id;
    private bool Hidden(string id) => _hidden.Contains(id);
    public string Text(string text) => text;

    // These methods receive a newly built state. Never mutate the live incident/world records
    // which the Defense builder currently includes by reference.
    public SWDefenseMapState Defense(SWDefenseMapState state)
    {
        state.Worlds = state.Worlds.Select(w => new SWDefenseWorldView
        {
            Id = w.Id, Name = w.Name, SectorId = w.SectorId, Faction = w.Faction,
            Shipyard = !Hidden(w.Id) && w.Shipyard,
        }).ToList();
        var links = state.Links.Select(l => l.Id).ToHashSet();
        state.Events = state.Events.Where(e => !Hidden(e.WorldId) || e.Kind == SWDefenseEventKind.Raid)
            .Select(e => new SWDefenseEventView
            {
                Id = e.Id, Title = e.ImperialBlockade ? "Имперское вторжение и блокада" : Text(e.Title), WorldId = World(e.WorldId),
                EdgeId = links.Contains(e.EdgeId) ? e.EdgeId : string.Empty,
                Kind = e.Kind, Severity = e.Severity, EnemyStrength = e.EnemyStrength,
                ExpiresAt = e.ExpiresAt, Resolved = e.Resolved, DirectorCreated = e.DirectorCreated,
                ImperialCrossing = e.ImperialCrossing, ImperialRaid = e.ImperialRaid, ImperialContact = e.ImperialContact, ImperialBlockade = e.ImperialBlockade, ImperialPhase = e.ImperialPhase,
                Status = Text(e.Status), Risk = Text(e.Risk),
            }).ToList();
        var events = state.Events.Select(e => e.Id).ToHashSet();
        state.SupportTargets = state.SupportTargets.Where(o => events.Contains(o.EventId)).ToList();
        state.OrderOptions = state.OrderOptions.Where(o => o.EventId == 0 || events.Contains(o.EventId)).ToList();
        foreach (var option in state.OrderOptions)
        {
            option.ResolvedWorldId = World(option.ResolvedWorldId);
            option.Reason = Text(option.Reason);
        }
        foreach (var border in state.Borders)
        {
            border.ForeignWorldId = World(border.ForeignWorldId);
        }
        foreach (var fleet in state.Fleets)
        {
            fleet.HomeWorldId = World(fleet.HomeWorldId);
            fleet.CurrentWorldId = World(fleet.CurrentWorldId);
            fleet.NextWorldId = fleet.NextWorldId is { } next ? World(next) : null;
            fleet.TargetWorldId = World(fleet.TargetWorldId);
            fleet.Status = Text(fleet.Status);
            if (!links.Contains(fleet.PatrolEdgeId)) fleet.PatrolEdgeId = string.Empty;
        }
        state.Shipyards = state.Shipyards.Where(y => !Hidden(y.WorldId)).ToList();
        return state;
    }

    public SWSecurityMapState Security(SWSecurityMapState state)
    {
        foreach (var team in state.Teams)
        {
            team.WorldId = World(team.WorldId);
            team.NextWorldId = team.NextWorldId is { } next ? World(next) : null;
            team.TargetWorldId = World(team.TargetWorldId);
            team.SupplyProjectWorld = World(team.SupplyProjectWorld);
            team.Status = Text(team.Status);
        }
        foreach (var cell in state.Cells) cell.WorldId = World(cell.WorldId);
        foreach (var dossier in state.Cases)
        {
            dossier.WorldId = World(dossier.WorldId);
            dossier.Title = Text(dossier.Title);
            dossier.Status = Text(dossier.Status);
            dossier.Objective = Text(dossier.Objective);
            dossier.Briefing = Text(dossier.Briefing);
            dossier.Scene = Text(dossier.Scene);
            dossier.Clues = dossier.Clues.Select(Text).ToList();
        }
        foreach (var option in state.OrderOptions)
        {
            option.WorldId = World(option.WorldId);
            option.Reason = Text(option.Reason);
        }
        Sabotage(state.SabotageRequests);
        return state;
    }

    public void Sabotage(IEnumerable<SWSecuritySabotageRequestView> requests)
    {
        foreach (var request in requests)
        {
            request.WorldId = World(request.WorldId);
            request.WorldName = Text(request.WorldName);
            request.Title = Text(request.Title);
        }
    }

    public string ResolveSecurityTarget(SWSecurityMapState authoritative, int teamId, SWSecurityOrder order,
        string target, int caseId)
    {
        if (!Hidden(target)) return target.StartsWith("public-sector:", StringComparison.Ordinal) ? string.Empty : target;
        // Knowing a world's location does not authorize an operation against it.
        return authoritative.OrderOptions.FirstOrDefault(o => o.Available && o.TeamId == teamId && o.Order == order
            && o.CaseId == caseId && o.WorldId == target)?.WorldId ?? string.Empty;
    }
}
