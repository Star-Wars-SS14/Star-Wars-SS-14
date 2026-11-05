using System;
using System.Linq;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed class SWSecurityMapPanel : BoxContainer
{
    private readonly SWGalacticMapPrototype _source = IoCManager.Resolve<IPrototypeManager>().Index<SWGalacticMapPrototype>("SenateRepublic");
    private readonly BoxContainer _host = Stack();
    private readonly BoxContainer _teams = Stack();
    private readonly BoxContainer _details = Stack();
    private readonly Label _title = SWConsoleStyle.Text("Галактика", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private readonly Label _teamStatus = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Label _zoom = SWConsoleStyle.Text("100%", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Button _galaxy = SWConsoleStyle.TabButton("Галактика");
    private SWSecurityMapState? _state;
    private string? _revision;
    private string? _sector, _system, _world;
    private int? _team, _case;
    private bool _cluesOpen, _materialsOpen;
    private readonly ScrollContainer _scroll = new() { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
    public int? SelectedTeamId => _team;
    public event Action<int?>? TeamSelected;
    private SWGalacticMapControl _map = default!;
    public event Action<int, SWSecurityOrder, string, int>? OrderRequested;
    public event Action<string, bool>? CellRequested;
    public event Action<int>? CaseSelected;
    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };

    public SWSecurityMapPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;
        var bar = new WrapContainer { HorizontalExpand = true, SeparationOverride = 5, CrossSeparationOverride = 4 };
        _galaxy.OnPressed += _ => { if (_system != null && _sector != null) SwitchView(_sector); else SwitchView(null); };
        bar.AddChild(_galaxy); bar.AddChild(_title);
        var minus = SWConsoleStyle.TabButton("−"); minus.OnPressed += _ => _map.ChangeZoom(1 / 1.2f); bar.AddChild(minus);
        var plus = SWConsoleStyle.TabButton("+"); plus.OnPressed += _ => _map.ChangeZoom(1.2f); bar.AddChild(plus);
        bar.AddChild(_zoom);
        var reset = SWConsoleStyle.TabButton("Сбросить масштаб"); reset.OnPressed += _ => _map.ResetView(); bar.AddChild(reset);
        AddChild(bar);
        var side = Stack(); side.AddChild(_teams); side.AddChild(_details); _scroll.AddChild(side);
        _host.VerticalExpand = true;
        AddChild(new SWAtlasLayout(_host, _scroll, stackBelow: 520, minDetails: 230, maxDetails: 330));
        SwitchView(null);
    }
    public void SetState(SWSecurityMapState? state)
    {
        _state = state;
        if (_team != null && state?.Teams.All(t => t.Id != _team) != false) _team = null;
        _map.SetSecurity(state, _team);
        UpdateTeamStatus();
        var revision = state == null ? "" : $"{state.Budget}|{state.MinisterActive}|{state.ClockRunning}|{state.TeamsLost}|{state.ReplacementCost}|" +
            string.Join(";", state.Teams.Select(t => $"{t.Id}:{t.WorldId}:{t.TargetWorldId}:{t.Phase}:{t.Status}:{t.Supply}:{t.SupplyStatus}:{t.SupplyProjectWorld}:{string.Join(",", t.SupplyMaterials.Select(m => $"{m.Name}:{m.Required}:{m.Delivered}:{m.Incoming}:{SWSenateDuration.FormatHours(Math.Max(0, m.EtaHours - state.NowHours))}"))}")) + "|" +
            string.Join(";", state.Cells.Select(c => $"{c.WorldId}:{c.Deployed}:{c.Burned}:{c.CanDeploy}:{c.Reason}")) + "|" +
            string.Join(";", state.Cases.Select(c => $"{c.Id}:{c.Title}:{c.Kind}:{c.WorldId}:{c.Evidence}:{c.RequiredEvidence}:{c.Status}:{c.Risk}:{c.SabotageApproved}:{c.SabotageApprovalPending}:{c.ReconComplete}:{c.Briefing}:{c.Scene}:{c.Objective}:{string.Join(",", c.Clues)}")) + "|" +
            string.Join(";", state.OrderOptions.Select(o => $"{o.TeamId}:{o.Order}:{o.WorldId}:{o.CaseId}:{o.Available}:{o.Reason}:{o.Risk}:{o.Cost}:{o.BaseRiskPercent}:{o.SupplyPenaltyPercent}:{o.LaunchRiskPercent}:{o.SupplyCost}:{o.TravelHours}:{o.WorkHours}"));
        if (_revision == revision) return;
        _revision = revision;
        var scroll = _scroll.VScroll; Refresh(); _scroll.VScroll = scroll;
    }
    public void SelectTeam(int? id)
    {
        _team = id; _map.SetSecurity(_state, id); Refresh();
    }
    private void ChooseTeam(int id)
    {
        SelectTeam(id); TeamSelected?.Invoke(id);
    }
    public void SelectCase(int id)
    {
        _case = id;
        var dossier = _state?.Cases.FirstOrDefault(c => c.Id == id);
        if (dossier != null) SelectWorld(dossier.WorldId);
    }
    public void SelectWorld(string id)
    {
        var visible = SWGalacticMapViews.ForFaction(_source, "republic");
        var world = visible.Worlds.FirstOrDefault(w => w.Id == id);
        if (world == null) return;
        if (_sector != world.Sector?.Id && _sector != null) SwitchView(world.Sector?.Id);
        else if (_sector == null && !world.Anchor) SwitchView(world.Sector?.Id);
        _world = id; _map.SelectWorld(id); Refresh();
    }
    private void SwitchView(string? sector, string? system = null)
    {
        if (_map != null && sector == _sector && system == _system) return;
        var previous = _map;
        var anchor = _source.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == (sector ?? _sector))?.Id;
        _sector = sector; _system = system;
        var visible = SWGalacticMapViews.ForFaction(_source, "republic");
        var view = system != null ? SWGalacticMapViews.System(visible, system) : sector == null ? SWGalacticAtlas.Overview(visible) : SWGalacticMapViews.Sector(visible, sector);
        view = SWGalacticAtlas.MakeSchematicIfNeeded(view, system == null ? sector : null, system, null, out var schematic);
        _host.RemoveAllChildren();
        _map = new SWGalacticMapControl(view, system == null ? sector : null, focusSystem: system) { MinHeight = 260 };
        _map.SetGeography(visible, schematic);
        _map.SetExternalLabels(true);
        _map.SystemSelected += id =>
        {
            var world = visible.Worlds.FirstOrDefault(w => w.SystemId == id);
            if (world?.Sector is { } parent) SwitchView(parent.Id);
            else SwitchView(null, id);
        };
        _map.SetSecurity(_state, _team);
        _map.WorldSelected += id => { _world = id; Refresh(); };
        _map.WorldActivated += id =>
        {
            var world = visible.Worlds.FirstOrDefault(w => w.Id == id);
            if (world == null) return;
            if (_system == null && _sector != null && world.Sector?.Id == _sector && !string.IsNullOrEmpty(world.SystemId))
                SwitchView(_sector, world.SystemId);
            else if (_system == null && world.Sector is { } parent && parent.Id != _sector)
                SwitchView(parent.Id);
            _world = id; _map.SelectWorld(id); Refresh();
        };
        _map.SecurityTeamSelected += ChooseTeam;
        _map.SecurityCaseSelected += id => { SelectCase(id); CaseSelected?.Invoke(id); };
        _map.SecurityCellSelected += SelectWorld;
        _map.ViewChanged += () => _zoom.Text = $"{_map.Zoom * 100:0}%";
        _host.AddChild(_map);
        _galaxy.Visible = sector != null || system != null;
        _galaxy.Text = system != null && sector != null ? "К сектору" : "Галактика";
        _title.Text = system != null ? "Система" : sector == null ? "Галактика" : _source.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == sector)?.Name ?? "Сектор";
        if (_world != null) _map.SelectWorld(_world);
        if (previous != null && anchor != null) _map.TransitionFrom(previous, anchor, sector != null);
    }
    private string WorldName(string? id) => _state?.Worlds.FirstOrDefault(w => w.Id == id)?.Name ?? _source.Worlds.FirstOrDefault(w => w.Id == id)?.Name ?? "Неизвестный мир";
    private void Refresh()
    {
        _teams.RemoveAllChildren(); _details.RemoveAllChildren();
        _teams.AddChild(SWConsoleStyle.Section("Оперативные группы"));
        if (_state == null) return;
        var choices = new WrapContainer { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
        foreach (var team in _state.Teams)
        {
            var id = team.Id;
            var button = SWConsoleStyle.TabButton(team.Phase == SWSecurityTeamPhase.Lost ? $"{team.Name} — потеряна" : team.Name);
            SWConsoleStyle.StyleTabButton(button, _team == id);
            button.ToolTip = $"{team.Status}: {WorldName(team.WorldId)}";
            button.OnPressed += _ => ChooseTeam(id);
            choices.AddChild(button);
        }
        _teams.AddChild(choices);
        _teams.AddChild(SWConsoleStyle.Text($"Бюджет: {_state.Budget} кр   Ячейки: {_state.Cells.Count(c => c.Deployed)}/{_state.CellCap}", SWConsoleStyle.Brass, SWConsoleStyle.Small));
        if (_state.TeamsLost > 0) _teams.AddChild(SWConsoleStyle.Text($"Потеряно групп: {_state.TeamsLost}", SWConsoleStyle.Warn, SWConsoleStyle.Small));
        var locate = SWConsoleStyle.TabButton("К группе"); locate.Disabled = _team == null;
        locate.OnPressed += _ => { if (_team is { } id) _map.FocusSecurityTeam(id); }; _teams.AddChild(locate);
        var selected = _state.Teams.FirstOrDefault(t => t.Id == _team);
        _details.AddChild(SWConsoleStyle.Section("Назначение"));
        _details.AddChild(SWConsoleStyle.Wrap(_world == null ? "Выберите мир на карте" : WorldName(_world), SWConsoleStyle.TextBright));
        if (selected is { Phase: SWSecurityTeamPhase.Lost })
        {
            _details.AddChild(SWConsoleStyle.Wrap($"{selected.Name}: потеряна", SWConsoleStyle.Bad));
            var replace = _state.OrderOptions.FirstOrDefault(o => o.TeamId == selected.Id && o.Order == SWSecurityOrder.Replace);
            var button = SWConsoleStyle.Action("Набрать замену", SWConsoleStyle.Warn, replace?.Available != true);
            if (replace != null) _details.AddChild(SWConsoleStyle.Wrap($"Стоимость: {replace.Cost} кр; набор: {SWSenateDuration.FormatHours(replace.WorkHours)}", SWConsoleStyle.TextDim));
            if (replace?.Available == true) button.OnPressed += _ => { OrderRequested?.Invoke(selected.Id, SWSecurityOrder.Replace, replace.WorldId, 0); button.Disabled = true; button.Text = "Набор начат"; };
            _details.AddChild(button);
            if (replace?.Available != true) _details.AddChild(SWConsoleStyle.Wrap(replace?.Reason ?? "Замена недоступна", SWConsoleStyle.TextDim));
        }
        else if (selected != null)
        {
            UpdateTeamStatus();
            _details.AddChild(_teamStatus);
            AddSupply(selected);
            var target = _state.Worlds.FirstOrDefault(w => w.Id == _world);
            var option = _state.OrderOptions.FirstOrDefault(o => o.TeamId == selected.Id && o.Order == SWSecurityOrder.Move && o.WorldId == _world);
            var reason = target == null ? "Выберите мир" : option?.Reason ?? "Маршрут недоступен";
            var move = SWConsoleStyle.Primary(option == null ? "Отправить группу" : $"Отправить: {SWSenateDuration.FormatHours(option.TravelHours)}, бесплатно", option?.Available != true);
            if (option?.Available == true && target != null) move.OnPressed += _ => { OrderRequested?.Invoke(selected.Id, SWSecurityOrder.Move, target.Id, 0); move.Disabled = true; move.Text = "Приказ отправлен"; };
            _details.AddChild(move);
            var withdrawWorld = selected.Phase == SWSecurityTeamPhase.Supply ? selected.WorldId : _world;
            var withdraw = _state.OrderOptions.FirstOrDefault(o => o.TeamId == selected.Id && o.Order == SWSecurityOrder.Withdraw && o.WorldId == withdrawWorld);
            if (withdraw != null && (selected.Phase is SWSecurityTeamPhase.Work or SWSecurityTeamPhase.Supply || _state.Worlds.FirstOrDefault(w => w.Id == selected.WorldId)?.Faction != "republic"))
            {
                var back = SWConsoleStyle.Action(selected.Phase == SWSecurityTeamPhase.Supply ? "Прервать пополнение" : $"Отозвать группу: {SWSenateDuration.FormatHours(withdraw.TravelHours)}", SWConsoleStyle.Warn, !withdraw.Available);
                if (withdraw.Available) back.OnPressed += _ => { OrderRequested?.Invoke(selected.Id, SWSecurityOrder.Withdraw, withdraw.WorldId, 0); back.Disabled = true; back.Text = "Приказ отправлен"; };
                _details.AddChild(back);
                if (!withdraw.Available) _details.AddChild(SWConsoleStyle.Wrap(withdraw.Reason, SWConsoleStyle.TextDim));
            }
            if (option?.Available != true && !string.IsNullOrEmpty(reason)) _details.AddChild(SWConsoleStyle.Wrap(reason, SWConsoleStyle.TextDim));
        }
        else _details.AddChild(SWConsoleStyle.Wrap("Выберите группу на карте или в списке", SWConsoleStyle.TextDim));
        var cell = _state.Cells.FirstOrDefault(c => c.WorldId == _world);
        if (cell != null)
        {
            _details.AddChild(SWConsoleStyle.Section("Ячейка на мире"));
            var reason = cell.Burned ? "Ячейка раскрыта; направьте группу для восстановления" : !cell.Deployed && !cell.CanDeploy ? cell.Reason : !_state.MinisterActive || !_state.ClockRunning ? "Министерство не работает" : null;
            var button = SWConsoleStyle.Action(cell.Deployed ? "Отозвать ячейку" : $"Развернуть: {_state.DeploymentCost} кр", SWConsoleStyle.Accent, reason != null);
            if (reason == null) button.OnPressed += _ => { CellRequested?.Invoke(cell.WorldId, !cell.Deployed); button.Disabled = true; button.Text = "Приказ отправлен"; };
            _details.AddChild(button);
            if (reason != null) _details.AddChild(SWConsoleStyle.Wrap(reason, SWConsoleStyle.Warn));
            if (cell.Burned && selected != null && selected.WorldId == cell.WorldId && selected.Phase == SWSecurityTeamPhase.Ready)
            {
                var recovery = _state.OrderOptions.FirstOrDefault(o => o.TeamId == selected.Id && o.Order == SWSecurityOrder.Recover && o.WorldId == cell.WorldId);
                var recover = SWConsoleStyle.Action($"Восстановить: {_state.RecoveryCost} кр", SWConsoleStyle.Warn, recovery?.Available != true);
                recover.OnPressed += _ => { OrderRequested?.Invoke(selected.Id, SWSecurityOrder.Recover, cell.WorldId, 0); recover.Disabled = true; }; _details.AddChild(recover);
            }
        }
        foreach (var dossier in _state.Cases.Where(c => c.WorldId == _world))
        {
            var id = dossier.Id;
            var button = SWConsoleStyle.TabButton(dossier.Title);
            SWConsoleStyle.StyleTabButton(button, id == _case);
            button.OnPressed += _ => { _case = id; CaseSelected?.Invoke(id); Refresh(); };
            _details.AddChild(button);
            if (_case != id) continue;
            SWSecurityDossierPresentation.Add(_details, dossier, WorldName(dossier.WorldId), _cluesOpen, () => { _cluesOpen = !_cluesOpen; Refresh(); });
            var complete = dossier.Evidence >= dossier.RequiredEvidence;
            var order = complete ? SWSecurityOrder.Finish : SWSecurityOrder.Investigate;
            AddCaseAction(dossier, order, complete ? dossier.Kind == SWSecurityCaseKind.Imperial ? "Завершить разведку" : "Завершить дело" : "Собрать улику");
            if (dossier.Kind == SWSecurityCaseKind.Imperial)
            {
                if (complete) _details.AddChild(SWConsoleStyle.Wrap("Разведотчёт безопасен; диверсия даёт более сильный результат, но может привести к потере группы.", SWConsoleStyle.TextDim));
                if (complete) AddCaseAction(dossier, SWSecurityOrder.Sabotage, dossier.SabotageApproved ? "Запустить диверсию" : "Запросить одобрение");
                AddCaseAction(dossier, SWSecurityOrder.Surveil, "Подготовить наблюдение");
            }
        }
    }
    private void AddSupply(SWSecurityTeamView team)
    {
        if (_state == null) return;
        _details.AddChild(SWConsoleStyle.Section("Снабжение"));
        var readiness = team.Supply >= 75 ? "готова" : team.Supply >= 50 ? "ограниченная готовность" : "недостаточное оснащение";
        _details.AddChild(SWConsoleStyle.Wrap($"{team.Supply}/100, {readiness}", team.Supply < 50 ? SWConsoleStyle.Warn : SWConsoleStyle.TextBright));
        if (!string.IsNullOrWhiteSpace(team.SupplyStatus)) _details.AddChild(SWConsoleStyle.Wrap(team.SupplyStatus, SWConsoleStyle.TextDim));
        _details.AddChild(SWConsoleStyle.Bar(team.Supply, 0, 100, team.Supply < 50 ? SWConsoleStyle.Warn : SWConsoleStyle.Accent));
        var option = _state.OrderOptions.FirstOrDefault(o => o.TeamId == team.Id && o.Order == SWSecurityOrder.Resupply && o.WorldId == team.WorldId);
        var resupply = SWConsoleStyle.Action("Пополнить снабжение", SWConsoleStyle.Accent, option?.Available != true);
        if (option?.Available == true) resupply.OnPressed += _ => { OrderRequested?.Invoke(team.Id, SWSecurityOrder.Resupply, team.WorldId, 0); resupply.Disabled = true; resupply.Text = "Приказ отправлен"; };
        _details.AddChild(resupply);
        if (option?.Available != true && !string.IsNullOrEmpty(option?.Reason)) _details.AddChild(SWConsoleStyle.Wrap(option.Reason, SWConsoleStyle.TextDim));
        if (team.SupplyMaterials.Count == 0) return;
        var materials = SWConsoleStyle.TabButton(_materialsOpen ? "Скрыть материалы" : "Материалы и доставка");
        materials.OnPressed += _ => { _materialsOpen = !_materialsOpen; Refresh(); };
        _details.AddChild(materials);
        if (!_materialsOpen) return;
        foreach (var material in team.SupplyMaterials)
        {
            var eta = material.Incoming > 0 && material.EtaHours >= 0 ? $", ближайшая партия через {SWSenateDuration.FormatHours(Math.Max(0, material.EtaHours - _state.NowHours))}" : "";
            _details.AddChild(SWConsoleStyle.Wrap($"{material.Name}: получено {material.Delivered:0.###}/{material.Required:0.###}, в пути {material.Incoming:0.###}{eta}", SWConsoleStyle.TextDim));
        }
    }
    private void UpdateTeamStatus()
    {
        var selected = _state?.Teams.FirstOrDefault(t => t.Id == _team);
        if (selected == null) { _teamStatus.Text = ""; return; }
        var remaining = selected.Phase == SWSecurityTeamPhase.Travel ? selected.DestinationEtaHours : selected.EtaHours;
        var timer = selected.Phase is SWSecurityTeamPhase.Travel or SWSecurityTeamPhase.Work or SWSecurityTeamPhase.Recovery
            ? $" — {SWSenateDuration.FormatHours(Math.Max(0, remaining - _state!.NowHours))}" : "";
        _teamStatus.Text = $"{selected.Name}: {selected.Status}{timer}\n{WorldName(selected.WorldId)}" +
            (selected.Phase == SWSecurityTeamPhase.Travel ? $" → {WorldName(selected.TargetWorldId)}" : "");
    }
    private void AddCaseAction(SWSecurityCaseView dossier, SWSecurityOrder order, string title)
    {
        var option = _state?.OrderOptions.FirstOrDefault(o => o.TeamId == _team && o.Order == order && o.CaseId == dossier.Id);
        var text = title;
        var button = SWConsoleStyle.Primary(text, option?.Available != true);
        if (option?.Available == true && _team is { } id)
            button.OnPressed += _ => { OrderRequested?.Invoke(id, order, dossier.WorldId, dossier.Id); button.Disabled = true; button.Text = "Приказ отправлен"; };
        _details.AddChild(button);
        if (option != null)
        {
            _details.AddChild(SWConsoleStyle.Wrap($"Стоимость: {option.Cost} кр; путь: {SWSenateDuration.FormatHours(option.TravelHours)}; работа: {SWSenateDuration.FormatHours(option.WorkHours)}", SWConsoleStyle.TextDim));
            if (option.SupplyPenaltyPercent > 0) _details.AddChild(SWConsoleStyle.Wrap($"Недостаток снабжения: +{option.SupplyPenaltyPercent}% к риску; итоговая вероятность потери {option.LaunchRiskPercent}%.", SWConsoleStyle.Warn));
            if (!string.IsNullOrEmpty(option.Risk)) _details.AddChild(SWConsoleStyle.Wrap(option.Risk, SWConsoleStyle.Warn));
            if (!option.Available && !string.IsNullOrEmpty(option.Reason)) _details.AddChild(SWConsoleStyle.Wrap(option.Reason, SWConsoleStyle.Warn));
        }
        else _details.AddChild(SWConsoleStyle.Wrap("Выберите группу", SWConsoleStyle.TextDim));
    }

}
