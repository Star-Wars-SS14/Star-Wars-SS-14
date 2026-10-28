using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Robust.Shared.Random;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryInternalSecuritySystem
{
    [Dependency] private readonly SWRepublicEconomySystem _economy = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    private const int SecurityDeploymentCost = 500;
    private const int SecurityRecoveryCost = 800;
    private const int SecuritySabotageCost = 1200;
    private const int SecurityReplacementCost = 1500;
    private readonly Dictionary<string, SWSecurityWorldView> _securityWorlds = new();
    private readonly List<SWSecurityLinkView> _securityLinks = new();
    private readonly List<SecurityTeam> _securityTeams = new();
    private readonly Dictionary<string, SWSecurityCellView> _securityCells = new();
    private readonly List<SWSecurityCaseView> _securityCases = new();
    private readonly Dictionary<(string, string, bool), List<string>?> _securityPathCache = new();
    private readonly Dictionary<string, (float Strength, double Until)> _securityMitigation = new();
    private readonly Dictionary<int, (int TeamId, string WorldId, bool Approved)> _sabotageApprovals = new();
    private int _teamsLost;
    private bool _securityInitialized;
    private int _nextSecurityCase = 1;
    private double _nextSecurityCaseAt;
    private int _domesticCasesCompleted;
    private int _imperialCasesCompleted;
    public int CompletedCases => _plotsBusted;
    public int DomesticCasesCompleted => _domesticCasesCompleted;
    public int ImperialCasesCompleted => _imperialCasesCompleted;
    public int ActiveCells => _securityCells.Values.Count(c => c.Deployed && !c.Burned);
    public bool MinisterActive => _ministry.IsHolderActive(SecurityMinistryId);

    private sealed class SecurityTeam
    {
        public int Id;
        public string World = string.Empty;
        public string Target = string.Empty;
        public string? Next;
        public Queue<string> Path = new();
        public SWSecurityOrder Order;
        public SWSecurityTeamPhase Phase;
        public int CaseId;
        public double Started;
        public double Until;
        public float RiskChance;
        public int Supply = 100;
    }

    private void ResetSecurityMap()
    {
        ResetSecurityStories();
        _supplyProjects.Clear(); _nextSupplyProject = 0;
        _securityInitialized = false;
        _securityWorlds.Clear(); _securityLinks.Clear(); _securityTeams.Clear();
        _securityCells.Clear(); _securityCases.Clear(); _securityPathCache.Clear(); _securityMitigation.Clear(); _sabotageApprovals.Clear(); _teamsLost = 0;
        _nextSecurityCase = 1; _nextSecurityCaseAt = 0;
        _domesticCasesCompleted = 0; _imperialCasesCompleted = 0;
        _senate.ReplaceSituationalContributions("ministry.security.posture", Array.Empty<SWSituationalContribution>());
        _senate.ReplaceSituationalContributions("ministry.security.field", Array.Empty<SWSituationalContribution>());
        _senate.ReplaceSituationalContributions("ministry.security.cases", Array.Empty<SWSituationalContribution>());
    }

    private void EnsureSecurityMap()
    {
        if (_securityInitialized) return;
        _securityInitialized = true;
        var map = _proto.Index<SWGalacticMapPrototype>("SenateRepublic");
        foreach (var world in map.Worlds)
        {
            var faction = map.GetWorldFaction(world);
            _securityWorlds[world.Id] = new SWSecurityWorldView { Id = world.Id, Name = world.Name,
                SectorId = world.Sector?.Id ?? string.Empty, Faction = faction };
            if (faction == "republic" && world.Sector != null)
                _securityCells[world.Id] = new SWSecurityCellView { WorldId = world.Id };
        }
        var main = map.Routes.Where(r => !r.Local && r.Corridor != null)
            .Select(r => SWTransitLinkPrototype.LinkId(r.From, r.To)).ToHashSet();
        foreach (var link in _proto.EnumeratePrototypes<SWTransitLinkPrototype>().OrderBy(l => l.ID))
        {
            if (!_securityWorlds.ContainsKey(link.From) || !_securityWorlds.ContainsKey(link.To)) continue;
            var major = main.Contains(link.ID);
            _securityLinks.Add(new SWSecurityLinkView { Id = link.ID, From = link.From, To = link.To,
                MainHyperlane = major, Traversable = link.Accessible,
                TravelHours = SWMinistryDefenseSystem.FleetCrossingHours(link.TravelHours, major) });
        }
        var homes = _securityWorlds.Values.Where(w => w.Faction == "republic" && w.SectorId.Length > 0)
            .OrderBy(w => w.Id).ToArray();
        for (var i = 0; i < 3 && homes.Length > 0; i++)
        {
            var home = homes[i * homes.Length / 3].Id;
            _securityTeams.Add(new SecurityTeam { Id = i + 1, World = home, Target = home });
        }
        _nextSecurityCaseAt = _time.ActiveGameHours + .5;
    }

    private List<string>? SecurityPath(string from, string to)
    {
        var imperial = _securityWorlds.TryGetValue(from, out var origin) && origin.Faction == "empire"
            || _securityWorlds.TryGetValue(to, out var destination) && destination.Faction == "empire";
        var key = (from, to, imperial);
        if (_securityPathCache.TryGetValue(key, out var cached)) return cached;
        var path = CalculateSecurityPath(from, to, imperial);
        _securityPathCache[key] = path;
        return path;
    }
    private List<string>? CalculateSecurityPath(string from, string to, bool imperialMission)
    {
        if (!_securityWorlds.ContainsKey(from) || !_securityWorlds.ContainsKey(to)) return null;
        var distance = new Dictionary<string, double> { [from] = 0 };
        var previous = new Dictionary<string, string>();
        var remaining = new HashSet<string>(_securityWorlds.Keys);
        while (remaining.Count > 0)
        {
            var current = remaining.Where(distance.ContainsKey).OrderBy(w => distance[w]).FirstOrDefault();
            if (current == null) break;
            if (current == to)
            {
                var result = new List<string>();
                while (current != from) { result.Add(current); current = previous[current]; }
                result.Reverse(); return result;
            }
            remaining.Remove(current);
            foreach (var link in _securityLinks.Where(l => l.From == current || l.To == current))
            {
                var next = link.From == current ? link.To : link.From;
                var imperial = _securityWorlds[current].Faction == "empire" || _securityWorlds[next].Faction == "empire";
                if (!remaining.Contains(next) || (!link.Traversable && !(imperialMission && imperial))
                    || (!imperialMission && _securityWorlds[next].Faction == "empire")) continue;
                var candidate = distance[current] + link.TravelHours;
                if (distance.TryGetValue(next, out var old) && old <= candidate) continue;
                distance[next] = candidate; previous[next] = current;
            }
        }
        return null;
    }

    private SWSecurityLinkView SecurityLink(string a, string b) => _securityLinks.First(l =>
        l.From == a && l.To == b || l.From == b && l.To == a);
    private double SecurityPathHours(string world, IEnumerable<string> path)
    {
        var total = 0d;
        foreach (var next in path) { total += SecurityLink(world, next).TravelHours; world = next; }
        return total;
    }
    private void StartSecurityLeg(SecurityTeam team)
    {
        if (team.Path.Count == 0) { ArriveSecurity(team); return; }
        team.Next = team.Path.Dequeue(); team.Started = _time.ActiveGameHours;
        team.Until = team.Started + SecurityLink(team.World, team.Next).TravelHours;
        team.Phase = SWSecurityTeamPhase.Travel;
    }
    private void ArriveSecurity(SecurityTeam team)
    {
        team.Next = null;
        if (team.Order is SWSecurityOrder.Move or SWSecurityOrder.Withdraw)
        { team.Phase = SWSecurityTeamPhase.Ready; team.CaseId = 0; return; }
        team.Phase = SWSecurityTeamPhase.Work;
        team.Until = _time.ActiveGameHours + WorkDuration(team.Order, team.World, _securityCases.FirstOrDefault(c => c.Id == team.CaseId), team.Supply);
    }

    private double WorkDuration(SWSecurityOrder order, string worldId, SWSecurityCaseView? dossier, int supply)
    {
        if (order is SWSecurityOrder.Move or SWSecurityOrder.Withdraw) return 0;
        if (order is SWSecurityOrder.Recover or SWSecurityOrder.Replace) return 1;
        var duration = order == SWSecurityOrder.Surveil || order == SWSecurityOrder.Finish && dossier?.Kind == SWSecurityCaseKind.Imperial
            ? .5 : _securityCells.TryGetValue(worldId, out var cell) && cell.Deployed && !cell.Burned ? .5 : 1;
        return order is SWSecurityOrder.Investigate or SWSecurityOrder.Surveil
            ? duration * (1 + (100 - Math.Clamp(supply, 0, 100)) / 200d) : duration;
    }

    public SWSecurityMapState BuildMapState()
    {
        EnsureSecurityMap();
        var state = new SWSecurityMapState { NowHours = _time.ActiveGameHours, MinisterActive = MinisterActive,
            ClockRunning = _time.ModeActive && _time.DayActive, RealSecondsPerHour = SWSenateTimeSystem.RealSecondsPerGameHour, Budget = _dilemma.GetMinistryBudget(SecurityMinistryId),
            DeploymentCost = SecurityDeploymentCost, RecoveryCost = SecurityRecoveryCost,
            SabotageCost = SecuritySabotageCost, ReplacementCost = SecurityReplacementCost, TeamsLost = _teamsLost, SabotageRequests = BuildSabotageRequests(), CompletedCases = CompletedCases, CellCap = CellCap,
            Worlds = _securityWorlds.Values.Select(w => new SWSecurityWorldView { Id = w.Id, Name = w.Name,
                SectorId = w.SectorId, Faction = w.Faction }).ToList(),
            Links = _securityLinks.Select(l => new SWSecurityLinkView { Id = l.Id, From = l.From, To = l.To,
                TravelHours = l.TravelHours, MainHyperlane = l.MainHyperlane, Traversable = l.Traversable }).ToList(),
            Cells = _securityCells.Values.Select(c => new SWSecurityCellView { WorldId = c.WorldId,
                Deployed = c.Deployed, Burned = c.Burned, ReadyAt = c.ReadyAt }).ToList(),
            Cases = _securityCases.Select(c => new SWSecurityCaseView { Id = c.Id, Title = c.Title, Kind = c.Kind,
                WorldId = c.WorldId, Evidence = c.Evidence, RequiredEvidence = c.RequiredEvidence, ReconComplete = c.ReconComplete,
                Briefing = c.Briefing, Scene = c.Scene, DirectorCreated = c.DirectorCreated, Status = c.Status, Risk = c.Risk, Objective = c.Objective, Clues = new(c.Clues),
                SabotageApprovalPending = _sabotageApprovals.TryGetValue(c.Id, out var request) && !request.Approved,
                SabotageApproved = _sabotageApprovals.TryGetValue(c.Id, out var approval) && approval.Approved }).ToList() };
        foreach (var cell in state.Cells)
        {
            cell.CanDeploy = !cell.Deployed && !cell.Burned && _securityCells.Values.Count(c => c.Deployed) < CellCap && MinisterActive
                && state.ClockRunning && state.Budget >= SecurityDeploymentCost;
            cell.Reason = cell.Burned ? "Прикрытие раскрыто: требуется восстановление" : cell.Deployed ? "Ячейка развёрнута"
                : _securityCells.Values.Count(c => c.Deployed) >= CellCap ? "Достигнут предел сети" : state.Budget < SecurityDeploymentCost ? "Недостаточно бюджета"
                : !MinisterActive || !state.ClockRunning ? "Ведомство не работает" : string.Empty;
        }
        foreach (var t in _securityTeams)
        {
            if (t.Phase == SWSecurityTeamPhase.Lost)
                state.OrderOptions.Add(SecurityOption(t, SWSecurityOrder.Replace, t.World, 0));
            foreach (var dossier in _securityCases)
            foreach (var order in new[] { SWSecurityOrder.Investigate, SWSecurityOrder.Surveil, SWSecurityOrder.Finish, SWSecurityOrder.Sabotage })
                state.OrderOptions.Add(SecurityOption(t, order, dossier.WorldId, dossier.Id));
            foreach (var world in _securityWorlds.Values.Where(w => w.Faction == "republic"))
            {
                state.OrderOptions.Add(SecurityOption(t, SWSecurityOrder.Resupply, world.Id, 0));
                state.OrderOptions.Add(SecurityOption(t, SWSecurityOrder.Move, world.Id, 0));
                state.OrderOptions.Add(SecurityOption(t, SWSecurityOrder.Withdraw, world.Id, 0));
                if (_securityCells.TryGetValue(world.Id, out var mapCell) && mapCell.Burned) state.OrderOptions.Add(SecurityOption(t, SWSecurityOrder.Recover, world.Id, 0));
            }
            var teamView = new SWSecurityTeamView { Id = t.Id, Name = $"Группа {t.Id}", WorldId = t.World,
                NextWorldId = t.Next, TargetWorldId = t.Target, Phase = t.Phase, Order = t.Order, CaseId = t.CaseId,
                LegStartedAt = t.Started, EtaHours = t.Until,
                DestinationEtaHours = t.Phase == SWSecurityTeamPhase.Travel
                    ? t.Until + SecurityPathHours(t.Next!, t.Path) : t.Until,
                Status = t.Phase switch { SWSecurityTeamPhase.Travel => "В пути", SWSecurityTeamPhase.Work => "Работает",
                    SWSecurityTeamPhase.Recovery => "Восстановление", SWSecurityTeamPhase.Lost => "Группа потеряна", SWSecurityTeamPhase.Supply => "Ожидает снабжение", _ => "Готова" } };
            FillSupplyView(t, teamView); state.Teams.Add(teamView);
        }
        return state;
    }

    private SWSecurityOrderOptionView SecurityOption(SecurityTeam team, SWSecurityOrder order, string worldId, int caseId)
    {
        var option = new SWSecurityOrderOptionView { TeamId = team.Id, Order = order, WorldId = worldId, CaseId = caseId,
            Cost = order == SWSecurityOrder.Sabotage ? SecuritySabotageCost : order == SWSecurityOrder.Recover ? SecurityRecoveryCost : order == SWSecurityOrder.Replace ? SecurityReplacementCost : 0,
            WorkHours = WorkDuration(order, worldId, _securityCases.FirstOrDefault(c => c.Id == caseId), team.Supply),
            SupplyCost = SupplyCost(order),
            Risk = order is SWSecurityOrder.Finish or SWSecurityOrder.Sabotage ? "Риск раскрытия 10–25%; возможно восстановление" : "Низкий" };
        var dossier = _securityCases.FirstOrDefault(c => c.Id == caseId);
        if ((order is SWSecurityOrder.Finish or SWSecurityOrder.Sabotage) && !(order == SWSecurityOrder.Finish && dossier?.Kind == SWSecurityCaseKind.Imperial))
        {
            var covered = _securityCells.TryGetValue(worldId, out var riskCell) && riskCell.Deployed && !riskCell.Burned;
            var chance = Math.Max(5, (covered ? 10 : 25) - (dossier?.ReconComplete == true ? 10 : 0) - (int) (Mitigation(worldId) * 10));
            option.BaseRiskPercent = chance;
            option.SupplyPenaltyPercent = SupplyPenalty(team.Supply);
            option.LaunchRiskPercent = Math.Min(95, chance + option.SupplyPenaltyPercent);
            chance = option.LaunchRiskPercent;
            option.Risk = $"Вероятность провала и безвозвратной потери группы: {chance}%; улики сохранятся";
        }
        if (order == SWSecurityOrder.Finish && dossier?.Kind == SWSecurityCaseKind.Imperial)
        { option.Risk = "Безопасное извлечение разведданных; временное усиление безопасности ближайшего сектора"; }
        var path = SecurityPath(team.World, worldId);
        if (!_time.ModeActive || !_time.DayActive || !MinisterActive) option.Reason = "Ведомство не работает";
        else if (team.Phase != SWSecurityTeamPhase.Ready && !(order == SWSecurityOrder.Withdraw && team.Phase is SWSecurityTeamPhase.Work or SWSecurityTeamPhase.Supply) && !(order == SWSecurityOrder.Replace && team.Phase == SWSecurityTeamPhase.Lost)) option.Reason = "Группа занята";
        else if (order == SWSecurityOrder.Sabotage && dossier != null && _sabotageApprovals.TryGetValue(dossier.Id, out var approval)
            && (approval.TeamId != team.Id || approval.WorldId != worldId)) option.Reason = "Запрос связан с другой группой";
        else if (order == SWSecurityOrder.Sabotage && dossier != null && _sabotageApprovals.TryGetValue(dossier.Id, out var pending) && !pending.Approved) option.Reason = "Ожидается решение канцлера";
        else if (path == null && order != SWSecurityOrder.Replace) option.Reason = "Маршрут недоступен";
        else if (order == SWSecurityOrder.Resupply && !CanSupply(worldId, team)) option.Reason = "Снабжение недоступно на этой планете";
        else if (order == SWSecurityOrder.Recover && (team.World != worldId || !_securityCells.TryGetValue(worldId, out var cell) || !cell.Burned)) option.Reason = "Группа должна находиться у раскрытой ячейки";
        else if (caseId != 0 && _securityTeams.Any(t => t.Id != team.Id && t.CaseId == caseId)) option.Reason = "Делом занимается другая группа";
        else if (order == SWSecurityOrder.Surveil && dossier != null && dossier.ReconComplete) option.Reason = "Наблюдение уже проведено";
        else if (order == SWSecurityOrder.Investigate && dossier != null && dossier.Evidence >= dossier.RequiredEvidence) option.Reason = "Улики уже собраны";
        else if (order == SWSecurityOrder.Finish && (dossier == null || dossier.Evidence < dossier.RequiredEvidence)) option.Reason = "Нужно собрать улики";
        else if (order == SWSecurityOrder.Sabotage && (dossier == null || dossier.Kind != SWSecurityCaseKind.Imperial || dossier.Evidence < dossier.RequiredEvidence)) option.Reason = "Нужны улики по имперскому делу";
        else if (option.Cost > _dilemma.GetMinistryBudget(SecurityMinistryId)) option.Reason = "Недостаточно бюджета";
        else option.Available = true;
        if (path != null) option.TravelHours = SecurityPathHours(team.World, path);
        return option;
    }

    public bool SetCell(string worldId, bool deploy)
    {
        EnsureSecurityMap();
        if (!_time.ModeActive || !_time.DayActive || !MinisterActive || !_securityCells.TryGetValue(worldId, out var cell)
            || cell.Deployed == deploy || cell.Burned) return false;
        if (deploy && _securityCells.Values.Count(c => c.Deployed) >= CellCap) return false;
        if (deploy && !_dilemma.SpendMinistry(SecurityMinistryId, SecurityDeploymentCost)) return false;
        cell.Deployed = deploy; return true;
    }

    public bool ExecuteOrder(int teamId, SWSecurityOrder order, string worldId, int caseId = 0)
    {
        EnsureSecurityMap();
        if (!_time.ModeActive || !_time.DayActive || !MinisterActive || !_securityWorlds.TryGetValue(worldId, out var world)) return false;
        var team = _securityTeams.FirstOrDefault(t => t.Id == teamId);
        if (team == null) return false;
        if (order == SWSecurityOrder.Replace)
        {
            if (team.Phase != SWSecurityTeamPhase.Lost) return false;
            var home = _securityWorlds.Values.FirstOrDefault(w => w.Faction == "republic" && w.SectorId.Length > 0);
            if (home == null || !_dilemma.SpendMinistry(SecurityMinistryId, SecurityReplacementCost)) return false;
            team.Supply = 35;
            team.World = home.Id; team.Target = home.Id; team.Next = null; team.Path.Clear();
            team.Phase = SWSecurityTeamPhase.Recovery; team.Until = _time.ActiveGameHours + 1;
            AddLog($"Группа {team.Id}: замена набирается, готовность через час."); return true;
        }
        if (team.Phase != SWSecurityTeamPhase.Ready && !(order == SWSecurityOrder.Withdraw && team.Phase is SWSecurityTeamPhase.Work or SWSecurityTeamPhase.Supply)) return false;
        if (order == SWSecurityOrder.Resupply) return BeginSupply(team, worldId);
        if (order is not (SWSecurityOrder.Move or SWSecurityOrder.Investigate or SWSecurityOrder.Surveil
            or SWSecurityOrder.Finish or SWSecurityOrder.Withdraw or SWSecurityOrder.Sabotage or SWSecurityOrder.Recover)) return false;
        if (order == SWSecurityOrder.Recover)
        {
            if (!_securityCells.TryGetValue(worldId, out var cell) || !cell.Burned || team.World != worldId
                || !_dilemma.SpendMinistry(SecurityMinistryId, SecurityRecoveryCost)) return false;
            cell.Burned = false; cell.ReadyAt = 0; team.Phase = SWSecurityTeamPhase.Recovery;
            team.Until = _time.ActiveGameHours + 1; return true;
        }
        var investigation = order is SWSecurityOrder.Investigate or SWSecurityOrder.Surveil or SWSecurityOrder.Finish or SWSecurityOrder.Sabotage;
        var dossier = _securityCases.FirstOrDefault(c => c.Id == caseId);
        if (investigation && (dossier == null || dossier.WorldId != worldId
            || _securityTeams.Any(t => t.Id != teamId && t.CaseId == caseId))) return false;
        if (order == SWSecurityOrder.Investigate && dossier!.Evidence >= dossier.RequiredEvidence) return false;
        if (order == SWSecurityOrder.Surveil && dossier!.ReconComplete) return false;
        if (order == SWSecurityOrder.Finish && dossier!.Evidence < dossier.RequiredEvidence) return false;
        if (order == SWSecurityOrder.Sabotage && (dossier!.Kind != SWSecurityCaseKind.Imperial || dossier.Evidence < dossier.RequiredEvidence)) return false;
        if (order == SWSecurityOrder.Move && world.Faction != "republic") return false;
        if (order == SWSecurityOrder.Withdraw && world.Faction != "republic") return false;
        var path = SecurityPath(team.World, worldId);
        if (path == null) return false;
        if (order == SWSecurityOrder.Sabotage)
        {
            if (!_sabotageApprovals.TryGetValue(caseId, out var approval))
            {
                _sabotageApprovals[caseId] = (team.Id, worldId, _senate.Chancellor == null);
                AddLog(_senate.Chancellor == null ? $"{dossier!.Title}: диверсия автоматически одобрена; требуется запуск."
                    : $"{dossier!.Title}: запрошено одобрение канцлера.");
                return true;
            }
            if (!approval.Approved || approval.TeamId != team.Id || approval.WorldId != worldId) return false;
            if (!_dilemma.SpendMinistry(SecurityMinistryId, SecuritySabotageCost)) return false;
            _sabotageApprovals.Remove(caseId);
        }
        if (order is SWSecurityOrder.Finish or SWSecurityOrder.Sabotage)
        {
            var covered = _securityCells.TryGetValue(worldId, out var cell) && cell.Deployed && !cell.Burned;
            team.RiskChance = Math.Min(95, Math.Max(5, (covered ? 10 : 25) - (dossier!.ReconComplete ? 10 : 0) - (int) (Mitigation(worldId) * 10)) + SupplyPenalty(team.Supply)) / 100f;
        }
        if (team.Phase == SWSecurityTeamPhase.Supply) AddLog($"Заказ снабжения остаётся на планете {_securityWorlds[team.World].Name}.");
        team.Order = order; team.CaseId = investigation ? caseId : 0; team.Target = worldId;
        team.Path = new Queue<string>(path); StartSecurityLeg(team); return true;
    }

    public List<SWSecuritySabotageRequestView> BuildSabotageRequests()
    {
        var result = new List<SWSecuritySabotageRequestView>();
        foreach (var (id, approval) in _sabotageApprovals)
        {
            if (approval.Approved) continue;
            var dossier = _securityCases.FirstOrDefault(c => c.Id == id && c.WorldId == approval.WorldId);
            var team = _securityTeams.FirstOrDefault(t => t.Id == approval.TeamId);
            if (dossier == null || team == null) continue;
            var option = SecurityOption(team, SWSecurityOrder.Sabotage, approval.WorldId, id);
            result.Add(new SWSecuritySabotageRequestView { CaseId = id, TeamId = approval.TeamId,
                Title = dossier.Title, WorldId = approval.WorldId, WorldName = _securityWorlds[approval.WorldId].Name, Cost = SecuritySabotageCost, Risk = option.Risk });
        }
        return result;
    }
    public bool ApproveSabotage(int caseId)
    {
        if (!_time.ModeActive || !_time.DayActive || !_sabotageApprovals.TryGetValue(caseId, out var request) || request.Approved
            || !_securityCases.Any(c => c.Id == caseId && c.WorldId == request.WorldId)) return false;
        _sabotageApprovals[caseId] = (request.TeamId, request.WorldId, true);
        AddLog("Канцлер одобрил диверсию; группа может начать операцию."); return true;
    }
    public bool RejectSabotage(int caseId)
    {
        if (!_time.ModeActive || !_time.DayActive || !_sabotageApprovals.TryGetValue(caseId, out var request) || request.Approved
            || !_securityCases.Any(c => c.Id == caseId && c.WorldId == request.WorldId)) return false;
        _sabotageApprovals.Remove(caseId); AddLog("Канцлер отклонил диверсию."); return true;
    }

    public int AddCase(string title, string worldId, SWSecurityCaseKind kind, bool directorCreated = true)
    {
        EnsureSecurityMap();
        if (kind is not (SWSecurityCaseKind.Domestic or SWSecurityCaseKind.Imperial) || _securityCases.Count >= 3 || !_securityWorlds.TryGetValue(worldId, out var world)
            || kind == SWSecurityCaseKind.Imperial && world.Faction != "empire"
            || kind == SWSecurityCaseKind.Domestic && world.Faction != "republic"
            || !_securityTeams.Any(t => SecurityPath(t.World, worldId) != null)) return 0;
        var dossier = new SWSecurityCaseView { Id = _nextSecurityCase++, Title = title, WorldId = worldId,
            Kind = kind, RequiredEvidence = 2 + _nextSecurityCase % 2, DirectorCreated = directorCreated,
            Status = "Требуются улики", Objective = $"Изучить следы на {world.Name}", Risk = kind == SWSecurityCaseKind.Imperial
                ? "После диверсии защита ближайшего сектора +3 на 6 часов с постепенным спадом; при провале группа теряется" : "Расследование безопасно; операция может раскрыть ячейку" };
        if (!InitializeSecurityStory(dossier, title, directorCreated)) return 0;
        _securityCases.Add(dossier); return dossier.Id;
    }
    public bool CancelCase(int caseId)
    {
        var dossier = _securityCases.FirstOrDefault(c => c.Id == caseId);
        if (dossier == null) return false;
        RemoveSecurityStory(caseId);
        _securityCases.Remove(dossier); _sabotageApprovals.Remove(caseId); _nextSecurityCaseAt = _time.ActiveGameHours + 1;
        foreach (var team in _securityTeams.Where(t => t.CaseId == caseId))
        { team.CaseId = 0; team.Path.Clear(); if (team.Next == null) team.Phase = SWSecurityTeamPhase.Ready; }
        return true;
    }

    private void TickSecurityMap()
    {
        var now = _time.ActiveGameHours;
        if (_senate.Chancellor == null)
            foreach (var id in _sabotageApprovals.Where(p => !p.Value.Approved).Select(p => p.Key).ToArray()) ApproveSabotage(id);
        foreach (var team in _securityTeams)
        {
            if (team.Phase == SWSecurityTeamPhase.Supply) { TickSupply(team); continue; }
            if (team.Phase == SWSecurityTeamPhase.Travel && now >= team.Until)
            {
                team.World = team.Next!; team.Next = null;
                if (team.CaseId == 0 && team.Order is not (SWSecurityOrder.Move or SWSecurityOrder.Withdraw) || team.CaseId != 0 && !_securityCases.Any(c => c.Id == team.CaseId))
                { team.Path.Clear(); team.CaseId = 0; team.Phase = SWSecurityTeamPhase.Ready; }
                else StartSecurityLeg(team);
            }
            else if (team.Phase == SWSecurityTeamPhase.Recovery && now >= team.Until)
                team.Phase = SWSecurityTeamPhase.Ready;
            else if (team.Phase == SWSecurityTeamPhase.Work && now >= team.Until)
            {
                var dossier = _securityCases.FirstOrDefault(c => c.Id == team.CaseId);
                if (dossier != null)
                {
                    team.Supply = Math.Max(0, team.Supply - SupplyCost(team.Order));
                    if (team.Order == SWSecurityOrder.Surveil)
                    {
                        dossier.ReconComplete = true;
                        dossier.Clues.Add($"Наблюдение на {_securityWorlds[team.World].Name}: установлены маршруты отхода, риск операции снижен.");
                    }
                    else if (team.Order == SWSecurityOrder.Investigate)
                    {
                        AdvanceSecurityStory(dossier);
                    }
                    else if (team.Order is SWSecurityOrder.Finish or SWSecurityOrder.Sabotage)
                    {
                        var covered = _securityCells.TryGetValue(team.World, out var cell) && cell.Deployed && !cell.Burned;
                        var exposed = !(team.Order == SWSecurityOrder.Finish && dossier.Kind == SWSecurityCaseKind.Imperial) && _random.Prob(team.RiskChance);
                        if (exposed)
                        {
                            if (covered) cell!.Burned = true;
                            team.Phase = SWSecurityTeamPhase.Lost; _teamsLost++;
                            foreach (var pending in _sabotageApprovals.Where(p => p.Value.TeamId == team.Id).Select(p => p.Key).ToArray())
                                _sabotageApprovals.Remove(pending);
                            AddLog(SecurityStoryEnding(dossier, team.Order, false));
                            dossier.Status = "Операция провалена; улики сохранены";
                            AddLog($"{dossier.Title}: операция провалена, группа {team.Id} потеряна; улики сохранены.");
                            team.CaseId = 0; continue;
                        }
                        AddLog(SecurityStoryEnding(dossier, team.Order, true)); RemoveSecurityStory(dossier.Id);
                        _securityCases.Remove(dossier); _sabotageApprovals.Remove(dossier.Id); _plotsBusted++; AddThreat(-4);
                        if (dossier.Kind == SWSecurityCaseKind.Domestic)
                        {
                            _domesticCasesCompleted++;
                            var sector = _securityWorlds[team.World].SectorId;
                            _transient.Refresh("ministry.security.resolution", sector, "SWStatPolice", 2f);
                            AddLog($"{dossier.Title}: дело раскрыто; доверие к безопасности сектора временно выросло.");
                        }
                        else
                        {
                            _imperialCasesCompleted++;
                            var republic = _securityWorlds.Values.Where(w => w.Faction == "republic" && w.SectorId.Length > 0)
                                .Select(w => (World: w, Path: SecurityPath(team.World, w.Id)))
                                .Where(x => x.Path != null).OrderBy(x => SecurityPathHours(team.World, x.Path!)).FirstOrDefault();
                            if (republic.World != null)
                                _transient.Refresh("ministry.security.counterintelligence", republic.World.SectorId,
                                    team.Order == SWSecurityOrder.Sabotage ? "SWStatFleet" : "SWStatPolice",
                                    team.Order == SWSecurityOrder.Sabotage ? 3f : 1.5f);
                            AddLog(team.Order == SWSecurityOrder.Sabotage
                                ? $"{dossier.Title}: диверсия завершена; защита ближайшего сектора временно усилена."
                                : $"{dossier.Title}: разведданные извлечены; безопасность ближайшего сектора временно усилена.");
                        }
                        _nextSecurityCaseAt = now + 1;
                        team.Phase = SWSecurityTeamPhase.Recovery; team.Until = now + .5;
                    }
                }
                team.CaseId = 0;
                if (team.Phase == SWSecurityTeamPhase.Work) team.Phase = SWSecurityTeamPhase.Ready;
            }
        }
        if (!MinisterActive || _securityCases.Count >= 3 || now < _nextSecurityCaseAt) return;
        var kind = _nextSecurityCase % 3 == 0 ? SWSecurityCaseKind.Imperial : SWSecurityCaseKind.Domestic;
        var faction = kind == SWSecurityCaseKind.Imperial ? "empire" : "republic";
        var worlds = _securityWorlds.Values.Where(w => w.Faction == faction && (kind == SWSecurityCaseKind.Imperial || w.SectorId.Length > 0)
            && _securityCases.All(c => c.WorldId != w.Id) && _securityTeams.Any(t => SecurityPath(t.World, w.Id) != null)).OrderBy(w => w.Id).ToArray();
        if (worlds.Length > 0)
        {
            var titles = kind == SWSecurityCaseKind.Imperial ? new[] { "Имперская сеть", "Вражеский агент" }
                : new[] { "Контрабандная сеть", "Похищенное вооружение", "Диверсионная группа", "Вражеский агент" };
            var weighted = worlds.Select(w => (World: w, Weight: 1 + (int) Math.Clamp(
                _senate.SectorGlobals.TryGetValue(w.SectorId, out var stats) && stats.TryGetValue("SWGlobalSecurity", out var order)
                    ? (100f - order) / 20f : _threat / 20f, 0f, 5f))).ToArray();
            var roll = _random.Next(weighted.Sum(w => w.Weight));
            var chosen = weighted[0].World;
            foreach (var entry in weighted)
            {
                roll -= entry.Weight;
                if (roll >= 0) continue;
                chosen = entry.World; break;
            }
            AddCase(string.Empty, chosen.Id, kind, false);
        }
        _nextSecurityCaseAt = now + 1;
    }

    private float Mitigation(string worldId)
    {
        if (!_securityWorlds.TryGetValue(worldId, out var world) || !_securityMitigation.TryGetValue(world.SectorId, out var effect)) return 0;
        return effect.Strength * (float) Math.Clamp((effect.Until - _time.ActiveGameHours) / 6d, 0, 1);
    }

    private void PublishSecurityConditions()
    {
        var doctrine = (MinisterActive ? Config(_posture).Nudges : Array.Empty<(string Stat, float Delta)>()).Select(n => new SWSituationalContribution(null,
            n.Stat switch { "SWGlobalSecurity" => "SWStatPolice", "SWGlobalEconomy" => "SWStatEmployment", _ => "SWStatMorale" }, n.Delta * 6f)).ToArray();
        _senate.ReplaceSituationalContributions("ministry.security.posture", doctrine);
        var caseHarm = _securityCases.Where(c => MinisterActive && c.Kind == SWSecurityCaseKind.Domestic)
            .Where(c => _securityWorlds.TryGetValue(c.WorldId, out var w) && w.SectorId.Length > 0)
            .GroupBy(c => _securityWorlds[c.WorldId].SectorId)
            .Select(g => new SWSituationalContribution(g.Key, "SWStatPolice", -Math.Min(3f, g.Count() * 1.5f) * (1f - Mitigation(g.First().WorldId)))).ToArray();
        _senate.ReplaceSituationalContributions("ministry.security.cases", caseHarm);
        var field = _securityCells.Values.Where(c => MinisterActive && c.Deployed && !c.Burned)
            .GroupBy(c => _securityWorlds[c.WorldId].SectorId).Where(g => g.Key.Length > 0)
            .Select(g => new SWSituationalContribution(g.Key, "SWStatPolice", Math.Min(6f, g.Count() * 1.5f))).ToArray();
        _senate.ReplaceSituationalContributions("ministry.security.field", field);
    }
}
