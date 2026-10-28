using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWMinistryWorkConsoleMenu
{
    private SWSecurityMapPanel? _securityMap;
    private int? _securityCase;
    private int? _securityTeam;
    private string? _securityCaseRevision;
    private bool _securityCluesOpen;

    private void BuildNetwork(SWMinistryWorkConsoleState state)
    {
        if (!state.IsSecurity) return;
        if (_securityMap == null)
        {
            _securityMap = new SWSecurityMapPanel();
            _securityMap.OrderRequested += (team, order, world, dossier) => _owner.SecurityOrder(team, order, world, dossier);
            _securityMap.CellRequested += (world, deploy) => _owner.SecurityCell(world, deploy);
            _securityMap.CaseSelected += id => { _securityCase = id; _securityCaseRevision = null; if (_state != null) BuildSecOps(_state); };
            _securityMap.TeamSelected += id => { _securityTeam = id; _securityMap?.SelectTeam(id); _securityCaseRevision = null; if (_state != null) BuildSecOps(_state); };
            NetworkPanel.AddChild(_securityMap);
        }
        _securityMap.SetState(state.SecurityMap);
    }
    private void BuildSecOps(SWMinistryWorkConsoleState state)
    {
        if (!state.IsSecurity || state.SecurityMap is not { } map) return;
        if (_securityCase != null && map.Cases.All(c => c.Id != _securityCase)) _securityCase = null;
        if (_securityTeam != null && map.Teams.All(t => t.Id != _securityTeam)) _securityTeam = null;
        var revision = $"{map.MinisterActive}|{map.ClockRunning}|{map.Budget}|{_securityCase}|{_securityTeam}|" +
            string.Join(";", state.SecurityLog) + "|" + string.Join(";", map.Cases.Select(c => $"{c.Id}:{c.Title}:{c.Kind}:{c.WorldId}:{c.Evidence}:{c.RequiredEvidence}:{c.Status}:{c.Risk}:{c.ReconComplete}:{c.SabotageApproved}:{c.SabotageApprovalPending}:{c.Briefing}:{c.Scene}:{c.Objective}:{string.Join(",", c.Clues)}")) + "|" +
            string.Join(";", map.Teams.Select(t => $"{t.Id}:{t.WorldId}:{t.Phase}:{t.Status}:{t.Supply}:{t.SupplyStatus}:{SWSenateDuration.FormatHours(System.Math.Max(0, t.DestinationEtaHours - map.NowHours))}")) + "|" +
            string.Join(";", map.OrderOptions.Where(o => o.CaseId == _securityCase && o.TeamId == _securityTeam).Select(o => $"{o.Order}:{o.Available}:{o.Reason}:{o.Risk}:{o.Cost}:{o.BaseRiskPercent}:{o.SupplyPenaltyPercent}:{o.LaunchRiskPercent}:{o.SupplyCost}:{o.TravelHours}:{o.WorkHours}"));
        if (_securityCaseRevision == revision) return;
        _securityCaseRevision = revision;
        var scroll = SecOpsPanel.VScroll;
        SecOpsContent.RemoveAllChildren();
        SecOpsContent.AddChild(SWConsoleStyle.Text($"Бюджет: {map.Budget} кр   Закрыто дел: {map.CompletedCases}", SWConsoleStyle.Brass, SWConsoleStyle.Strong));
        SecOpsContent.AddChild(SWConsoleStyle.Section("Дела"));
        if (state.SecurityLog.Count > 0)
        {
            SecOpsContent.AddChild(SWConsoleStyle.Section("Результаты"));
            foreach (var entry in state.SecurityLog) SecOpsContent.AddChild(SWConsoleStyle.Wrap(entry, SWConsoleStyle.TextDim));
        }
        var cases = new WrapContainer { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
        foreach (var item in map.Cases)
        {
            var id = item.Id; var button = SWConsoleStyle.TabButton(item.Title);
            SWConsoleStyle.StyleTabButton(button, id == _securityCase);
            button.OnPressed += _ => { _securityCase = id; _securityCaseRevision = null; if (_state != null) BuildSecOps(_state); };
            cases.AddChild(button);
        }
        SecOpsContent.AddChild(cases);
        var dossier = map.Cases.FirstOrDefault(c => c.Id == _securityCase);
        if (dossier == null)
        {
            SecOpsContent.AddChild(SWConsoleStyle.Wrap(map.Cases.Count == 0 ? "Активных дел нет" : "Выберите дело", SWConsoleStyle.TextDim));
            SecOpsPanel.VScroll = scroll;
            return;
        }
        var world = map.Worlds.FirstOrDefault(w => w.Id == dossier.WorldId)?.Name ?? "Неизвестный мир";
        SecOpsContent.AddChild(SWConsoleStyle.Section(dossier.Title));
        SWSecurityDossierPresentation.Add(SecOpsContent, dossier, world, _securityCluesOpen, () => { _securityCluesOpen = !_securityCluesOpen; _securityCaseRevision = null; if (_state != null) BuildSecOps(_state); });
        var show = SWConsoleStyle.TabButton("Показать на карте");
        show.OnPressed += _ => { _securityMap?.SelectCase(dossier.Id); ShowTab(NetworkTab); };
        SecOpsContent.AddChild(show);
        SecOpsContent.AddChild(SWConsoleStyle.Section("Исполнитель"));
        var teams = new WrapContainer { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
        foreach (var item in map.Teams)
        {
            var id = item.Id; var button = SWConsoleStyle.TabButton(item.Name);
            SWConsoleStyle.StyleTabButton(button, id == _securityTeam);
            button.ToolTip = item.Status;
            button.OnPressed += _ => { _securityTeam = id; _securityMap?.SelectTeam(id); _securityCaseRevision = null; if (_state != null) BuildSecOps(_state); };
            teams.AddChild(button);
        }
        SecOpsContent.AddChild(teams);
        var team = map.Teams.FirstOrDefault(t => t.Id == _securityTeam);
        if (team != null && team.Phase != SWSecurityTeamPhase.Ready)
            SecOpsContent.AddChild(SWConsoleStyle.Wrap($"{team.Status}: осталось {SWSenateDuration.FormatHours(System.Math.Max(0, team.DestinationEtaHours - map.NowHours))}", SWConsoleStyle.Accent));
        var complete = dossier.Evidence >= dossier.RequiredEvidence;
        var order = complete ? SWSecurityOrder.Finish : SWSecurityOrder.Investigate;
        var option = map.OrderOptions.FirstOrDefault(o => o.TeamId == _securityTeam && o.Order == order && o.WorldId == dossier.WorldId && o.CaseId == dossier.Id);
        var cost = option?.Cost ?? 0;
        var reason = team == null ? "Выберите группу" : option?.Reason ?? "Приказ недоступен";
        var caption = order switch { SWSecurityOrder.Sabotage => dossier.SabotageApproved ? "Запустить диверсию" : "Запросить одобрение", SWSecurityOrder.Finish => dossier.Kind == SWSecurityCaseKind.Imperial ? "Завершить разведку" : "Завершить дело", SWSecurityOrder.Surveil => "Провести наблюдение", _ => "Собрать улику" };
        var action = SWConsoleStyle.Primary(order == SWSecurityOrder.Sabotage && !dossier.SabotageApproved ? $"{caption} (запуск: {cost} кр)" : $"{caption} — {cost} кр", option?.Available != true);
        if (option?.Available == true && team != null) action.OnPressed += _ => { _owner.SecurityOrder(team.Id, order, dossier.WorldId, dossier.Id); action.Disabled = true; action.Text = "Приказ отправлен"; };
        SecOpsContent.AddChild(action);
        if (option is { SupplyPenaltyPercent: > 0 }) SecOpsContent.AddChild(SWConsoleStyle.Wrap($"Недостаток снабжения: +{option.SupplyPenaltyPercent}% к риску; итоговая вероятность потери {option.LaunchRiskPercent}%.", SWConsoleStyle.Warn));
        if (option?.Available != true && !string.IsNullOrEmpty(reason)) SecOpsContent.AddChild(SWConsoleStyle.Wrap(reason, SWConsoleStyle.Warn));
        if (dossier.Kind == SWSecurityCaseKind.Imperial)
        {
            if (complete)
            {
                SecOpsContent.AddChild(SWConsoleStyle.Wrap("Разведотчёт безопасен и завершает дело.", SWConsoleStyle.TextDim));
                var sabotage = map.OrderOptions.FirstOrDefault(o => o.TeamId == _securityTeam && o.Order == SWSecurityOrder.Sabotage && o.CaseId == dossier.Id);
                var sabotageTitle = dossier.SabotageApproved ? "Запустить диверсию" : "Запросить одобрение диверсии";
                var sabotageButton = SWConsoleStyle.Action(sabotage == null ? sabotageTitle : $"{sabotageTitle} (запуск: {sabotage.Cost} кр)", SWConsoleStyle.Warn, sabotage?.Available != true);
                if (sabotage?.Available == true && team != null) sabotageButton.OnPressed += _ => { _owner.SecurityOrder(team.Id, SWSecurityOrder.Sabotage, dossier.WorldId, dossier.Id); sabotageButton.Disabled = true; sabotageButton.Text = "Приказ отправлен"; };
                SecOpsContent.AddChild(sabotageButton);
                if (sabotage != null)
                {
                    if (sabotage.SupplyPenaltyPercent > 0) SecOpsContent.AddChild(SWConsoleStyle.Wrap($"Недостаток снабжения: +{sabotage.SupplyPenaltyPercent}% к риску; итоговая вероятность потери {sabotage.LaunchRiskPercent}%.", SWConsoleStyle.Warn));
                    SecOpsContent.AddChild(SWConsoleStyle.Wrap(sabotage.Risk, SWConsoleStyle.Warn));
                    if (!sabotage.Available) SecOpsContent.AddChild(SWConsoleStyle.Wrap(sabotage.Reason, SWConsoleStyle.TextDim));
                }
                SecOpsContent.AddChild(SWConsoleStyle.Wrap("Диверсия даёт более сильный результат и может привести к потере группы.", SWConsoleStyle.TextDim));
            }
            var surveillance = map.OrderOptions.FirstOrDefault(o => o.TeamId == _securityTeam && o.Order == SWSecurityOrder.Surveil && o.CaseId == dossier.Id);
            var prepare = SWConsoleStyle.Action("Подготовить наблюдение", SWConsoleStyle.Accent, surveillance?.Available != true);
            if (surveillance?.Available == true && team != null) prepare.OnPressed += _ => { _owner.SecurityOrder(team.Id, SWSecurityOrder.Surveil, dossier.WorldId, dossier.Id); prepare.Disabled = true; prepare.Text = "Приказ отправлен"; };
            SecOpsContent.AddChild(prepare);
            if (surveillance != null && !surveillance.Available) SecOpsContent.AddChild(SWConsoleStyle.Wrap(surveillance.Reason, SWConsoleStyle.TextDim));
        }
        if (option != null) SecOpsContent.AddChild(SWConsoleStyle.Wrap($"Путь: {SWSenateDuration.FormatHours(option.TravelHours)}; работа: {SWSenateDuration.FormatHours(option.WorkHours)}\n{option.Risk}", SWConsoleStyle.TextDim));
        SecOpsPanel.VScroll = scroll;
    }
}
