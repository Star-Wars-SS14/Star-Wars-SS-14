using System;
using System.Linq;
using System.Collections.Generic;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._SW._Senate.Consoles.Empire;

public sealed class SWEmpireIntelligencePanel : BoxContainer
{
    private readonly SWEmpireCouncilConsoleBoundUserInterface _owner;
    private readonly SWGalacticMapPrototype _geography;
    private readonly BoxContainer _host = Stack(), _details = Stack();
    private readonly ScrollContainer _scroll = new() { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
    private readonly Label _title = SWConsoleStyle.Text("Галактика", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private SWGalacticMapControl _map = default!;
    private SWEmpireCouncilConsoleState? _state;
    private string? _sector, _system, _world, _target, _pendingId;
    private int? _group;
    private SWEmpireIntelligenceAction _action = SWEmpireIntelligenceAction.Recon;
    private SWEmpireIntelligenceRequest? _pending;
    private bool _submitted;
    private string _result = "";
    private SWEmpireCouncilSphere? _sphere;
    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 5 };
    public SWEmpireIntelligencePanel(SWEmpireCouncilConsoleBoundUserInterface owner)
    {
        _owner = owner; Orientation = LayoutOrientation.Vertical; HorizontalExpand = VerticalExpand = true; SeparationOverride = 4;
        _geography = SWGalacticMapViews.ForFaction(IoCManager.Resolve<IPrototypeManager>().Index<SWGalacticMapPrototype>("SenateRepublic"), "empire");
        var toolbar = new WrapContainer { HorizontalExpand = true, SeparationOverride = 5, CrossSeparationOverride = 3 }; AddChild(toolbar);
        var back = SWConsoleStyle.TabButton("Галактика"); back.OnPressed += _ => SwitchView(null,null); toolbar.AddChild(back); toolbar.AddChild(_title);
        var minus = SWConsoleStyle.TabButton("−"); minus.OnPressed += _ => _map.ChangeZoom(1 / 1.2f); toolbar.AddChild(minus);
        var plus = SWConsoleStyle.TabButton("+"); plus.OnPressed += _ => _map.ChangeZoom(1.2f); toolbar.AddChild(plus);
        _scroll.AddChild(_details); _host.VerticalExpand = true;
        AddChild(new SWAtlasLayout(_host,_scroll, stackBelow: 520,minDetails: 245,maxDetails: 330,minMapHeight: 180) { VerticalExpand = true });
        SwitchView(null,null);
    }
    private void SwitchView(string? sector, string? system)
    {
        var zoom = _map?.Zoom ?? 1; _sector = sector; _system = system;
        var view = system != null ? SWGalacticMapViews.System(_geography,system) : sector != null ? SWGalacticMapViews.PoliticalSector(_geography,sector) : SWGalacticMapViews.Overview(_geography);
        _host.RemoveAllChildren(); _map = new SWGalacticMapControl(view,focusSystem: system,focusPoliticalSector: sector) { MinHeight = 180 };
        _map.IntelligenceGroupSelected += id => { _group = id; Invalidate(); UpdateRoute(); Refresh(); };
        _map.WorldSelected += id => { _world = id; _target = null; Invalidate(); Refresh(); };
        _map.WorldActivated += id => { var world = view.Worlds.FirstOrDefault(w => w.Id == id); if (world == null || id.StartsWith("public-sector:")) return; if (_sector == null && !string.IsNullOrEmpty(world.PoliticalSectorId)) SwitchView(world.PoliticalSectorId,null); else if (_system == null && !string.IsNullOrEmpty(world.SystemId)) SwitchView(_sector,world.SystemId); };
        if (Math.Abs(zoom-1) > .001f) _map.ChangeZoom(zoom); if (_world != null) _map.SelectWorld(_world);
        _host.AddChild(_map); _title.Text = system != null ? "Система" : sector != null ? "Сектор" : "Галактика";
        UpdateRoute();
    }
    public void SetState(SWEmpireCouncilConsoleState state)
    {
        if (_sphere != state.Sphere) { _pending = null; _pendingId = null; _submitted = false; _result = ""; _group = null; }
        _sphere = state.Sphere; _state = state;
        if (_submitted && state.ActionResultIsCommand && _pendingId == state.ResultRequestId) { _result = state.Result; _submitted = false; _pending = null; _pendingId = null; }
        if (_group != null && !state.Intelligence.Groups.Any(g => g.Id == _group)) { _group = null; Invalidate(); }
        UpdateRoute(); Refresh();
    }
    private void UpdateRoute()
    {
        if (_state == null || _map == null) return;
        _map.SetIntelligence(_state.Intelligence, _geography, _group);
        var group = _state.Intelligence.Groups.FirstOrDefault(g => g.Id == _group);
        var route = new System.Collections.Generic.List<string>();
        if (group != null) { if (!string.IsNullOrEmpty(group.WorldId)) route.Add(group.WorldId); if (!string.IsNullOrEmpty(group.NextWorldId)) route.Add(group.NextWorldId); route.AddRange(group.RouteWorldIds); }
        if (_pendingId != null && _state.ResultRequestId == _pendingId && !_state.ActionResultIsCommand && _state.IntelligenceQuote.Allowed)
            _map.SetPublishedDefenseRoute(_state.IntelligenceQuote.RouteWorldIds);
        else _map.SetPublishedDefenseRoute(route);
    }
    private void Invalidate() { _pending = null; _pendingId = null; _submitted = false; _result = ""; }
    private string WorldName(string? id) => _state?.Intelligence.Worlds.FirstOrDefault(w => w.Id == id)?.Name ?? _geography.Worlds.FirstOrDefault(w => w.Id == id)?.Name ?? "Мир";
    private void Refresh()
    {
        if (_state == null) return; var position = _scroll.VScroll; _details.RemoveAllChildren();
        var canCommand = _state.Sphere == SWEmpireCouncilSphere.Intelligence;
        if (canCommand)
        {
            _details.AddChild(SWConsoleStyle.Section("Разведывательные группы"));
            foreach (var group in _state.Intelligence.Groups)
            {
                var id = group.Id; var button = SWConsoleStyle.TabButton(group.Name); button.OnPressed += _ => { _group = id; Invalidate(); UpdateRoute(); Refresh(); }; _details.AddChild(button);
                if (id == _group) _details.AddChild(SWConsoleStyle.Wrap($"{group.Status}\n{group.WorldName}\nГотовность {group.Readiness:0}%  Прибытие {group.EtaHours:0.0} ч\n{string.Join(" → ", group.Route)}", SWConsoleStyle.TextDim));
            }
            _details.AddChild(SWConsoleStyle.Section(_world == null ? "Выберите мир на карте" : WorldName(_world)));
            var choices = new[] { (SWEmpireIntelligenceAction.Recon,"Разведать"), (SWEmpireIntelligenceAction.EstablishNetwork,"Создать сеть"), (SWEmpireIntelligenceAction.ProductionSabotage,"Саботаж производства"), (SWEmpireIntelligenceAction.TransitSabotage,"Саботаж перехода"), (SWEmpireIntelligenceAction.Recall,"Отозвать"), (SWEmpireIntelligenceAction.Rest,"Отдых"), (SWEmpireIntelligenceAction.Hire,"Нанять группу") };
            foreach (var (action,name) in choices)
            {
                var button = SWConsoleStyle.TabButton(name); SWConsoleStyle.StyleTabButton(button, _action == action); button.OnPressed += _ => { _action = action; _target = null; Invalidate(); Refresh(); }; _details.AddChild(button);
            }
            if (_action is SWEmpireIntelligenceAction.ProductionSabotage or SWEmpireIntelligenceAction.TransitSabotage)
            {
                var targets = _state.Intelligence.Reports.Where(r => r.WorldId == _world).SelectMany(r => r.Targets).Where(t => t.Action == _action).ToArray();
                if (targets.Length == 0) _details.AddChild(SWConsoleStyle.Wrap("Для выбора цели требуется донесение", SWConsoleStyle.Warn));
                foreach (var target in targets) { var t = target; var button = SWConsoleStyle.TabButton(t.Name); SWConsoleStyle.StyleTabButton(button, _target == t.Id); button.OnPressed += _ => { _target = t.Id; Invalidate(); Refresh(); }; _details.AddChild(button); }
            }
            _details.AddChild(SWConsoleStyle.Wrap($"Бюджет сферы: {_state.Budgets.GetValueOrDefault(SWEmpireCouncilSphere.Intelligence):N0} кр",SWConsoleStyle.Brass));
            if (_pendingId != null && _pendingId == _state.ResultRequestId && !_submitted)
            {
                var q = _state.IntelligenceQuote;
                _details.AddChild(SWConsoleStyle.Wrap($"Стоимость {q.Cost:N0} кр\nВсего {q.EtaHours:0.0} ч\nРабота {q.WorkHours:0.0} ч\nРиск: {q.Risk}\n{(q.RouteWorldIds.Count > 0 ? "Маршрут: " + string.Join(" → ", q.RouteWorldIds.Select(WorldName)) + "\n" : "")}{q.Result}\n{q.Reason}",q.Allowed ? SWConsoleStyle.TextBright : SWConsoleStyle.Warn));
                var affordable = q.Cost <= _state.Budgets.GetValueOrDefault(SWEmpireCouncilSphere.Intelligence);
                if (q.Allowed && !affordable) _details.AddChild(SWConsoleStyle.Wrap("Недостаточно средств сферы", SWConsoleStyle.Warn));
                var send = SWConsoleStyle.Action("Отправить приказ", SWConsoleStyle.Accent); send.Disabled = !q.Allowed || !affordable; send.OnPressed += _ => { if (_pending == null || _submitted) return; _submitted = true; _owner.SubmitIntelligence(_pending); Refresh(); }; _details.AddChild(send);
            }
            else { var quote = SWConsoleStyle.Action("Рассчитать",SWConsoleStyle.Accent); quote.Disabled = _submitted || (_action != SWEmpireIntelligenceAction.Hire && _group == null); quote.OnPressed += _ => Calculate(); _details.AddChild(quote); }
            if (_submitted) _details.AddChild(SWConsoleStyle.Wrap("Отправка приказа",SWConsoleStyle.TextDim));
            if (!string.IsNullOrWhiteSpace(_result)) _details.AddChild(SWConsoleStyle.Wrap(_result,SWConsoleStyle.Warn));
        }
        _details.AddChild(SWConsoleStyle.Section("Донесения"));
        if (_state.Intelligence.Reports.Count == 0) _details.AddChild(SWConsoleStyle.Wrap("Донесений пока нет", SWConsoleStyle.TextDim));
        foreach (var report in _state.Intelligence.Reports.AsEnumerable().Reverse()) _details.AddChild(SWConsoleStyle.Wrap($"{report.WorldName}\n{report.Hour:0.0} ч\n{report.Summary}", SWConsoleStyle.TextBright));
        if (canCommand) foreach (var effect in _state.Intelligence.Effects) _details.AddChild(SWConsoleStyle.Wrap($"{WorldName(effect.WorldId)}\n{effect.TargetName}\n{effect.Status}  {effect.RemainingHours:0.0} ч",SWConsoleStyle.TextDim));
        _scroll.VScroll = position;
    }
    private void Calculate()
    {
        if (_state?.Sphere != SWEmpireCouncilSphere.Intelligence) return;
        _pending = new SWEmpireIntelligenceRequest { RequestId = Guid.NewGuid().ToString("N"), Action = _action, GroupId = _group ?? 0, WorldId = _world ?? "", TargetId = _target ?? "" };
        _pendingId = _pending.RequestId; _submitted = false; _owner.QuoteIntelligence(_pending); Refresh();
    }
}
