using System;
using System.Linq;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared._SW._Senate.Economy;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWMinistryWorkConsoleMenu
{
    private SWRepublicMapBrowser? _economyMap;
    private readonly Button[] _economyModes = new Button[3];
    private int _candidatePage;
    private string? _candidateWorld;
    private bool _showIndustrialOrders;
    private bool _showIndustrialEvents;
    private int _catalogPage;
    private int _routePage;
    private string? _catalogGood;
    private OptionButton? _economyGoodPicker;
    private Button? _availableProjectsButton;
    private Button? _projectOrdersButton;
    private Button? _industrialEventsButton;
    private Label? _industrialHeading;
    private string? _industrialGoodsSignature;

    private void BuildEconomyMap(SWMinistryWorkConsoleState state)
    {
        if (!state.IsEconomy) return;
        if (_economyMap == null)
        {
            var modes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            var names = new[] { "Дефицит", "Производство", "Перевозки" };
            for (var i = 0; i < names.Length; i++)
            {
                var mode = i;
                var button = SWConsoleStyle.TabButton(names[i]);
                button.Name = $"EconomyMapMode{mode}";
                button.OnPressed += _ => SetEconomyMode(mode);
                _economyModes[i] = button;
                modes.AddChild(button);
            }
            ProjectsContent.AddChild(modes);
            _economyMap = new SWRepublicMapBrowser(compactEconomy: true) { EconomyDetailsOverride = BuildEconomySelection };
            _economyMap.CommodityChanged += good =>
            {
                if (good != null && good != _state?.EconSelectedGood)
                {
                    _candidatePage = 0;
                    _owner.SelectEconomyGood(good);
                }
            };
            ProjectsContent.AddChild(_economyMap);
            SetEconomyMode(0);
        }
        if (state.EconGoods.Count > 0) _economyMap.SetIndustrialGoods(state.EconGoods.Select(g => g.Id));
        _economyMap.SetCrises(state.EconMapCrises);
        if (!string.IsNullOrEmpty(state.EconSelectedGood) && _economyMap.CommodityFilter != state.EconSelectedGood)
            _economyMap.SelectCommodity(state.EconSelectedGood);
        _economyMap.RefreshEconomySelection();
    }

    private void SetEconomyMode(int mode)
    {
        for (var i = 0; i < _economyModes.Length; i++) SWConsoleStyle.StyleTabButton(_economyModes[i], i == mode);
        _economyMap?.SetEconomyMapMode(mode);
    }

    private void BuildEconomySelection(BoxContainer heading, BoxContainer detail, string? worldId, SWGalacticMapRoute? route)
    {
        var state = _state;
        if (state == null || _economyMap == null) return;
        var selectedEvent = state.EconEvents.FirstOrDefault(e => _economyMap.SelectedCrisisIds.Contains(e.CrisisId));
        if (selectedEvent != null)
        {
            heading.AddChild(SWConsoleStyle.Section("Экономическое потрясение"));
            AddEconomyEventCard(detail, selectedEvent, state, false);
            return;
        }
        if (route != null)
        {
            var id = SWTransitLinkPrototype.LinkId(route.From, route.To);
            var edge = state.EconRoutes.FirstOrDefault(r => r.Id == id);
            heading.AddChild(SWConsoleStyle.Wrap(edge == null ? "Маршрут" : $"{edge.AName} / {edge.BName}", SWConsoleStyle.Brass));
            if (edge == null) { detail.AddChild(SWConsoleStyle.Wrap("Нет данных о модернизации маршрута", SWConsoleStyle.TextDim)); return; }
            detail.AddChild(SWConsoleStyle.Wrap(edge.Detail, SWConsoleStyle.TextBright));
            detail.AddChild(SWConsoleStyle.Wrap($"Стоимость: {edge.Cost:N0} кр\n{edge.Status}", SWConsoleStyle.TextDim));
            var fund = SWConsoleStyle.Action("Финансировать модернизацию", SWConsoleStyle.Accent);
            fund.Name = "EconomyFundRoute";
            fund.Disabled = !edge.Available || !state.HasCard || !state.IsMinisterCard;
            fund.ToolTip = !state.HasCard || !state.IsMinisterCard ? "Требуется карта министра" : edge.Status;
            fund.OnPressed += _ => _owner.FundEconomyRoute(edge.Id);
            detail.AddChild(fund);
            return;
        }
        if (worldId == null) { heading.AddChild(SWConsoleStyle.Wrap("Выберите мир или маршрут", SWConsoleStyle.TextDim)); return; }
        if (_candidateWorld != worldId) { _candidateWorld = worldId; _candidatePage = 0; }
        var problem = state.EconProblems.FirstOrDefault(p => p.World == worldId && p.Good == state.EconSelectedGood);
        var worldName = _economyMap.GetWorldName(worldId);
        heading.AddChild(SWConsoleStyle.Wrap(_economyMap.CurrentSectorId == null && _economyMap.SelectedSectorName is { } sectorName ? $"Сектор: {sectorName}" : worldName, SWConsoleStyle.Brass));
        var goodName = state.EconGoods.FirstOrDefault(g => g.Id == state.EconSelectedGood)?.Name ?? "Выберите товар";
        heading.AddChild(SWConsoleStyle.Wrap(goodName, SWConsoleStyle.Accent));
        if (_economyMap.CurrentSectorId == null && _economyMap.Map.EconomySectorSummary(worldId) is { } sectorSummary)
        {
            detail.AddChild(SWConsoleStyle.Wrap(sectorSummary, SWConsoleStyle.TextBright));
            detail.AddChild(SWConsoleStyle.Wrap("Двойной щелчок: открыть сектор", SWConsoleStyle.TextDim));
            return;
        }
        var world = _economyMap.EconomyView?.Worlds.FirstOrDefault(w => w.World == worldId);
        var live = world?.Goods.FirstOrDefault(g => g.Good == state.EconSelectedGood);
        if (_economyMap.EconomyView is not { Ready: true }) detail.AddChild(SWConsoleStyle.Wrap("Ожидается публикация экономики", SWConsoleStyle.TextDim));
        else if (world == null) detail.AddChild(SWConsoleStyle.Wrap("Нет данных экономики для выбранного мира", SWConsoleStyle.TextDim));
        else if (live == null) detail.AddChild(SWConsoleStyle.Wrap("Для этого товара нет данных на выбранном мире", SWConsoleStyle.TextDim));
        else
        {
            var coverage = live.RequiredPerHour > 0 ? $"{live.Stock / live.RequiredPerHour:0.0} ч" : "нет спроса";
            detail.AddChild(SWConsoleStyle.Wrap($"Запас {live.Stock:0.##} / {live.Capacity:0.##}   Покрытие: {coverage}\nВыпуск {live.ProducedPerHour:0.##} ед/ч   Потребление {live.ConsumedPerHour:0.##} ед/ч\nВвоз {live.ImportedPerHour:0.##} ед/ч   В пути {live.Incoming:0.##}", SWConsoleStyle.TextBright));
        }
        if (problem != null)
        {
            detail.AddChild(SWConsoleStyle.Section(problem.Kind));
            detail.AddChild(SWConsoleStyle.Wrap(problem.Detail, SWConsoleStyle.TextBright));
            if (!string.IsNullOrWhiteSpace(problem.Sources)) detail.AddChild(SWConsoleStyle.Wrap($"Источники: {problem.Sources}", SWConsoleStyle.TextDim));
            if (!string.IsNullOrWhiteSpace(problem.Governor)) detail.AddChild(SWConsoleStyle.Wrap($"Губернатор: {problem.Governor}", SWConsoleStyle.TextDim));
        }
        else if (live != null) detail.AddChild(SWConsoleStyle.Wrap("Проблема по выбранному товару не выявлена", SWConsoleStyle.TextDim));
        detail.AddChild(SWConsoleStyle.Section("Промышленные расширения"));
        var candidates = state.EconCandidates.Where(c => c.Good == state.EconSelectedGood && c.World == worldId).ToArray();
        if (candidates.Length == 0) detail.AddChild(SWConsoleStyle.Wrap("В этом мире нет расширений для выбранного товара", SWConsoleStyle.TextDim));
        const int pageSize = 3;
        var pages = Math.Max(1, (candidates.Length + pageSize - 1) / pageSize);
        _candidatePage = Math.Clamp(_candidatePage, 0, pages - 1);
        if (pages > 1)
        {
            var pagination = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            var previous = SWConsoleStyle.TabButton("Назад");
            previous.Disabled = _candidatePage == 0;
            previous.OnPressed += _ => { _candidatePage--; _economyMap.RefreshEconomySelection(); };
            pagination.AddChild(previous);
            pagination.AddChild(SWConsoleStyle.Text($"{_candidatePage + 1} / {pages}", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            var next = SWConsoleStyle.TabButton("Далее");
            next.Disabled = _candidatePage >= pages - 1;
            next.OnPressed += _ => { _candidatePage++; _economyMap.RefreshEconomySelection(); };
            pagination.AddChild(next);
            detail.AddChild(pagination);
        }
        foreach (var candidate in candidates.Skip(_candidatePage * pageSize).Take(pageSize))
        {
            var rate = candidate.AddedRate > 0 ? $"+{candidate.AddedRate:0.##} ед/ч" : "нет данных";
            detail.AddChild(SWConsoleStyle.Wrap($"{candidate.RecipeName}\n{candidate.Cost:N0} кр   Выпуск: {rate}\nРаботы: {candidate.WorkHours:0.#} ч после доставки\nМатериалы: {candidate.Materials}\nСнабжение линии сырьём прошло текущую оценку\n{candidate.Reason}\n{candidate.Status}", SWConsoleStyle.TextBright));
            var fund = SWConsoleStyle.Action("Финансировать", SWConsoleStyle.Accent);
            fund.Name = $"EconomyFund_{candidate.World}_{candidate.Recipe}";
            fund.Disabled = !candidate.Available || !state.HasCard || !state.IsMinisterCard;
            fund.ToolTip = !state.HasCard || !state.IsMinisterCard ? "Требуется карта министра" : candidate.Status;
            fund.OnPressed += _ => _owner.FundExpansion(candidate.World, candidate.Recipe);
            detail.AddChild(fund);
        }
    }

    private void BuildIndustrialPrograms(SWMinistryWorkConsoleState state)
    {
        if (_catalogGood != state.EconSelectedGood) { _catalogGood = state.EconSelectedGood; _catalogPage = 0; _candidatePage = 0; }
        var scroll = RoutesScroll.VScroll;
        RoutesContent.RemoveAllChildren();
        if (!state.IsEconomy) return;
        UpdateIndustrialHeader(state);
        if (_showIndustrialEvents)
        {
            BuildIndustrialEvents(state);
            RoutesScroll.VScroll = scroll;
            return;
        }
        if (_showIndustrialOrders)
        {
            if (state.EconIndustrialProjects.Count == 0)
                RoutesContent.AddChild(SWConsoleStyle.Wrap(state.EconGoods.Count == 0 && state.EconRoutes.Count == 0
                    ? "Ожидается публикация экономики" : "Нет запущенных проектов. Откройте доступные проекты, чтобы выбрать вариант.", SWConsoleStyle.TextDim));
            foreach (var project in state.EconIndustrialProjects)
            {
                var row = SWConsoleStyle.Row(SWConsoleStyle.BgPanel, project.Completed ? SWConsoleStyle.Good : SWConsoleStyle.Accent);
                var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(6) };
                box.AddChild(SWConsoleStyle.Wrap($"{project.Name} / {project.WorldName}   {project.Progress * 100:0}%", SWConsoleStyle.TextBright));
                box.AddChild(SWConsoleStyle.Wrap($"{project.Status}\n{project.Materials}\n{project.Work}", SWConsoleStyle.TextDim));
                row.AddChild(box);
                RoutesContent.AddChild(row);
            }
            RoutesScroll.VScroll = scroll;
            return;
        }
        if (!state.HasCard || !state.IsMinisterCard)
            RoutesContent.AddChild(SWConsoleStyle.Wrap("Для финансирования требуется карта министра", SWConsoleStyle.TextDim));

        var good = state.EconGoods.FirstOrDefault(g => g.Id == state.EconSelectedGood);
        if (good == null)
            RoutesContent.AddChild(SWConsoleStyle.Wrap(state.EconGoods.Count == 0
                ? "Нет данных о производимых товарах: ожидается публикация экономики"
                : "Выберите товар, чтобы увидеть варианты расширения", SWConsoleStyle.TextDim));
        else if (state.EconCandidates.Count == 0)
            RoutesContent.AddChild(SWConsoleStyle.Wrap($"Для товара «{good.Name}» сейчас нет кандидатов на расширение.", SWConsoleStyle.TextDim));
        else
        {
            RoutesContent.AddChild(SWConsoleStyle.Wrap($"{good.Name}: доступно {state.EconCandidates.Count(c => c.Available)} из {state.EconCandidates.Count}", SWConsoleStyle.TextBright));
            AddIndustrialPagination(state.EconCandidates.Count, ref _catalogPage, false);
            foreach (var candidate in state.EconCandidates.Skip(_catalogPage * 2).Take(2))
            {
                var rate = candidate.AddedRate > 0 ? $"+{candidate.AddedRate:0.##} ед/ч" : "нет данных";
                var location = candidate.WorldName == candidate.SectorName ? candidate.WorldName : $"{candidate.WorldName}, {candidate.SectorName}";
                RoutesContent.AddChild(SWConsoleStyle.Wrap($"{candidate.RecipeName} — {location}\nСтоимость: {candidate.Cost:N0} кр   Выпуск: {rate}\nМатериалы: {candidate.Materials}\nРаботы: {candidate.WorkHours:0.#} ч после доставки\nСнабжение линии сырьём прошло текущую оценку\n{candidate.Reason}\n{candidate.Status}", SWConsoleStyle.TextBright));
                var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
                var fund = SWConsoleStyle.Action("Финансировать", SWConsoleStyle.Accent);
                fund.Name = $"EconomyCatalogFund_{candidate.World}_{candidate.Recipe}";
                fund.Disabled = !candidate.Available || !state.HasCard || !state.IsMinisterCard;
                fund.ToolTip = !state.HasCard || !state.IsMinisterCard ? "Требуется карта министра" : candidate.Status;
                fund.OnPressed += _ => _owner.FundExpansion(candidate.World, candidate.Recipe);
                var map = SWConsoleStyle.Action("На карте", SWConsoleStyle.Brass);
                map.Name = $"EconomyCatalogMap_{candidate.World}_{candidate.Recipe}";
                map.OnPressed += _ => { _economyMap?.OpenSector(candidate.Sector); _economyMap?.SelectWorld(candidate.World, true); ShowTab(ProjectsTab); };
                actions.AddChild(fund); actions.AddChild(map); RoutesContent.AddChild(actions);
            }
        }

        RoutesContent.AddChild(SWConsoleStyle.Section("Транспортные модернизации"));
        if (state.EconRoutes.Count == 0)
            RoutesContent.AddChild(SWConsoleStyle.Wrap("Нет данных о маршрутах: ожидается публикация экономики", SWConsoleStyle.TextDim));
        else
        {
            RoutesContent.AddChild(SWConsoleStyle.Wrap($"Доступно {state.EconRoutes.Count(r => r.Available)} из {state.EconRoutes.Count} маршрутов", SWConsoleStyle.TextBright));
            AddIndustrialPagination(state.EconRoutes.Count, ref _routePage, true);
            foreach (var route in state.EconRoutes.Skip(_routePage * 2).Take(2))
            {
                RoutesContent.AddChild(SWConsoleStyle.Wrap($"{route.AName} — {route.BName}\nСтройплощадка: {route.ConstructionWorldName}\nБазовая ёмкость: {route.BaseCapacity:0.##} мест/ч\n{route.Detail}\nСтоимость: {route.Cost:N0} кр\n{route.Status}", SWConsoleStyle.TextBright));
                var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
                var fund = SWConsoleStyle.Action("Финансировать", SWConsoleStyle.Accent);
                fund.Name = $"EconomyCatalogRouteFund_{route.Id}";
                fund.Disabled = !route.Available || !state.HasCard || !state.IsMinisterCard;
                fund.ToolTip = !state.HasCard || !state.IsMinisterCard ? "Требуется карта министра" : route.Status;
                fund.OnPressed += _ => _owner.FundEconomyRoute(route.Id);
                var map = SWConsoleStyle.Action("На карте", SWConsoleStyle.Brass);
                map.Name = $"EconomyCatalogRouteMap_{route.Id}";
                map.OnPressed += _ => { if (_economyMap?.SelectEconomyRoute(route.Id) == true) ShowTab(ProjectsTab); };
                actions.AddChild(fund); actions.AddChild(map); RoutesContent.AddChild(actions);
            }
        }
        RoutesScroll.VScroll = scroll;
    }

    private void UpdateIndustrialHeader(SWMinistryWorkConsoleState state)
    {
        if (_economyGoodPicker == null)
        {
            RoutesHeader.AddChild(SWConsoleStyle.Section("Программы и поставки"));
            var tabs = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            _availableProjectsButton = SWConsoleStyle.TabButton("Доступные проекты");
            _projectOrdersButton = SWConsoleStyle.TabButton("Выполнение и поставки");
            _industrialEventsButton = SWConsoleStyle.TabButton("Потрясения");
            _availableProjectsButton.Name = "EconomyAvailableProjects";
            _projectOrdersButton.Name = "EconomyProjectOrders";
            _industrialEventsButton.Name = "EconomyEvents";
            _availableProjectsButton.OnPressed += _ => { _showIndustrialOrders = false; _showIndustrialEvents = false; RoutesScroll.VScroll = 0; BuildIndustrialPrograms(_state!); };
            _projectOrdersButton.OnPressed += _ => { _showIndustrialOrders = true; _showIndustrialEvents = false; RoutesScroll.VScroll = 0; BuildIndustrialPrograms(_state!); };
            _industrialEventsButton.OnPressed += _ => { _showIndustrialOrders = false; _showIndustrialEvents = true; RoutesScroll.VScroll = 0; BuildIndustrialPrograms(_state!); };
            tabs.AddChild(_availableProjectsButton);
            tabs.AddChild(_projectOrdersButton);
            tabs.AddChild(_industrialEventsButton);
            RoutesHeader.AddChild(tabs);
            _industrialHeading = SWConsoleStyle.Section("Промышленные расширения");
            RoutesHeader.AddChild(_industrialHeading);
            _economyGoodPicker = new OptionButton { Name = "EconomyProgramGood", Filterable = true, HorizontalExpand = true };
            _economyGoodPicker.OnItemSelected += args =>
            {
                var current = _state;
                if (current == null || args.Id <= 0 || args.Id > current.EconGoods.Count) return;
                _catalogPage = 0;
                _candidatePage = 0;
                _owner.SelectEconomyGood(current.EconGoods[args.Id - 1].Id);
            };
            RoutesHeader.AddChild(_economyGoodPicker);
        }
        SWConsoleStyle.StyleTabButton(_availableProjectsButton!, !_showIndustrialOrders && !_showIndustrialEvents);
        SWConsoleStyle.StyleTabButton(_projectOrdersButton!, _showIndustrialOrders);
        SWConsoleStyle.StyleTabButton(_industrialEventsButton!, _showIndustrialEvents);
        _industrialHeading!.Visible = !_showIndustrialOrders && !_showIndustrialEvents;
        _economyGoodPicker.Visible = !_showIndustrialOrders && !_showIndustrialEvents;
        var signature = string.Join("|", state.EconGoods.Select(g => $"{g.Id.Length}:{g.Id}{g.Name.Length}:{g.Name}"));
        if (_industrialGoodsSignature != signature)
        {
            _industrialGoodsSignature = signature;
            _economyGoodPicker.Clear();
            _economyGoodPicker.AddItem("Выберите товар", 0);
            _economyGoodPicker.SetItemDisabled(0, true);
            for (var i = 0; i < state.EconGoods.Count; i++) _economyGoodPicker.AddItem(state.EconGoods[i].Name, i + 1);
        }
        var selected = state.EconGoods.FindIndex(g => g.Id == state.EconSelectedGood) + 1;
        if (_economyGoodPicker.SelectedId != selected) _economyGoodPicker.SelectId(selected);
        _economyGoodPicker.Disabled = state.EconGoods.Count == 0;
    }

    private void AddIndustrialPagination(int count, ref int page, bool routes)
    {
        var pages = Math.Max(1, (count + 1) / 2);
        page = Math.Clamp(page, 0, pages - 1);
        if (pages == 1) return;
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var previous = SWConsoleStyle.TabButton("Назад");
        previous.Name = routes ? "EconomyRoutePagePrevious" : "EconomyCandidatePagePrevious";
        previous.Disabled = page == 0;
        previous.OnPressed += _ => { if (routes) _routePage--; else _catalogPage--; BuildIndustrialPrograms(_state!); };
        var next = SWConsoleStyle.TabButton("Далее");
        next.Name = routes ? "EconomyRoutePageNext" : "EconomyCandidatePageNext";
        next.Disabled = page == pages - 1;
        next.OnPressed += _ => { if (routes) _routePage++; else _catalogPage++; BuildIndustrialPrograms(_state!); };
        buttons.AddChild(previous);
        buttons.AddChild(SWConsoleStyle.Text($"{page + 1} / {pages}", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        buttons.AddChild(next);
        RoutesContent.AddChild(buttons);
    }
}
