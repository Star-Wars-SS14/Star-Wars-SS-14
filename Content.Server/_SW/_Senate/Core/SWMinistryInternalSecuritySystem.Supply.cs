using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryInternalSecuritySystem
{
    private readonly Dictionary<string, string> _supplyProjects = new();
    private int _nextSupplyProject;
    public static int SupplyCost(SWSecurityOrder order) => order switch
    {
        SWSecurityOrder.Investigate => 10, SWSecurityOrder.Surveil => 5,
        SWSecurityOrder.Finish or SWSecurityOrder.Sabotage => 15, _ => 0,
    };
    public static int SupplyPenalty(int remaining) => (100 - Math.Clamp(remaining, 0, 100)) / 5;
    private bool CanSupply(string world, SecurityTeam team) => team.World == world && team.Supply < 100
        && _securityWorlds.TryGetValue(world, out var w) && w.Faction == "republic"
        && _economy.Definition?.Worlds.Any(x => x.Id == world && x.Active) == true
        && !_securityTeams.Any(t => t.Id != team.Id && t.World == world && t.Phase == SWSecurityTeamPhase.Supply)
        && (_supplyProjects.ContainsKey(world) || _supplyProjects.Count < 4);
    private bool BeginSupply(SecurityTeam team, string world)
    {
        if (!CanSupply(world, team)) return false;
        if (!_supplyProjects.ContainsKey(world))
        {
            var id = $"security-supply-{++_nextSupplyProject}";
            var request = new EconomyProjectRequest(id, world, new[] { new EconomyAmount("military_supplies", .15), new EconomyAmount("circuits", .05) }, .25);
            if (!_economy.QueueProject(request)) return false;
            _supplyProjects.Add(world, id);
        }
        team.Order = SWSecurityOrder.Resupply; team.Target = world; team.CaseId = 0;
        team.Started = 0; team.Until = 0; team.Next = null; team.Path.Clear(); team.Phase = SWSecurityTeamPhase.Supply;
        return true;
    }
    private void TickSupply(SecurityTeam team)
    {
        if (!_supplyProjects.TryGetValue(team.World, out var id)) { team.Phase = SWSecurityTeamPhase.Ready; return; }
        if (_economy.GetProject(id)?.Status != EconomyProjectStatus.Completed) return;
        _supplyProjects.Remove(team.World); team.Supply = 100; team.Phase = SWSecurityTeamPhase.Ready;
        team.Target = team.World; team.CaseId = 0; team.Started = 0; team.Until = 0;
        AddLog($"Группа {team.Id} получила снабжение.");
    }
    private void FillSupplyView(SecurityTeam team, SWSecurityTeamView view)
    {
        view.Supply = team.Supply;
        if (!_supplyProjects.TryGetValue(team.World, out var id)) return;
        view.SupplyProjectWorld = team.World;
        var project = _economy.GetProject(id);
        view.SupplyStatus = project?.Status switch
        {
            EconomyProjectStatus.Completed => "Комплект готов на планете",
            EconomyProjectStatus.Working => "Сборка комплекта",
            EconomyProjectStatus.Delivering => "Доставка комплекта",
            EconomyProjectStatus.WaitingMaterials when project.Materials.Any(m => m.Incoming > 0) => "Материалы в пути",
            _ => "Ожидание материалов",
        };
        if (project == null) return;
        view.SupplyMaterials = project.Materials.Select(m => new SWSecuritySupplyMaterialView { Good = m.Good,
            Name = m.Good == "military_supplies" ? "Военные припасы" : "Электроника", Required = m.Required,
            Delivered = m.Delivered, Incoming = m.Incoming, EtaHours = m.NextDeliveryHours is { } eta ? _time.ActiveGameHours + eta : -1 }).ToList();
    }
}
