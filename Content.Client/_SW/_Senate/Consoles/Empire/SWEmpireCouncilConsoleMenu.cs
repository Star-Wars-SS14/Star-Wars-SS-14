using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Client.UserInterface.Controls;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.Client._SW._Senate.Consoles.Statistics.SWStatisticsWidgets;

namespace Content.Client._SW._Senate.Consoles.Empire;

/// <summary>Imperial economic workspace. Drafts and scroll positions survive periodic updates.</summary>
public sealed class SWEmpireCouncilConsoleMenu : FancyWindow
{
    private readonly SWEmpireCouncilConsoleBoundUserInterface _owner;
    private readonly SWEmpireMilitaryPanel _military;
    private readonly IPrototypeManager _proto = IoCManager.Resolve<IPrototypeManager>();
    private readonly SWGalacticMapPrototype _geography;
    private readonly SWRepublicMapBrowser _atlas = new(compactEconomy: true, showMedicalToggle: false, viewerFaction: "empire");
    private readonly BoxContainer _pages = VerticalStack(0);
    private readonly Button[] _tabs = new Button[5];
    private readonly Control[] _panels = new Control[5];
    private readonly Label _role = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Label _treasury = SWConsoleStyle.Text("", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private readonly Label _feedback = SWConsoleStyle.Text("", SWConsoleStyle.Warn, SWConsoleStyle.Small);
    private readonly BoxContainer _orders = VerticalStack(6);
    private readonly ScrollContainer _orderScroll = new() { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
    private readonly BoxContainer _ordersPage = VerticalStack(0);
    private readonly ScrollContainer _deliveryScroll = new() { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
    private readonly SWConsoleColumns _orderColumns;
    private readonly BoxContainer _finance = VerticalStack(8);
    private readonly RichTextLabel _financeSummary = SWConsoleStyle.Wrap("", SWConsoleStyle.TextBright);
    private readonly OptionButton _recipient = new() { HorizontalExpand = true, MinWidth = 180 };
    private readonly LineEdit _transferAmount = new() { HorizontalExpand = true, PlaceHolder = "Сумма" };
    private readonly Button _transfer = SWConsoleStyle.Action("Перевести", SWConsoleStyle.Accent);
    private readonly Composer _delivery;
    private readonly Composer _development;
    private SWEmpireCouncilConsoleState? _state;
    private SWEmpireCouncilSphere? _currentSphere;
    private int _tab;
    private string _ordersKey = "initial";
    private string _financeKey = string.Empty;
    private bool _transferConfirm;
    private bool _initialWorldChosen;
    private string? _transferRequest;
    private string? _rejectRequest;

    public SWEmpireCouncilConsoleMenu(SWEmpireCouncilConsoleBoundUserInterface owner)
    {
        _owner = owner;
        _military = new SWEmpireMilitaryPanel(owner);
        _geography = _proto.Index<SWGalacticMapPrototype>("SenateRepublic");
        Title = "Совет Империи";
        SetSize = new Vector2(1120, 720);
        MinSize = new Vector2(760, 580);
        var root = new PanelContainer { HorizontalExpand = true, VerticalExpand = true };
        ContentsContainer.AddChild(root);
        SWConsoleStyle.StyleRoot(root);
        var layout = VerticalStack(0);
        layout.VerticalExpand = true;
        root.AddChild(layout);
        var header = new PanelContainer { HorizontalExpand = true };
        SWConsoleStyle.StyleHeader(header);
        var headerRow = new BoxContainer { Margin = new Thickness(14, 10), SeparationOverride = 12, HorizontalExpand = true };
        header.AddChild(headerRow);
        var heading = VerticalStack(2);
        heading.HorizontalExpand = true;
        heading.AddChild(SWConsoleStyle.Text("СОВЕТ ИМПЕРИИ", SWConsoleStyle.Bad, SWConsoleStyle.Small));
        heading.AddChild(SWConsoleStyle.Text("Сферы Совета", SWConsoleStyle.TextBright, SWConsoleStyle.Header));
        headerRow.AddChild(heading);
        var balance = VerticalStack(2);
        balance.AddChild(_treasury);
        balance.AddChild(_role);
        headerRow.AddChild(balance);
        layout.AddChild(header);
        var bar = new WrapContainer { Margin = new Thickness(8, 4), HorizontalExpand = true, SeparationOverride = 5 };
        var names = new[] { "Карта", "Заказы", "Развитие", "Финансы", "Флот" };
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            _tabs[i] = SWConsoleStyle.TabButton(names[i]);
            _tabs[i].Name = $"EmpireTab{i}";
            _tabs[i].OnPressed += _ => ShowTab(index);
            bar.AddChild(_tabs[i]);
        }
        layout.AddChild(bar);
        _feedback.Margin = new Thickness(14, 0, 14, 4);
        layout.AddChild(_feedback);
        _pages.VerticalExpand = true;
        _pages.Margin = new Thickness(12, 4, 12, 10);
        layout.AddChild(_pages);
        _atlas.VerticalExpand = true;
        _atlas.EconomyDetailsOverride = MapDetails;
        _panels[0] = _atlas;
        _orderScroll.AddChild(_orders);
        _delivery = new Composer(this, delivery: true);
        _development = new Composer(this, delivery: false);
        _deliveryScroll.AddChild(_delivery);
        _orderColumns = new SWConsoleColumns(_orderScroll, _deliveryScroll, 400, 730) { VerticalExpand = true };
        _ordersPage.VerticalExpand = true;
        _ordersPage.AddChild(_orderColumns);
        _panels[1] = _ordersPage;
        _panels[2] = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        ((ScrollContainer) _panels[2]).AddChild(new SWConsoleColumns(_development, _development.QuotePanel, 560, 900, fixedFirst: true));
        _panels[3] = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        ((ScrollContainer) _panels[3]).AddChild(_finance);
        BuildFinance();
        _panels[4] = _military;
        foreach (var page in _panels) _pages.AddChild(page);
        UpdateTabs(SWEmpireCouncilSphere.None);
    }

    private bool CanManage => _state?.Sphere == SWEmpireCouncilSphere.ProductionLogistics;

    private void UpdateTabs(SWEmpireCouncilSphere sphere)
    {
        var valid = sphere is SWEmpireCouncilSphere.ProductionLogistics or SWEmpireCouncilSphere.MilitaryOffense
            or SWEmpireCouncilSphere.Intelligence or SWEmpireCouncilSphere.ExpansionDiplomacy;
        for (var i = 0; i < _tabs.Length; i++)
            _tabs[i].Visible = valid && (i != 2 || sphere == SWEmpireCouncilSphere.ProductionLogistics)
                                     && (i != 4 || sphere == SWEmpireCouncilSphere.MilitaryOffense);
        _tabs[1].Text = sphere == SWEmpireCouncilSphere.ProductionLogistics ? "Заказы" : "Заявки";
        var production = sphere == SWEmpireCouncilSphere.ProductionLogistics;
        if (production && _deliveryScroll.Parent == _ordersPage)
        {
            _ordersPage.RemoveChild(_deliveryScroll);
            _orderColumns.AddChild(_deliveryScroll);
            _ordersPage.AddChild(_orderColumns);
        }
        else if (!production && _deliveryScroll.Parent == _orderColumns)
        {
            _orderColumns.RemoveChild(_deliveryScroll);
            _ordersPage.RemoveChild(_orderColumns);
            _ordersPage.AddChild(_deliveryScroll);
        }
        if (!valid)
        {
            _tab = -1;
            foreach (var panel in _panels) panel.Visible = false;
            return;
        }
        ShowTab(sphere == SWEmpireCouncilSphere.MilitaryOffense ? 4 : 0);
    }

    private void ShowTab(int tab)
    {
        if (tab < 0 || tab >= _tabs.Length || !_tabs[tab].Visible) return;
        _tab = tab;
        for (var i = 0; i < _tabs.Length; i++)
        {
            _panels[i].Visible = i == tab;
            SWConsoleStyle.StyleTabButton(_tabs[i], i == tab);
        }
    }

    public void SetState(SWEmpireCouncilConsoleState state)
    {
        var previousSphere = _currentSphere;
        _currentSphere = state.Sphere;
        _state = state;
        _military.SetState(state);
        if (previousSphere != state.Sphere)
        {
            ResetTransfer();
            _transferRequest = null; _rejectRequest = null; _feedback.Text = string.Empty;
            _transferAmount.SetText(string.Empty);
            UpdateTabs(state.Sphere);
        }
        _treasury.Text = $"Казна  {state.Treasury:N0} кр";
        _role.Text = SphereName(state.Sphere);
        _atlas.SetEconomy(state.Economy);
        if (!_initialWorldChosen)
        {
            var initial = state.Economy.Worlds.FirstOrDefault(w =>
                _proto.TryIndex<SWPlanetEconomyPrototype>(w.World, out var profile) && profile.ActiveEconomy
                && _geography.Worlds.Any(g => g.Id == w.World && g.Overview && _geography.GetWorldFaction(g) == "empire"));
            if (initial != null) { _initialWorldChosen = true; _atlas.SelectWorld(initial.World); }
        }
        _delivery.Update(state);
        _development.Update(state);
        RefreshOrders();
        RefreshFinance();
        if (_rejectRequest != null && state.ResultRequestId == _rejectRequest)
        {
            _feedback.Text = state.Result;
            _rejectRequest = null;
        }
        if (_transferRequest != null && state.ResultRequestId == _transferRequest)
        {
            _feedback.Text = state.Result;
            _transferRequest = null;
            _transferConfirm = false;
            _transfer.Text = "Перевести";
        }
    }

    private void MapDetails(BoxContainer heading, BoxContainer detail, string? id, SWGalacticMapRoute? route)
    {
        if (route != null)
        {
            heading.AddChild(SWConsoleStyle.Wrap($"{World(route.From)} — {World(route.To)}", SWConsoleStyle.Brass));
            if (CanManage)
            {
                var expand = SWConsoleStyle.TabButton("Развитие маршрута");
                expand.ToolTip = "Рассчитать увеличение пропускной способности";
                expand.OnPressed += _ => { _development.SelectRoute(route.From, SWTransitLinkPrototype.LinkId(route.From, route.To)); ShowTab(2); };
                detail.AddChild(expand);
            }
            return;
        }
        if (id == null) { heading.AddChild(SWConsoleStyle.Wrap("Выберите мир", SWConsoleStyle.TextDim)); return; }
        var world = _state?.Economy.Worlds.FirstOrDefault(w => w.World == id);
        heading.AddChild(SWConsoleStyle.Wrap(_atlas.GetWorldName(id), SWConsoleStyle.Brass));
        if (world == null) { detail.AddChild(SWConsoleStyle.Wrap("Данные хозяйства недоступны", SWConsoleStyle.TextDim)); return; }
        if (world.Capacity is { } capacity)
        {
            detail.AddChild(SWConsoleStyle.Section("Мощности"));
            detail.AddChild(SWConsoleStyle.Wrap($"Энергия  {Number(capacity.ReactorCapacityPower)}\nБытовая нагрузка  {Number(capacity.DomesticPower)}\nТруд  {Number(capacity.LaborCapacity)}", SWConsoleStyle.TextBright));
        }
        var stocks = world.Goods.Where(g => g.Stock > 0).OrderByDescending(g => g.Stock).Take(5).ToArray();
        if (stocks.Length > 0)
        {
            detail.AddChild(SWConsoleStyle.Section("Запасы"));
            foreach (var stock in stocks) detail.AddChild(SWConsoleStyle.Wrap($"{Good(stock.Good)}  {Number(stock.Stock)}", SWConsoleStyle.TextDim));
        }
        var projects = _state?.Projects.Where(p => p.World == id || p.Source == id || p.Target == id).ToArray() ?? Array.Empty<SWEmpireIndustryProjectView>();
        if (projects.Length > 0)
        {
            detail.AddChild(SWConsoleStyle.Section("Проекты и заказы"));
            foreach (var project in projects.Take(4)) detail.AddChild(SWConsoleStyle.Wrap($"{ActionName(project.Action)}  {project.Status}", SWConsoleStyle.TextDim));
        }
        var ship = SWConsoleStyle.TabButton(CanManage ? "Адресная поставка" : "Материальная заявка");
        ship.Disabled = _state?.Sphere is null or SWEmpireCouncilSphere.None;
        ship.ToolTip = "Оформить доставку с этого мира";
        ship.OnPressed += _ => { if (CanManage) _delivery.SelectWorld(id); else _delivery.SelectDestination(id); ShowTab(1); };
        detail.AddChild(ship);
        if (CanManage)
        {
            var develop = SWConsoleStyle.TabButton("Развитие мира");
            develop.ToolTip = "Рассчитать проект";
            develop.OnPressed += _ => { _development.SelectWorld(id); ShowTab(2); };
            detail.AddChild(develop);
        }
    }

    private void RefreshOrders()
    {
        if (_state == null) return;
        var key = _state.Sphere + ":" + string.Join("|", _state.Projects.Select(p => $"{p.Id}:{p.Status}:{p.Source}:{p.AwaitingSource}:{p.Sent}:{p.InTransit}:{p.Delivered}:{p.EtaHours}:{p.Reason}"));
        if (_ordersKey == key) return;
        _ordersKey = key;
        var scroll = _orderScroll.VScroll;
        _orders.RemoveAllChildren();
        _orders.AddChild(SWConsoleStyle.Section("Текущие заказы"));
        if (_state.Projects.Count == 0) _orders.AddChild(SWConsoleStyle.Wrap("Заказов пока нет", SWConsoleStyle.TextDim));
        foreach (var p in _state.Projects.AsEnumerable().Reverse())
        {
            var row = SWConsoleStyle.Row();
            var body = VerticalStack(4);
            row.AddChild(body);
            var target = World(p.Target.Length > 0 ? p.Target : p.World);
            body.AddChild(SWConsoleStyle.Wrap(ActionName(p.Action), SWConsoleStyle.TextBright));
            body.AddChild(SWConsoleStyle.Wrap(target, SWConsoleStyle.Brass));
            if (p.Recipe.Length > 0 && _proto.TryIndex<SWProductionRecipePrototype>(p.Recipe, out var production))
            {
                var facility = _proto.Index(production.Facility).Name;
                var products = string.Join(", ", production.Outputs.Keys.Select(id => _proto.Index(id).Name));
                body.AddChild(SWConsoleStyle.Wrap($"{facility} — {products}", SWConsoleStyle.TextDim));
            }
            if (p.Link.Length > 0 && _proto.TryIndex<SWTransitLinkPrototype>(p.Link, out var link))
                body.AddChild(SWConsoleStyle.Wrap($"{World(link.From)} — {World(link.To)}", SWConsoleStyle.TextDim));
            if (p.Action is SWEmpireIndustryAction.UpgradeLine or SWEmpireIndustryAction.CreateLine or SWEmpireIndustryAction.ExpandRoute or SWEmpireIndustryAction.Concentrate)
            {
                if (p.AddedRate > 0) body.AddChild(SWConsoleStyle.Wrap($"Прирост  {Number(p.AddedRate)} {(p.Action == SWEmpireIndustryAction.ExpandRoute ? "мест/ч" : "циклов/ч")}", SWConsoleStyle.TextDim));
                if (p.WorkHours > 0)
                {
                    body.AddChild(SWConsoleStyle.Wrap($"Выполнено  {Number(p.WorkedHours)} из {Number(p.WorkHours)} ч", SWConsoleStyle.TextDim));
                    body.AddChild(new SWStatisticsBar { Value = (float) p.WorkedHours, Minimum = 0, Maximum = (float) p.WorkHours, Fill = SWConsoleStyle.Accent });
                }
                if (p.Action == SWEmpireIndustryAction.Concentrate)
                    body.AddChild(SWConsoleStyle.Wrap("Остальные направления временно работают с половинной мощностью", SWConsoleStyle.TextDim));
            }
            if (p.Good.Length > 0) body.AddChild(SWConsoleStyle.Wrap($"{Good(p.Good)}   {Number(p.Delivered)} / {Number(p.Amount)}", SWConsoleStyle.Brass));
            if (p.Action == SWEmpireIndustryAction.Ship && !string.IsNullOrEmpty(p.Source))
                body.AddChild(SWConsoleStyle.Wrap($"Источник  {World(p.Source)}", SWConsoleStyle.TextDim));
            if (p.Urgent) body.AddChild(SWConsoleStyle.Wrap("Срочный", SWConsoleStyle.Warn));
            if (p.Action == SWEmpireIndustryAction.Ship)
            {
                var metrics = new BoxContainer { HorizontalExpand = true, SeparationOverride = 8 };
                foreach (var (label, value) in new[] { ("Отправлено", p.Sent), ("В пути", p.InTransit), ("Доставлено", p.Delivered) })
                {
                    var metric = VerticalStack(2); metric.HorizontalExpand = true;
                    metric.AddChild(SWConsoleStyle.Text(label, SWConsoleStyle.TextDim, SWConsoleStyle.Small));
                    metric.AddChild(SWConsoleStyle.Text(Number(value), SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
                    metrics.AddChild(metric);
                }
                body.AddChild(metrics);
                body.AddChild(new SWStatisticsBar { Value = (float) p.Delivered, Minimum = 0, Maximum = (float) Math.Max(1, p.Amount), Fill = SWConsoleStyle.Accent });
            }
            body.AddChild(SWConsoleStyle.Wrap($"{SphereName(p.SourceSphere)}\nФинансирование  {p.Cost:N0} кр", SWConsoleStyle.TextDim));
            if (p.DeadlineHours > 0) body.AddChild(SWConsoleStyle.Wrap($"Срок  {Number(p.DeadlineHours)} ч", SWConsoleStyle.TextDim));
            if (p.Action == SWEmpireIndustryAction.Ship) body.AddChild(SWConsoleStyle.Wrap(Eta(p.EtaHours), SWConsoleStyle.TextDim));
            body.AddChild(SWConsoleStyle.Wrap(p.Status, SWConsoleStyle.TextDim));
            if (!string.IsNullOrWhiteSpace(p.Reason)) body.AddChild(SWConsoleStyle.Wrap(p.Reason, SWConsoleStyle.Warn));
            if (p.AwaitingSource && CanManage)
            {
                var accept = SWConsoleStyle.TabButton("Выбрать источник");
                accept.OnPressed += _ => _delivery.SelectPending(p);
                body.AddChild(accept);
                var reject = SWConsoleStyle.TabButton("Отклонить заявку");
                reject.OnPressed += _ =>
                {
                    reject.Disabled = true;
                    _rejectRequest = p.Id;
                    _owner.RejectMaterial(p.Id);
                    _feedback.Text = "Решение отправлено";
                };
                body.AddChild(reject);
            }
            _orders.AddChild(row);
        }
        _orderScroll.VScroll = scroll;
    }

    private void BuildFinance()
    {
        _finance.AddChild(SWConsoleStyle.Section("Казна и полномочия"));
        _finance.AddChild(_financeSummary);
        _finance.AddChild(SWConsoleStyle.Section("Перевод между сферами"));
        foreach (var sphere in Spheres) _recipient.AddItem(SphereName(sphere), (int) sphere);
        _finance.AddChild(_recipient);
        _finance.AddChild(_transferAmount);
        _transferAmount.OnTextChanged += _ => ResetTransfer();
        _recipient.OnItemSelected += args => { _recipient.SelectId(args.Id); ResetTransfer(); };
        _transfer.OnPressed += _ =>
        {
            if (_state == null || !int.TryParse(_transferAmount.Text, out var amount) || amount <= 0 || _recipient.SelectedId < 0) return;
            if (!_transferConfirm) { _transferConfirm = true; _transfer.Text = $"Подтвердить перевод {amount:N0} кр"; return; }
            _transferRequest = Guid.NewGuid().ToString("N");
            _owner.Transfer((SWEmpireCouncilSphere) _recipient.SelectedId, amount, _transferRequest);
            _transfer.Disabled = true;
            _feedback.Text = "Перевод отправлен";
        };
        _finance.AddChild(_transfer);
    }

    private void ResetTransfer() { _transferConfirm = false; _transfer.Text = "Перевести"; RefreshFinance(); }
    private void RefreshFinance()
    {
        if (_state == null) return;
        var key = $"{_state.Treasury}:{_state.IncomePerHour}:{_state.Allocated}:{_state.TotalFunds}:{_state.DailyAllocation}:{_state.NextAllocationDay}:" + string.Join("|", Spheres.Select(s => $"{_state.Budgets.GetValueOrDefault(s)}:{_state.Spent.GetValueOrDefault(s)}:{_state.Received.GetValueOrDefault(s)}"));
        if (_financeKey != key)
        {
            _financeKey = key;
            var summary = $"Казна  {_state.Treasury:N0} кр\nДоход  {Number(_state.IncomePerHour)} кр/ч\n"
                + $"Распределено  {_state.Allocated:N0} кр\nВсего  {_state.TotalFunds:N0} кр\n"
                + $"Дневное начисление  {_state.DailyAllocation:N0} кр\nСледующее начисление  день {_state.NextAllocationDay}\n\n"
                + string.Join("\n\n", Spheres.Select(s => $"{SphereName(s)}\nЛимит  {_state.Budgets.GetValueOrDefault(s):N0} кр\nПолучено  {_state.Received.GetValueOrDefault(s):N0} кр\nРасход  {_state.Spent.GetValueOrDefault(s):N0} кр"));
            var message = new Robust.Shared.Utility.FormattedMessage();
            message.PushColor(SWConsoleStyle.TextBright); message.AddText(summary); message.Pop();
            _financeSummary.SetMessage(message);
        }
        var own = _state.Sphere;
        var valid = own != SWEmpireCouncilSphere.None && Spheres.Any(s => (int) s == _recipient.SelectedId) && (int) own != _recipient.SelectedId
            && int.TryParse(_transferAmount.Text, out var amount) && amount > 0 && amount <= _state.Budgets.GetValueOrDefault(own);
        _transfer.Disabled = !valid || _transferRequest != null;
        _transfer.ToolTip = valid ? "Перевод из лимита вашей сферы" : "Выберите другую сферу и сумму в пределах своего лимита";
    }

    private static readonly SWEmpireCouncilSphere[] Spheres = { SWEmpireCouncilSphere.ProductionLogistics, SWEmpireCouncilSphere.MilitaryOffense, SWEmpireCouncilSphere.Intelligence, SWEmpireCouncilSphere.ExpansionDiplomacy };
    private static string SphereName(SWEmpireCouncilSphere sphere) => sphere switch
    {
        SWEmpireCouncilSphere.ProductionLogistics => "Производство и логистика",
        SWEmpireCouncilSphere.MilitaryOffense => "Военное наступление",
        SWEmpireCouncilSphere.Intelligence => "Разведка",
        SWEmpireCouncilSphere.ExpansionDiplomacy => "Экспансия и дипломатия",
        _ => "Нет полномочий",
    };
    private static string ActionName(SWEmpireIndustryAction action) => action switch
    {
        SWEmpireIndustryAction.UpgradeLine => "Расширение предприятия",
        SWEmpireIndustryAction.CreateLine => "Новое предприятие",
        SWEmpireIndustryAction.ExpandRoute => "Расширение маршрута",
        SWEmpireIndustryAction.Ship => "Адресная поставка",
        SWEmpireIndustryAction.Concentrate => "Концентрация производства",
        _ => "Заказ",
    };
    private string World(string id) => string.IsNullOrEmpty(id) ? "Не указан" : _atlas.GetWorldName(id);
    private string Good(string id) => _proto.TryIndex<SWCommodityPrototype>(id, out var good) ? good.Name : "Товар";
    private static string Number(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
    private static string Eta(double? hours) => hours is { } h ? $"До прибытия {Number(h)} ч" : "Время не определено";

    private sealed class Composer : BoxContainer
    {
        private readonly SWEmpireCouncilConsoleMenu _menu;
        private readonly bool _delivery;
        private readonly Label _composerTitle = SWConsoleStyle.Section(string.Empty);
        private readonly OptionButton _action = new() { HorizontalExpand = true };
        private readonly OptionButton _world = new() { HorizontalExpand = true, Filterable = true };
        private readonly OptionButton _target = new() { HorizontalExpand = true, Filterable = true };
        private readonly OptionButton _good = new() { HorizontalExpand = true, Filterable = true };
        private readonly OptionButton _enterprise = new() { HorizontalExpand = true, Filterable = true };
        private readonly OptionButton _route = new() { HorizontalExpand = true, Filterable = true };
        private readonly OptionButton _priority = new() { HorizontalExpand = true };
        private readonly LineEdit _amount = new() { HorizontalExpand = true, Text = "1" };
        private readonly LineEdit _deadline = new() { HorizontalExpand = true, Text = "4" };
        private readonly Label _stock = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
        private readonly RichTextLabel _quote = SWConsoleStyle.Wrap("Ожидает расчёта", SWConsoleStyle.TextDim);
        public BoxContainer QuotePanel { get; } = VerticalStack(8);
        private readonly Button _calculate = SWConsoleStyle.Action("Рассчитать", SWConsoleStyle.Accent);
        private readonly Button _confirm = SWConsoleStyle.Action("Подтвердить", SWConsoleStyle.Brass, disabled: true);
        private readonly Button _cancelAcceptance = SWConsoleStyle.TabButton("Другая поставка");
        private readonly BoxContainer _actionField, _worldField, _targetField, _goodField, _enterpriseField, _routeField, _amountField, _priorityField, _deadlineField;
        private string[] _worlds = Array.Empty<string>(), _goods = Array.Empty<string>(), _recipes = Array.Empty<string>(), _routes = Array.Empty<string>();
        private string _worldId = string.Empty, _targetId = string.Empty, _goodId = string.Empty, _recipeId = string.Empty, _routeId = string.Empty;
        private string _catalogKey = string.Empty, _detailKey = string.Empty;
        private SWEmpireIndustryRequest? _pending;
        private SWEmpireMaterialRequest? _materialPending;
        private SWEmpireIndustryProjectView? _acceptOrder;
        private bool _submitted;
        private bool CanSubmit => _state != null && (_delivery ? _state.Sphere != SWEmpireCouncilSphere.None : _menu.CanManage);
        private SWEmpireCouncilConsoleState? _state;
        private SWEmpireCouncilSphere _lastSphere;

        public Composer(SWEmpireCouncilConsoleMenu menu, bool delivery)
        {
            _menu = menu;
            _delivery = delivery;
            Orientation = LayoutOrientation.Vertical;
            HorizontalExpand = true;
            SeparationOverride = 6;
            _composerTitle.Text = delivery ? "Новая поставка" : "Материальный проект";
            AddChild(_composerTitle);
            var actions = delivery
                ? new[] { SWEmpireIndustryAction.Ship }
                : new[] { SWEmpireIndustryAction.UpgradeLine, SWEmpireIndustryAction.CreateLine, SWEmpireIndustryAction.ExpandRoute, SWEmpireIndustryAction.Concentrate };
            foreach (var action in actions) _action.AddItem(ActionName(action), (int) action);
            _action.SelectId((int) actions[0]);
            _world.Name = delivery ? "EmpireDeliverySource" : "EmpireDevelopmentWorld";
            _target.Name = "EmpireDeliveryTarget";
            _good.Name = "EmpireDeliveryGood";
            _amount.Name = delivery ? "EmpireDeliveryAmount" : "EmpireDevelopmentAmount";
            _deadline.Name = delivery ? "EmpireDeliveryDeadline" : "EmpireDevelopmentDeadline";
            _calculate.Name = delivery ? "EmpireDeliveryQuote" : "EmpireDevelopmentQuote";
            _confirm.Name = delivery ? "EmpireDeliveryConfirm" : "EmpireDevelopmentConfirm";
            _actionField = Field(this, "Действие", _action);
            _worldField = Field(this, delivery ? "Источник" : "Мир", _world);
            _targetField = Field(this, "Получатель", _target);
            _goodField = Field(this, "Товар", _good);
            _enterpriseField = Field(this, "Предприятие", _enterprise);
            _routeField = Field(this, "Маршрут", _route);
            _amountField = Field(this, delivery ? "Количество" : "Прирост мощности", _amount);
            _priority.AddItem("Обычный", 0); _priority.AddItem("Срочный", 1); _priority.SelectId(0);
            _priorityField = Field(this, "Приоритет", _priority);
            _deadlineField = Field(this, "Срок, ч", _deadline);
            AddChild(_stock);
            if (_delivery) AddChild(_quote);
            else
            {
                QuotePanel.AddChild(SWConsoleStyle.Section("Оценка проекта"));
                QuotePanel.AddChild(_quote);
            }
            var buttons = new WrapContainer { HorizontalExpand = true, SeparationOverride = 6, CrossSeparationOverride = 6 };
            buttons.AddChild(_calculate); buttons.AddChild(_confirm); buttons.AddChild(_cancelAcceptance); AddChild(buttons);
            _cancelAcceptance.OnPressed += _ => { _acceptOrder = null; _detailKey = string.Empty; RebuildDetails(); Invalidate(); };
            _action.OnItemSelected += args => { _action.SelectId(args.Id); _detailKey = string.Empty; RebuildDetails(); Invalidate(); };
            _world.OnItemSelected += args => { if (args.Id < 0 || args.Id >= _worlds.Length) return; _world.SelectId(args.Id); _worldId = _worlds[args.Id]; _detailKey = string.Empty; RebuildDetails(); Invalidate(); };
            _target.OnItemSelected += args => { if (args.Id < 0 || args.Id >= _worlds.Length) return; _target.SelectId(args.Id); _targetId = _worlds[args.Id]; Invalidate(); };
            _good.OnItemSelected += args => { if (args.Id < 0 || args.Id >= _goods.Length) return; _good.SelectId(args.Id); _goodId = _goods[args.Id]; UpdateStock(); Invalidate(); };
            _enterprise.OnItemSelected += args => { if (args.Id < 0 || args.Id >= _recipes.Length) return; _enterprise.SelectId(args.Id); _recipeId = _recipes[args.Id]; Invalidate(); };
            _route.OnItemSelected += args => { if (args.Id < 0 || args.Id >= _routes.Length) return; _route.SelectId(args.Id); _routeId = _routes[args.Id]; Invalidate(); };
            _priority.OnItemSelected += args => { _priority.SelectId(args.Id); Invalidate(); };
            _amount.OnTextChanged += _ => Invalidate();
            _deadline.OnTextChanged += _ => Invalidate();
            _calculate.OnPressed += _ => Calculate();
            _confirm.OnPressed += _ =>
            {
                var expected = _materialPending?.RequestId ?? _pending?.RequestId;
                if (expected == null || _state?.ResultRequestId != expected || _state.Quote.Allowed != true || !CanSubmit) return;
                _submitted = true;
                _confirm.Disabled = true;
                if (_materialPending != null) _menu._owner.SubmitMaterial(_materialPending);
                else _menu._owner.Submit(_pending!);
                SetQuote("Заказ отправлен", SWConsoleStyle.TextDim);
            };
            SetVisibility();
        }

        private static BoxContainer Field(BoxContainer parent, string title, Control control)
        {
            var field = VerticalStack(3);
            field.AddChild(SWConsoleStyle.Text(title, SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            field.AddChild(control);
            parent.AddChild(field);
            return field;
        }

        public void SelectWorld(string id)
        {
            var index = Array.IndexOf(_worlds, id);
            if (index < 0) return;
            _worldId = id; _world.SelectId(index); _detailKey = string.Empty; RebuildDetails(); Invalidate();
        }

        public void SelectPending(SWEmpireIndustryProjectView order)
        {
            if (!_delivery || !_menu.CanManage || !order.AwaitingSource) return;
            _acceptOrder = order;
            _detailKey = string.Empty;
            RebuildDetails();
            Invalidate();
        }

        public void SelectDestination(string id)
        {
            var index = Array.IndexOf(_worlds, id);
            if (index < 0) return;
            _targetId = id; _target.SelectId(index); Invalidate();
        }

        public void SelectRoute(string world, string route)
        {
            if (_delivery) return;
            _action.SelectId((int) SWEmpireIndustryAction.ExpandRoute);
            SelectWorld(world);
            var index = Array.IndexOf(_routes, route);
            if (index >= 0) { _routeId = route; _route.SelectId(index); }
            Invalidate();
        }

        public void Update(SWEmpireCouncilConsoleState state)
        {
            if (_lastSphere != state.Sphere)
            {
                _lastSphere = state.Sphere;
                _acceptOrder = null;
                _worldId = _targetId = _goodId = _recipeId = _routeId = string.Empty;
                _catalogKey = _detailKey = string.Empty;
                _amount.SetText("1"); _deadline.SetText("4");
                _priority.SelectId(0);
                _action.SelectId((int) (_delivery ? SWEmpireIndustryAction.Ship : SWEmpireIndustryAction.UpgradeLine));
                Invalidate();
            }
            _state = state;
            if (_delivery)
            {
                _composerTitle.Text = _acceptOrder != null ? $"Заявка на {_menu.Good(_acceptOrder.Good)} для {_menu.World(_acceptOrder.Target)}"
                    : _menu.CanManage ? "Новая поставка" : "Материальная заявка";
                _action.Disabled = !_menu.CanManage;
                if (!_menu.CanManage && _action.SelectedId != (int) SWEmpireIndustryAction.Ship)
                {
                    _action.SelectId((int) SWEmpireIndustryAction.Ship);
                    _detailKey = string.Empty;
                    Invalidate();
                }
            }
            var worlds = state.Economy.Worlds.Select(w => w.World).Where(id =>
                _menu._proto.TryIndex<SWPlanetEconomyPrototype>(id, out var p) && p.ActiveEconomy
                && _menu._geography.Worlds.Any(w => w.Id == id && _menu._geography.GetWorldFaction(w) == "empire"))
                .OrderBy(_menu.World).ToArray();
            var key = string.Join("|", worlds);
            if (_catalogKey != key)
            {
                _catalogKey = key; _worlds = worlds;
                var firstTarget = string.IsNullOrEmpty(_targetId);
                FillWorld(_world, ref _worldId); FillWorld(_target, ref _targetId);
                if (firstTarget && _targetId == _worldId && _worlds.Length > 1)
                {
                    var index = Array.FindIndex(_worlds, id => id != _worldId);
                    _targetId = _worlds[index]; _target.SelectId(index);
                }
                _detailKey = string.Empty; RebuildDetails();
            }
            RebuildDetails();
            UpdateStock();
            _calculate.Disabled = !CanSubmit || _worlds.Length == 0;
            _calculate.ToolTip = CanSubmit ? "Рассчитать стоимость и сроки" : "Управление доступно сфере производства и логистики";
            var pendingId = _materialPending?.RequestId ?? _pending?.RequestId;
            if (pendingId == null || state.ResultRequestId != pendingId)
            {
                _confirm.Disabled = true;
                return;
            }
            if (_submitted)
            {
                SetQuote(state.Result, SWConsoleStyle.TextBright);
                if (_acceptOrder != null && state.Projects.Any(p => p.Id == _acceptOrder.Id && !p.AwaitingSource))
                { _acceptOrder = null; _detailKey = string.Empty; RebuildDetails(); }
                _pending = null; _materialPending = null; _submitted = false; _confirm.Disabled = true;
                return;
            }
            var q = state.Quote;
            var materials = q.Materials.Count == 0 ? "Материалы: не требуются" : "Материалы: " + string.Join(", ", q.Materials.Select(m => $"{_menu.Good(m.Good)} {Number(m.Quantity)}"));
            var estimate = $"Стоимость: {q.Cost:N0} кр\n{materials}\nРаботы  {Number(q.WorkHours)} ч\nЭнергия  {Number(q.Power)}\nТруд  {Number(q.Labor)}";
            if (q.AddedRate > 0) estimate += $"\nПрирост  {Number(q.AddedRate)} {(_pending?.Action == SWEmpireIndustryAction.ExpandRoute ? "мест/ч" : "циклов/ч")}";
            if (_delivery) estimate += $"\n{Eta(q.EtaHours)}";
            if (!string.IsNullOrWhiteSpace(q.Reason)) estimate += $"\n{q.Reason}";
            SetQuote(estimate, q.Allowed ? SWConsoleStyle.TextBright : SWConsoleStyle.Warn);
            var affordable = q.Cost <= state.Budgets.GetValueOrDefault(state.Sphere);
            if (q.Allowed && !affordable) SetQuote(estimate + "\nНедостаточно средств сферы", SWConsoleStyle.Warn);
            _confirm.Disabled = !q.Allowed || !CanSubmit || !affordable;
            _confirm.ToolTip = !q.Allowed ? q.Reason : !affordable ? "Недостаточно средств сферы" : "Подтвердить расход";
        }

        private void FillWorld(OptionButton picker, ref string selected)
        {
            picker.Clear();
            for (var i = 0; i < _worlds.Length; i++) picker.AddItem(_menu.World(_worlds[i]), i);
            var index = Array.IndexOf(_worlds, selected);
            if (index < 0) index = _worlds.Length > 0 ? 0 : -1;
            selected = index < 0 ? string.Empty : _worlds[index];
            if (index >= 0) picker.SelectId(index);
        }

        private void RebuildDetails()
        {
            if (_state == null) return;
            var active = _state.Economy.Worlds.FirstOrDefault(w => w.World == _worldId);
            var key = _worldId + ":" + _action.SelectedId + ":" + _menu.CanManage + ":" + _acceptOrder?.Id + ":" + string.Join("|", active?.Production.Select(p => p.Recipe) ?? Array.Empty<string>())
                + ":" + string.Join("|", active?.Goods.Select(g => g.Good) ?? Array.Empty<string>());
            if (_detailKey == key) return;
            _detailKey = key;
            _goods = (_delivery && !_menu.CanManage
                    ? _menu._proto.EnumeratePrototypes<SWCommodityPrototype>().Select(g => g.ID)
                    : active?.Goods.Select(g => g.Good) ?? Array.Empty<string>())
                .Distinct().OrderBy(_menu.Good).ToArray();
            Fill(_good, _goods, ref _goodId, _menu.Good);
            var action = (SWEmpireIndustryAction) _action.SelectedId;
            var current = active?.Production.Select(p => p.Recipe).ToHashSet() ?? new HashSet<string>();
            if (_menu._proto.TryIndex<SWPlanetEconomyPrototype>(_worldId, out var profile))
                _recipes = _menu._proto.EnumeratePrototypes<SWProductionRecipePrototype>()
                    .Where(r => profile.Facilities.Contains(r.Facility) && r.Requirements.All(profile.Conditions.Contains)
                        && r.ResourceCosts.Count == 0 && (action is SWEmpireIndustryAction.UpgradeLine or SWEmpireIndustryAction.Concentrate ? current.Contains(r.ID) : !current.Contains(r.ID)))
                    .OrderBy(EnterpriseName).Select(r => r.ID).ToArray();
            else _recipes = Array.Empty<string>();
            Fill(_enterprise, _recipes, ref _recipeId, id => EnterpriseName(_menu._proto.Index<SWProductionRecipePrototype>(id)));
            var permitted = _state.Economy.Worlds.Select(w => w.World).ToHashSet();
            _routes = _menu._proto.EnumeratePrototypes<SWTransitLinkPrototype>()
                .Where(l => l.Accessible && permitted.Contains(l.From) && permitted.Contains(l.To) && (l.From == _worldId || l.To == _worldId))
                .OrderBy(l => _menu.World(l.From) + _menu.World(l.To)).Select(l => l.ID).ToArray();
            Fill(_route, _routes, ref _routeId, id =>
            {
                var link = _menu._proto.Index<SWTransitLinkPrototype>(id);
                return $"{_menu.World(link.From)} — {_menu.World(link.To)}";
            });
            SetVisibility(); UpdateStock();
        }

        private string EnterpriseName(SWProductionRecipePrototype recipe)
        {
            var facility = _menu._proto.Index(recipe.Facility).Name;
            var products = string.Join(", ", recipe.Outputs.Keys.Select(id => _menu._proto.Index(id).Name));
            return $"{facility} — {products}";
        }

        private static void Fill(OptionButton picker, string[] entries, ref string selected, Func<string, string> label)
        {
            picker.Clear();
            for (var i = 0; i < entries.Length; i++) picker.AddItem(label(entries[i]), i);
            var index = Array.IndexOf(entries, selected);
            if (index < 0) index = entries.Length > 0 ? 0 : -1;
            selected = index < 0 ? string.Empty : entries[index];
            if (index >= 0) picker.SelectId(index);
        }

        private void SetVisibility()
        {
            var action = (SWEmpireIndustryAction) _action.SelectedId;
            var accepting = _acceptOrder != null;
            _composerTitle.Text = accepting ? $"Заявка на {_menu.Good(_acceptOrder!.Good)} для {_menu.World(_acceptOrder.Target)}"
                : _delivery ? (_menu.CanManage ? "Новая поставка" : "Материальная заявка") : "Материальный проект";
            _actionField.Visible = !accepting && !_delivery;
            _worldField.Visible = !_delivery || _menu.CanManage;
            _targetField.Visible = _delivery && !accepting; _goodField.Visible = _delivery && !accepting;
            _amountField.Visible = !accepting;
            _priorityField.Visible = _delivery && !accepting;
            _deadlineField.Visible = (_delivery || action == SWEmpireIndustryAction.Concentrate) && !accepting;
            _amountField.Children.OfType<Label>().First().Text = action == SWEmpireIndustryAction.Concentrate ? "Множитель мощности (1–1,5)"
                : _delivery ? "Количество" : action == SWEmpireIndustryAction.ExpandRoute ? "Прирост мест в час" : "Прирост мощности, циклов/ч";
            _deadlineField.Children.OfType<Label>().First().Text = action == SWEmpireIndustryAction.Concentrate ? "Срок, ч (1–4)" : "Срок, ч (1–42)";
            _cancelAcceptance.Visible = accepting;
            _enterpriseField.Visible = !_delivery && action != SWEmpireIndustryAction.ExpandRoute;
            _routeField.Visible = !_delivery && action == SWEmpireIndustryAction.ExpandRoute;
        }
        private void UpdateStock()
        {
            var requestedGood = _acceptOrder?.Good ?? _goodId;
            var stock = _state?.Economy.Worlds.FirstOrDefault(w => w.World == _worldId)?.Goods.FirstOrDefault(g => g.Good == requestedGood);
            _stock.Visible = _delivery && _menu.CanManage;
            _stock.Text = stock == null ? "Остаток недоступен"
                : $"Доступно для проекта  {Number(stock.AvailableForProjects)}\nНа складе источника  {Number(stock.Stock)}";
        }
        private void Invalidate()
        {
            _pending = null; _materialPending = null; _submitted = false; _confirm.Disabled = true;
            SetQuote(_delivery ? string.Empty : "Ожидает расчёта", SWConsoleStyle.TextDim);
        }
        private void SetQuote(string text, Color color)
        {
            var message = new Robust.Shared.Utility.FormattedMessage();
            message.PushColor(color); message.AddText(text); message.Pop(); _quote.SetMessage(message);
        }
        private void Calculate()
        {
            if (_acceptOrder != null)
            {
                if (!CanSubmit || string.IsNullOrEmpty(_worldId)) { SetQuote("Выберите источник", SWConsoleStyle.Warn); return; }
                _pending = new SWEmpireIndustryRequest { RequestId = Guid.NewGuid().ToString("N"),
                    Action = SWEmpireIndustryAction.AcceptMaterial, World = _acceptOrder.Id, Source = _worldId };
                _materialPending = null; _submitted = false; _confirm.Disabled = true;
                SetQuote("Расчёт…", SWConsoleStyle.TextDim); _menu._owner.Quote(_pending); return;
            }
            if (!CanSubmit || !double.TryParse(_amount.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var amount)
                || !double.IsFinite(amount) || amount <= 0
                || !double.TryParse(_deadline.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var deadline) || deadline < 0 || !double.IsFinite(deadline))
            {
                SetQuote("Укажите положительное количество и допустимый срок", SWConsoleStyle.Warn); return;
            }
            var action = (SWEmpireIndustryAction) _action.SelectedId;
            if ((!_delivery || _menu.CanManage) && string.IsNullOrEmpty(_worldId)
                || _delivery && (string.IsNullOrEmpty(_goodId) || string.IsNullOrEmpty(_targetId) || _menu.CanManage && _targetId == _worldId)
                || !_delivery && action == SWEmpireIndustryAction.ExpandRoute && string.IsNullOrEmpty(_routeId)
                || !_delivery && action != SWEmpireIndustryAction.ExpandRoute && string.IsNullOrEmpty(_recipeId))
            {
                SetQuote("Выберите доступный мир, предприятие или маршрут", SWConsoleStyle.Warn); return;
            }
            if (_delivery && !_menu.CanManage)
            {
                _materialPending = new SWEmpireMaterialRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"), World = _targetId, Good = _goodId,
                    Amount = amount, DeadlineHours = deadline, Urgent = _priority.SelectedId == 1,
                };
                _pending = null; _submitted = false; _confirm.Disabled = true;
                SetQuote("Расчёт…", SWConsoleStyle.TextDim);
                _menu._owner.QuoteMaterial(_materialPending);
                return;
            }
            _materialPending = null;
            _pending = new SWEmpireIndustryRequest
            {
                RequestId = Guid.NewGuid().ToString("N"), Action = action,
                World = _worldId, Source = _worldId, Target = _targetId, Good = _goodId,
                Recipe = _recipeId, Link = _routeId, Amount = amount, DeadlineHours = deadline,
                Urgent = _priority.SelectedId == 1,
            };
            _submitted = false; _confirm.Disabled = true;
            SetQuote("Расчёт…", SWConsoleStyle.TextDim);
            _menu._owner.Quote(_pending);
        }
    }
}
