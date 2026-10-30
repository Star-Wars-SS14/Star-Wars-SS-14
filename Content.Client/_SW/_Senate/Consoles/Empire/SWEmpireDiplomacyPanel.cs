using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._SW._Senate.Consoles.Empire;

public sealed class SWEmpireDiplomacyPanel : BoxContainer
{
    private readonly SWEmpireCouncilConsoleBoundUserInterface _owner;
    private readonly IPrototypeManager _proto = IoCManager.Resolve<IPrototypeManager>();
    private readonly SWGalacticMapPrototype _geography;
    private readonly BoxContainer _host = Stack(), _side = Stack(), _editor = Stack(), _terms = Stack(), _agreements = Stack(), _cargoList = Stack();
    private readonly ScrollContainer _listScroll = new() { HScrollEnabled = false, HorizontalExpand = true, MinHeight = 90, MaxHeight = 150 };
    private readonly ScrollContainer _detailScroll = new() { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
    private readonly BoxContainer _detail = Stack();
    private readonly Button _new = SWConsoleStyle.Action("Новое соглашение", SWConsoleStyle.Accent), _routeAgreement = SWConsoleStyle.Action("Согласовать маршрут", SWConsoleStyle.Accent);
    private readonly List<Button> _modes = new();
    private bool _editing;
    private string? _selectedCargo;
    private string? _cargoTarget, _cargoTargetName, _cargoWorld;
    private readonly Label _title = SWConsoleStyle.Text("Галактика",SWConsoleStyle.Brass,SWConsoleStyle.Strong);
    private readonly RichTextLabel _targetText = SWConsoleStyle.Wrap("Выберите объект на карте",SWConsoleStyle.TextDim), _quoteText = SWConsoleStyle.Wrap("",SWConsoleStyle.TextBright), _budget = SWConsoleStyle.Wrap("",SWConsoleStyle.Brass);
    private readonly OptionButton _term = new() { HorizontalExpand = true }, _party = new() { HorizontalExpand = true }, _duration = new() { HorizontalExpand = true }, _goods = new() { HorizontalExpand = true, Filterable = true }, _source = new() { HorizontalExpand = true, Filterable = true };
    private readonly LineEdit _amount = new() { HorizontalExpand = true, PlaceHolder = "Количество" };
    private readonly BoxContainer _supply = Stack();
    private readonly RichTextLabel _sourceLabel = SWConsoleStyle.Wrap("Имперский мир назначения", SWConsoleStyle.TextDim);
    private readonly Button _transitFrom = SWConsoleStyle.TabButton("Выбрать начало"), _transitTo = SWConsoleStyle.TabButton("Выбрать назначение");
    private readonly BoxContainer _transitFields = Stack();
    private readonly Button _sectorButton = SWConsoleStyle.TabButton("Выбрать сектор"), _calculate = SWConsoleStyle.Action("Рассчитать",SWConsoleStyle.Accent), _submit = SWConsoleStyle.Action("Отправить предложение",SWConsoleStyle.Accent);
    private SWGalacticMapControl _map = default!;
    private SWEmpireCouncilConsoleState? _state;
    private SWEmpireCouncilSphere? _sphere;
    private SWEmpireDiplomacyScope _scope = SWEmpireDiplomacyScope.World;
    private string? _sector, _system, _world, _target, _targetName, _pendingId, _transitSource, _transitDestination;
    private string[] _goodIds = Array.Empty<string>(), _sourceIds = Array.Empty<string>();
    private SWEmpireDiplomacyRequest? _pending;
    private bool _submitted, _breakPending, _cargoMode, _editTransitSource = true;
    private int? _selectedAgreement;
    private string _result = "", _agreementKey = "", _optionsKey = "";
    private bool CanEdit => _state?.Sphere == SWEmpireCouncilSphere.ExpansionDiplomacy;
    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 5 };
    public SWEmpireDiplomacyPanel(SWEmpireCouncilConsoleBoundUserInterface owner)
    {
        _owner=owner; Orientation=LayoutOrientation.Vertical; HorizontalExpand=VerticalExpand=true; SeparationOverride=4;
        _editor.Visible=false; _new.Visible=false; _budget.Visible=false;
        _geography=SWGalacticMapViews.ForFaction(_proto.Index<SWGalacticMapPrototype>("SenateRepublic"),"empire");
        var toolbar=new WrapContainer { HorizontalExpand=true,SeparationOverride=5,CrossSeparationOverride=3 }; AddChild(toolbar);
        var back=SWConsoleStyle.TabButton("Галактика"); back.OnPressed += _ => SwitchView(null,null); toolbar.AddChild(back); toolbar.AddChild(_title);
        var minus=SWConsoleStyle.TabButton("−"); minus.OnPressed += _ => _map.ChangeZoom(1/1.2f); toolbar.AddChild(minus); var plus=SWConsoleStyle.TabButton("+"); plus.OnPressed += _ => _map.ChangeZoom(1.2f); toolbar.AddChild(plus);
        var modes = new WrapContainer { HorizontalExpand = true, SeparationOverride = 5, CrossSeparationOverride = 3 }; AddChild(modes);
        var names = new[] { "Транзит", "Закупки", "Базирование", "Перемирие", "Грузы" };
        for (var i = 0; i < names.Length; i++) { var mode = i; var button = SWConsoleStyle.TabButton(names[i]); button.OnPressed += _ => SelectMode(mode); modes.AddChild(button); _modes.Add(button); }
        _side.AddChild(_budget); _side.AddChild(_new); _listScroll.AddChild(_agreements); _side.AddChild(_listScroll);
        _detail.AddChild(_editor); _detail.AddChild(_cargoList); _detailScroll.AddChild(_detail); _side.AddChild(_detailScroll);
        _new.OnPressed += _ => { if (!CanEdit) return; _editing=true; _selectedAgreement=null; _selectedCargo=null; _agreementKey=""; _cargoKey="initial"; Invalidate(); Refresh(); RefreshAgreements(); RefreshCargo(); };
        _host.VerticalExpand=true;
        AddChild(new SWAtlasLayout(_host,_side,stackBelow:520,minDetails:245,maxDetails:330,minMapHeight:180) { VerticalExpand=true });
        _editor.AddChild(SWConsoleStyle.Section("Дипломатия и поставки"));
        _editor.AddChild(_targetText);
        _transitFrom.OnPressed += _ => { _editTransitSource=true; Refresh(); }; _transitTo.OnPressed += _ => { _editTransitSource=false; Refresh(); };
        _transitFields.AddChild(_transitFrom); _transitFields.AddChild(_transitTo); _editor.AddChild(_transitFields);
        _sectorButton.OnPressed += _ => SelectSector(); _editor.AddChild(_sectorButton); _editor.AddChild(_terms);
        foreach (var (name,value) in new[] { ("Гражданский транзит",SWEmpireDiplomacyTerm.Transit),("Поставка из Республики",SWEmpireDiplomacyTerm.Supply),("Базирование",SWEmpireDiplomacyTerm.Basing),("Местное перемирие",SWEmpireDiplomacyTerm.LocalCeasefire) }) _term.AddItem(name,(int)value); _term.SelectId(0);
        _terms.AddChild(SWConsoleStyle.Wrap("Сторона",SWConsoleStyle.TextDim)); _party.AddItem("Республика",(int)SWEmpireDiplomacyParty.Republic); _party.AddItem("Нейтральная сторона",(int)SWEmpireDiplomacyParty.Neutral); _party.SelectId(0); _terms.AddChild(_party);
        _terms.AddChild(SWConsoleStyle.Wrap("Срок",SWConsoleStyle.TextDim)); _duration.AddItem("6 часов",6); _duration.AddItem("12 часов",12); _duration.SelectId(12); _terms.AddChild(_duration);
        _supply.AddChild(SWConsoleStyle.Wrap("Товар",SWConsoleStyle.TextDim)); _supply.AddChild(_goods); _supply.AddChild(_sourceLabel); _supply.AddChild(_source); _supply.AddChild(_amount); _editor.AddChild(_supply);
        _editor.AddChild(_quoteText); _routeAgreement.OnPressed += _ => AgreeRoute(); _editor.AddChild(_routeAgreement); _calculate.OnPressed += _ => Calculate(); _submit.OnPressed += _ => Submit(); _editor.AddChild(_calculate); _editor.AddChild(_submit);
        _term.OnItemSelected += e => { _term.SelectId(e.Id); _transitSource=null; _transitDestination=null; _editTransitSource=true; _target=null; _targetName=null; _world=null; Invalidate(); Refresh(); }; _party.OnItemSelected += e => { _party.SelectId(e.Id); Invalidate(); Refresh(); }; _duration.OnItemSelected += e => { _duration.SelectId(e.Id); Invalidate(); Refresh(); };
        _goods.OnItemSelected += e => { _goods.SelectId(e.Id); Invalidate(); Refresh(); }; _source.OnItemSelected += e => { _source.SelectId(e.Id); Invalidate(); Refresh(); }; _amount.OnTextChanged += _ => { Invalidate(); Refresh(); };
        SwitchView(null,null);
    }
    private void SwitchView(string? sector,string? system)
    {
        var zoom=_map?.Zoom ?? 1; _sector=sector; _system=system;
        var view=system!=null ? SWGalacticMapViews.System(_geography,system) : sector!=null ? SWGalacticMapViews.PoliticalSector(_geography,sector) : SWGalacticMapViews.Overview(_geography);
        _host.RemoveAllChildren(); _map=new SWGalacticMapControl(view,focusSystem:system,focusPoliticalSector:sector) { MinHeight=180 };
        _map.WorldSelected += id =>
        {
            if (!CanEdit || !_editing) return;
            var transit = !_cargoMode && (SWEmpireDiplomacyTerm)_term.SelectedId==SWEmpireDiplomacyTerm.Transit;
            if (transit)
            {
                if ((_editTransitSource && id == _transitSource) || (!_editTransitSource && id == _transitDestination)) return;
                if (_editTransitSource) _transitSource=id; else _transitDestination=id;
                _target=_transitDestination ?? _transitSource;
                _targetName=$"Откуда: {(_transitSource==null ? "не выбрано" : World(_transitSource))}\nКуда: {(_transitDestination==null ? "не выбрано" : World(_transitDestination))}";
            }
            else { _target=id; _targetName=World(id); }
            _world=id; _scope=SWEmpireDiplomacyScope.World; Invalidate(); Refresh();
        };
        _map.RouteSelected += route => { if (!CanEdit || !_editing) return; _world=null; _target=SWTransitLinkPrototype.LinkId(route.From,route.To); _transitSource=route.From; _transitDestination=route.To; _targetName=$"{World(route.From)} — {World(route.To)}"; _scope=SWEmpireDiplomacyScope.Link; Invalidate(); Refresh(); };
        _map.WorldActivated += id => { var world=view.Worlds.FirstOrDefault(w=>w.Id==id); if (world==null || id.StartsWith("public-sector:")) return; if (_sector==null && !string.IsNullOrEmpty(world.PoliticalSectorId)) SwitchView(world.PoliticalSectorId,null); else if (_system==null && !string.IsNullOrEmpty(world.SystemId)) SwitchView(_sector,world.SystemId); };
        if (Math.Abs(zoom-1)>.001f) _map.ChangeZoom(zoom); if (_world!=null) _map.SelectWorld(_world);
        if (_pendingId != null && _state?.ResultRequestId == _pendingId && _state.DiplomacyQuote.Allowed) _map.SetPublishedDefenseRoute(_state.DiplomacyQuote.Request.RouteWorlds);
        _host.AddChild(_map); _title.Text=system!=null ? "Система" : sector!=null ? "Сектор" : "Галактика";
    }
    private string World(string? id) => _geography.Worlds.FirstOrDefault(w=>w.Id==id)?.Name ?? "Мир";
    private void SelectSector()
    {
        if (!CanEdit || !_editing) return;
        var world=_geography.Worlds.FirstOrDefault(w=>w.Id==_world); if (world==null || string.IsNullOrEmpty(world.PoliticalSectorId)) return;
        _target=world.PoliticalSectorId; _targetName=world.PoliticalSectorName; _scope=SWEmpireDiplomacyScope.Sector; Invalidate(); Refresh();
    }
    public void SetState(SWEmpireCouncilConsoleState state)
    {
        if (_sphere!=state.Sphere)
        {
            var wasEditing = _editing;
            _editing=false; _pending=null; _pendingId=null; _submitted=false; _breakPending=false; _result="";
            _target=null; _targetName=null; _world=null; _transitSource=null; _transitDestination=null;
            _cargoTarget=null; _cargoTargetName=null; _cargoWorld=null; _scope=SWEmpireDiplomacyScope.World;
            _editTransitSource=true; _party.SelectId(0); _duration.SelectId(12); _amount.Text="";
            if (_goodIds.Length>0) _goods.SelectId(0);
            if (_sourceIds.Length>0) _source.SelectId(0);
            if (wasEditing) _map.SetPublishedDefenseRoute(Array.Empty<string>());
            _agreementKey="";
        }
        _sphere=state.Sphere; _state=state;
        if (_breakPending && state.ActionResultIsCommand && string.IsNullOrEmpty(state.ResultRequestId)) { _result=state.Result; _breakPending=false; }
        if (_submitted && state.ActionResultIsCommand && _pendingId==state.ResultRequestId) { _result=state.Result; _submitted=false; _pending=null; _pendingId=null; }
        if (_pendingId != null && state.ResultRequestId == _pendingId && !state.ActionResultIsCommand && state.DiplomacyQuote.Request.RouteWorlds.Count > 0)
            _map.SetPublishedDefenseRoute(state.DiplomacyQuote.Request.RouteWorlds);
        RefreshOptions(); Refresh(); RefreshAgreements(); RefreshCargo();
    }
    private void RefreshOptions()
    {
        if (_state==null) return;
        var goods=_proto.EnumeratePrototypes<SWCommodityPrototype>().OrderBy(g=>g.Name).ToArray();
        var worlds=_state.Economy.Worlds.Select(w=>w.World).Distinct().OrderBy(World).ToArray();
        var key=string.Join("|",goods.Select(g=>g.ID))+":"+string.Join("|",worlds); if (key==_optionsKey) return; _optionsKey=key;
        var selectedGood=_goods.SelectedId>=0 && _goods.SelectedId<_goodIds.Length ? _goodIds[_goods.SelectedId] : "";
        var selectedSource=_source.SelectedId>=0 && _source.SelectedId<_sourceIds.Length ? _sourceIds[_source.SelectedId] : "";
        _goodIds=goods.Select(g=>g.ID).ToArray(); _sourceIds=worlds; _goods.Clear(); _source.Clear();
        for (var i=0;i<goods.Length;i++) _goods.AddItem(goods[i].Name,i); for(var i=0;i<worlds.Length;i++) _source.AddItem(World(worlds[i]),i);
        var goodIndex=Array.IndexOf(_goodIds,selectedGood); var sourceIndex=Array.IndexOf(_sourceIds,selectedSource);
        if (_goodIds.Length>0) _goods.SelectId(Math.Max(0,goodIndex)); if (_sourceIds.Length>0) _source.SelectId(Math.Max(0,sourceIndex));
    }
    private void Invalidate() { _pending=null; _pendingId=null; _submitted=false; _result=""; _map?.SetPublishedDefenseRoute(Array.Empty<string>()); }
    private void Refresh()
    {
        if (_state==null) return; var own=CanEdit; _editor.Visible=own && _editing; _budget.Visible=own; _new.Visible=own; _new.Text=_cargoMode ? "Новая отправка" : "Новое соглашение";
        for (var i=0;i<_modes.Count;i++) SWConsoleStyle.StyleTabButton(_modes[i], i==(_cargoMode ? 4 : _term.SelectedId));
        var purchase = !_cargoMode && (SWEmpireDiplomacyTerm)_term.SelectedId==SWEmpireDiplomacyTerm.Supply;
        _targetText.SetMessage(_targetName == null ? (_cargoMode ? "Выберите мир назначения на карте" : purchase ? "Выберите источник в Республике на карте" : (SWEmpireDiplomacyTerm)_term.SelectedId==SWEmpireDiplomacyTerm.Transit ? "Выберите поле начала или назначения, затем мир на карте; либо выберите переход" : "Выберите мир или переход на карте") : (_cargoMode ? $"Назначение: {_targetName}" : purchase ? $"Источник в Республике: {_targetName}" : _targetName));
        var transit = !_cargoMode && (SWEmpireDiplomacyTerm)_term.SelectedId==SWEmpireDiplomacyTerm.Transit;
        _transitFields.Visible=transit; SWConsoleStyle.StyleTabButton(_transitFrom,_editTransitSource); SWConsoleStyle.StyleTabButton(_transitTo,!_editTransitSource);
        _sectorButton.Visible=!_cargoMode && !transit; _sectorButton.Disabled=_world==null; _terms.Visible=!_cargoMode;
        _supply.Visible=_cargoMode || (SWEmpireDiplomacyTerm)_term.SelectedId==SWEmpireDiplomacyTerm.Supply;
        var stock = _cargoMode && _source.SelectedId>=0 && _source.SelectedId<_sourceIds.Length && _goods.SelectedId>=0 && _goods.SelectedId<_goodIds.Length
            ? _state.Economy.Worlds.FirstOrDefault(w=>w.World==_sourceIds[_source.SelectedId])?.Goods.FirstOrDefault(g=>g.Good==_goodIds[_goods.SelectedId])?.Stock : null;
        _sourceLabel.SetMessage(_cargoMode ? $"Имперский мир отправления\nНа складе: {stock.GetValueOrDefault():0.##}" : "Имперский мир назначения");
        
        _submit.Text=_cargoMode ? "Отправить груз" : "Отправить предложение";
        _budget.SetMessage($"Бюджет сферы: {_state.Budgets.GetValueOrDefault(SWEmpireCouncilSphere.ExpansionDiplomacy):N0} кр");
        var quoted=_pendingId!=null && _state.ResultRequestId==_pendingId && !_submitted;
        var q=_state.DiplomacyQuote; var affordable=q.Cost<=_state.Budgets.GetValueOrDefault(SWEmpireCouncilSphere.ExpansionDiplomacy);
        _routeAgreement.Visible=own && _cargoMode && quoted && !q.Allowed && q.NeedsTransit && !q.NegotiationUnsupported;
        var routeDescription = quoted && q.Request.RouteWorlds.Count > 0 ? "\nМаршрут: " + string.Join(" → ", q.Request.RouteWorlds.Select(World)) : "";
        var cargoDescription = _cargoMode && _pending != null ? $"\nСо склада: {(_proto.TryIndex<SWCommodityPrototype>(_pending.Good,out var good) ? good.Name : "товар")} {_pending.Amount:0.##}" : "";
        _quoteText.SetMessage(!string.IsNullOrEmpty(_result) ? _result : _submitted ? (_cargoMode ? "Отправка груза" : "Отправка предложения") : quoted ? $"Стоимость {q.Cost:N0} кр{cargoDescription}{routeDescription}\n{q.Reason}{(q.Allowed && !affordable ? "\nНедостаточно средств сферы" : "")}" : "Выберите условия и рассчитайте стоимость");
        _calculate.Disabled=!own || _submitted; _submit.Disabled=!own || !quoted || !q.Allowed || !affordable || _pending==null;
    }
    private void SelectMode(int mode)
    {
        if (CanEdit && _cargoMode) { _cargoTarget=_target; _cargoTargetName=_targetName; _cargoWorld=_world; }
        _cargoMode=mode==4;
        if (!_cargoMode) _term.SelectId(mode);
        _target=_cargoMode ? _cargoTarget : null; _targetName=_cargoMode ? _cargoTargetName : null; _world=_cargoMode ? _cargoWorld : null;
        _scope=SWEmpireDiplomacyScope.World; _editing=false; _selectedAgreement=null; _selectedCargo=null;
        _agreementKey=""; _cargoKey="initial"; Invalidate(); Refresh(); RefreshAgreements(); RefreshCargo();
    }
    private void AgreeRoute()
    {
        if (!CanEdit || !_editing || _state==null || _pending==null || !_state.DiplomacyQuote.NeedsTransit) return;
        var q=_state.DiplomacyQuote; var route=q.Request.RouteWorlds.ToArray();
        _cargoTarget=_target; _cargoTargetName=_targetName; _cargoWorld=_world;
        var missing=q.MissingTransitLinks.ToHashSet();
        var party=q.RequiredParty==SWEmpireDiplomacyParty.Neutral ? "neutral" : "republic";
        bool Compatible(string id) { var world=_geography.Worlds.FirstOrDefault(w=>w.Id==id); if (world==null) return false; var faction=_geography.GetWorldFaction(world); return faction=="empire" || faction==party; }
        var first=-1; var last=-1;
        for (var i=0; i<q.Request.RouteLinks.Count && i+1<route.Length; i++)
        {
            if (first<0)
            {
                if (!missing.Contains(q.Request.RouteLinks[i])) continue;
                if (!Compatible(route[i]) || !Compatible(route[i+1])) { _quoteText.SetMessage("Прямой переход между Республикой и нейтральной стороной пока нельзя согласовать."); return; }
                first=last=i;
            }
            else if (missing.Contains(q.Request.RouteLinks[i]) && Compatible(route[i]) && Compatible(route[i+1])) last=i;
            else break;
        }
        if (first<0) return;
        _cargoMode=false; _term.SelectId((int) SWEmpireDiplomacyTerm.Transit); _party.SelectId((int) q.RequiredParty);
        _transitSource=route[first]; _transitDestination=route[last+1];
        _target=_transitDestination; _world=_transitDestination; _scope=SWEmpireDiplomacyScope.World;
        _targetName=$"Откуда: {World(_transitSource)}\nКуда: {World(_transitDestination)}";
        _editing=true; _agreementKey=""; Invalidate(); _map.SetPublishedDefenseRoute(route); Refresh(); RefreshAgreements(); RefreshCargo();
    }
    private Button ListCard(string text, bool selected, Action action)
    {
        var button=SWConsoleStyle.TabButton(""); SWConsoleStyle.StyleTabButton(button,selected);
        var label=SWConsoleStyle.Wrap(text,SWConsoleStyle.TextBright,240); label.MouseFilter=MouseFilterMode.Ignore; button.AddChild(label);
        button.OnPressed += _ => action(); return button;
    }
    private static string TermName(SWEmpireDiplomacyTerm term) => term switch { SWEmpireDiplomacyTerm.Transit => "Транзит", SWEmpireDiplomacyTerm.Supply => "Закупка", SWEmpireDiplomacyTerm.Basing => "Базирование", _ => "Перемирие" };
    private static string StatusName(SWEmpireDiplomacyStatus status) => status switch { SWEmpireDiplomacyStatus.AwaitingApproval => "Ожидает решения", SWEmpireDiplomacyStatus.Negotiating => "На рассмотрении", SWEmpireDiplomacyStatus.Active => "Действует", SWEmpireDiplomacyStatus.Completed => "Завершено", SWEmpireDiplomacyStatus.Expired => "Истекло", SWEmpireDiplomacyStatus.Broken => "Расторгнуто", _ => "Отклонено" };
    private static string PhaseName(SWEmpireDiplomacyCargoPhase phase) => phase switch { SWEmpireDiplomacyCargoPhase.AwaitingApproval => "Ожидает решения", SWEmpireDiplomacyCargoPhase.InTransit => "В пути", SWEmpireDiplomacyCargoPhase.Paused => "Задержан", SWEmpireDiplomacyCargoPhase.DepositPending => "Прибытие", SWEmpireDiplomacyCargoPhase.Returning => "Возвращается", SWEmpireDiplomacyCargoPhase.Completed => "Доставлен", SWEmpireDiplomacyCargoPhase.Rejected => "Отклонён", _ => "Подготовка" };
    private void RefreshAgreements()
    {
        if (_state==null) return;
        var key=_cargoMode+":"+_term.SelectedId+":"+_selectedAgreement+":"+_selectedCargo+":"+_editing+":"+string.Join("|",_state.Diplomacy.Agreements.Select(a=>$"{a.Id}:{a.Status}:{a.Reason}:{a.EndsAt}"))+":"+string.Join("|",_state.Diplomacy.Cargo.Select(c=>$"{c.Id}:{c.Phase}:{c.CurrentWorld}:{c.Delivered}:{c.EtaHours}:{c.Reason}"));
        if (key==_agreementKey) return; _agreementKey=key;
        var listPos=_listScroll.VScroll; var detailPos=_detailScroll.VScroll; _agreements.RemoveAllChildren(); _cargoList.RemoveAllChildren();
        if (_cargoMode)
        {
            var cargos=_state.Diplomacy.Cargo.Where(c=>c.AgreementId<=0).ToArray();
            if (cargos.Length==0) _agreements.AddChild(SWConsoleStyle.Wrap("Рейсов пока нет",SWConsoleStyle.TextDim,240));
            foreach(var c in cargos.Reverse()) { var cargo=c; _agreements.AddChild(ListCard($"{PhaseName(c.Phase)}\nОткуда: {World(c.SourceWorld)}\nКуда: {World(c.DestinationWorld)}",c.Id==_selectedCargo,()=>{ _editing=false; _selectedCargo=cargo.Id; _agreementKey=""; Refresh(); RefreshAgreements(); })); }
            var selected=cargos.FirstOrDefault(c=>c.Id==_selectedCargo);
            if (!_editing && selected!=null) { var good=_proto.TryIndex<SWCommodityPrototype>(selected.Good,out var commodity) ? commodity.Name : "Товар"; _cargoList.AddChild(SWConsoleStyle.Wrap($"{good}: {selected.Delivered:0.##} / {selected.Amount:0.##}\nОткуда: {World(selected.SourceWorld)}\nКуда: {World(selected.DestinationWorld)}\n{PhaseName(selected.Phase)}\nОсталось: {selected.EtaHours:0.0} ч\n{selected.Reason}",SWConsoleStyle.TextBright,240)); }
        }
        else
        {
            var agreements=_state.Diplomacy.Agreements.Where(a=>a.Terms.Term==(SWEmpireDiplomacyTerm)_term.SelectedId).ToArray();
            if (agreements.Length==0) _agreements.AddChild(SWConsoleStyle.Wrap("Соглашений пока нет",SWConsoleStyle.TextDim,240));
            foreach(var a in agreements.Reverse()) { var agreement=a; var route=a.Terms.RouteWorlds; var from=route.Count>0 ? World(route[0]) : "не указано"; var to=route.Count>0 ? World(route[route.Count-1]) : a.Terms.Scope==SWEmpireDiplomacyScope.Sector ? _geography.Worlds.FirstOrDefault(w=>w.PoliticalSectorId==a.Terms.TargetId)?.PoliticalSectorName ?? "Сектор" : World(a.Terms.TargetId); _agreements.AddChild(ListCard($"{TermName(a.Terms.Term)}: {StatusName(a.Status)}\nОткуда: {from}\nКуда: {to}",a.Id==_selectedAgreement,()=>{ _editing=false; _selectedAgreement=agreement.Id; _agreementKey=""; _map.SetPublishedDefenseRoute(agreement.Terms.RouteWorlds); Refresh(); RefreshAgreements(); })); }
            var selected=agreements.FirstOrDefault(a=>a.Id==_selectedAgreement);
            if (!_editing && selected!=null) { var route=selected.Terms.RouteWorlds.Count>0 ? "\nМаршрут: "+string.Join(" → ",selected.Terms.RouteWorlds.Select(World)) : ""; _cargoList.AddChild(SWConsoleStyle.Wrap($"{TermName(selected.Terms.Term)}\n{selected.Reason}\nСрок: {selected.Terms.Hours} ч{route}",SWConsoleStyle.TextBright,240)); foreach (var shipment in _state.Diplomacy.Cargo.Where(c=>c.AgreementId==selected.Id)) { var good=_proto.TryIndex<SWCommodityPrototype>(shipment.Good,out var commodity) ? commodity.Name : "Товар"; _cargoList.AddChild(SWConsoleStyle.Wrap($"{good}: {shipment.Delivered:0.##} / {shipment.Amount:0.##}\nОткуда: {World(shipment.SourceWorld)}\nКуда: {World(shipment.DestinationWorld)}\n{PhaseName(shipment.Phase)}\nОсталось: {shipment.EtaHours:0.0} ч\n{shipment.Reason}",SWConsoleStyle.TextDim,240)); } if (CanEdit && selected.Status==SWEmpireDiplomacyStatus.Active) { var id=selected.Id; var button=SWConsoleStyle.TabButton("Расторгнуть"); button.OnPressed += _ => { if (!CanEdit) return; _breakPending=true; _owner.BreakDiplomacy(id); }; _cargoList.AddChild(button); } }
        }
        _listScroll.VScroll=listPos; _detailScroll.VScroll=detailPos;
    }
    private string _cargoKey = "initial";
    private void RefreshCargo() { }
    private void Calculate()
    {
        if (!CanEdit || !_editing) return;
        if (_target==null) { _quoteText.SetMessage("Выберите объект на карте"); return; }
        if (!_cargoMode && (SWEmpireDiplomacyTerm)_term.SelectedId==SWEmpireDiplomacyTerm.Transit && (_transitSource==null || _transitDestination==null)) { _quoteText.SetMessage("Выберите поле начала или назначения, затем мир на карте; либо выберите переход"); return; }
        if (_cargoMode && (_world==null || _scope!=SWEmpireDiplomacyScope.World)) { _quoteText.SetMessage("Выберите мир назначения на карте"); return; }
        var amount=0d; if ((_cargoMode || (SWEmpireDiplomacyTerm)_term.SelectedId==SWEmpireDiplomacyTerm.Supply) && (!double.TryParse(_amount.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out amount) || !double.IsFinite(amount) || amount<=0)) { _quoteText.SetMessage("Укажите количество поставки"); return; }
        _pending=new SWEmpireDiplomacyRequest { RequestId=Guid.NewGuid().ToString("N"), Action=_cargoMode ? SWEmpireDiplomacyAction.ShipCargo : SWEmpireDiplomacyAction.Propose, Term=(SWEmpireDiplomacyTerm)_term.SelectedId, Party=(SWEmpireDiplomacyParty)_party.SelectedId, Scope=_scope, TargetId=_target, Hours=_duration.SelectedId,
            Good=_goods.SelectedId>=0 && _goods.SelectedId<_goodIds.Length ? _goodIds[_goods.SelectedId] : "", SourceWorld=_cargoMode ? (_source.SelectedId>=0 && _source.SelectedId<_sourceIds.Length ? _sourceIds[_source.SelectedId] : "") : (SWEmpireDiplomacyTerm)_term.SelectedId==SWEmpireDiplomacyTerm.Transit ? _transitSource ?? "" : _world ?? "", DestinationWorld=_cargoMode ? _world ?? "" : (SWEmpireDiplomacyTerm)_term.SelectedId==SWEmpireDiplomacyTerm.Transit ? _transitDestination ?? "" : (_source.SelectedId>=0 && _source.SelectedId<_sourceIds.Length ? _sourceIds[_source.SelectedId] : ""), Amount=amount };
        _pendingId=_pending.RequestId; _submitted=false; _owner.QuoteDiplomacy(_pending); Refresh();
    }
    private void Submit() { if (!CanEdit || !_editing || _pending==null || _submitted) return; _submitted=true; _owner.SubmitDiplomacy(_pending); Refresh(); }
}
