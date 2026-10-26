using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._SW._Senate.Consoles.Economy;
using Content.Shared._SW._Senate.Economy;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using static Content.Client._SW._Senate.Consoles.Statistics.SWStatisticsWidgets;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWRepublicMapBrowser
{
    public SWPlanetSurfaceControl? Surface { get; private set; }

    public bool CanOpenSurface(string id)
    {
        var proto = IoCManager.Resolve<IPrototypeManager>();
        return _worlds.TryGetValue(id, out var world) && world.Sector != null
            && (_lockedSector == null || world.Sector.Value.Id == _lockedSector)
            && proto.TryIndex<SWPlanetEconomyPrototype>(id, out var economy) && economy.ActiveEconomy
            && proto.TryIndex<SWPlanetSurfacePrototype>(id, out _);
    }

    public bool OpenSurface(string id)
    {
        if (!CanOpenSurface(id)) return false;
        if (Surface != null && SelectedWorldId == id) return true;
        var world = _worlds[id];
        OpenSector(world.Sector!.Value.Id);
        SelectWorld(id);
        var surface = new SWPlanetSurfaceControl(IoCManager.Resolve<IPrototypeManager>().Index<SWPlanetSurfacePrototype>(id));
        Surface = surface;
        surface.VerticalExpand = true;
        surface.HorizontalExpand = true;
        surface.SiteSelected += _ => RefreshSurfaceDetails();
        surface.ViewChanged += () => _zoom.Text = $"{surface.Zoom * 100:0}%";
        surface.SetEconomy(_economyView?.Worlds.FirstOrDefault(w => w.World == id), _economyView?.Paused ?? false, _economyView?.Ready ?? false);
        surface.SetTrafficFilter(_commodityFilter, _showTraffic);
        _body.RemoveAllChildren();
        _detail = VerticalStack(8);
        _detailHeading = VerticalStack(6);
        var sidebar = VerticalStack(8);
        sidebar.Name = "MapDetails";
        sidebar.AddChild(_detailHeading);
        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(_detail);
        sidebar.AddChild(scroll);
        _columns = new SWAtlasLayout(surface, sidebar) { VerticalExpand = true };
        _body.AddChild(_columns);
        _trafficToggle.Visible = true;
        RefreshEconomyStatus();
        _zoom.Text = $"{surface.Zoom * 100:0}%";
        _title.Text = $"Территория: {world.Name}";
        RefreshSurfaceDetails();
        return true;
    }

    private void ChangeAtlasZoom(float factor)
    {
        if (Surface != null) Surface.ChangeZoom(factor);
        else Map.ChangeZoom(factor);
    }

    private void RefreshSurfaceDetails()
    {
        if (Surface == null || SelectedWorldId is not { } id) return;
        _detailHeading.RemoveAllChildren();
        _detail.RemoveAllChildren();
        var back = SWConsoleStyle.TabButton("К сектору");
        back.Name = "BackToSector";
        back.OnPressed += _ => { OpenSector(CurrentSectorId!); SelectWorld(id); };
        _detailHeading.AddChild(back);
        _detailHeading.AddChild(SWConsoleStyle.Wrap(WorldName(id), SWConsoleStyle.Brass));
        _detailHeading.AddChild(SWConsoleStyle.Wrap(Surface.Surface.Biome, SWConsoleStyle.TextDim));
        var site = Surface.Surface.Sites.FirstOrDefault(s => s.Id == Surface.SelectedSiteId);
        if (site == null)
        {
            AddGovernorCard(_worlds[id].Sector?.Id);
            Empty("Выберите объект территории");
            if (Surface.SelectedCell is { } cell)
            {
                Empty($"Клетка {(char) ('A' + cell.X)}{cell.Y + 1}: {SWPlanetSurfaceControl.TerrainName(Surface.Surface.Terrain[cell.Y][cell.X])}");
                if (Surface.RoadAt(cell) is { } road)
                {
                    _detail.AddChild(SWConsoleStyle.Wrap(SWPlanetSurfaceControl.RoadName(road.Kind), SWConsoleStyle.Accent));
                    SurfaceFlowDetails(Surface.FlowsAt(cell));
                }
            }
            foreach (var entry in Surface.Surface.Sites.Where(s => s.Kind != "resource"))
            {
                var button = SWConsoleStyle.TabButton($"{(char) ('A' + entry.Cell.X)}{entry.Cell.Y + 1}  {entry.Name}");
                button.ToolTip = entry.Name;
                button.TextAlign = Label.AlignMode.Left;
                button.Label.ClipText = true;
                button.OnPressed += _ => Surface?.SelectSite(entry.Id);
                _detail.AddChild(button);
            }
            return;
        }
        _detail.AddChild(SWConsoleStyle.Wrap(site.Name, SWConsoleStyle.Brass));
        var terrain = Surface.Surface.Terrain[site.Cell.Y][site.Cell.X];
        Empty($"Местность: {SWPlanetSurfaceControl.TerrainName(terrain)}");
        var proto = IoCManager.Resolve<IPrototypeManager>();
        if (site.Resource is { } resource)
            _detail.AddChild(SWConsoleStyle.Wrap($"Ресурс: {proto.Index(resource).Name}", SWConsoleStyle.Accent));
        SurfaceFlowDetails(Surface.TrafficFlows.Where(f => f.From == site.Id || f.To == site.Id));
        var live = _economyView?.Worlds.FirstOrDefault(w => w.World == id);
        foreach (var recipeId in site.Recipes)
        {
            var recipe = proto.Index(recipeId);
            var block = VerticalStack(4);
            block.Name = $"SurfaceRecipe_{recipe.ID}";
            block.AddChild(SWConsoleStyle.Wrap(recipe.Name, SWConsoleStyle.TextBright));
            if (_showProductionRecipes)
            {
                block.AddChild(SWConsoleStyle.Wrap("Вход за цикл: " + (recipe.Inputs.Count == 0 ? "без товарных затрат" : string.Join(", ", recipe.Inputs.Select(g => $"{GoodName(g.Key.Id)} {Number(g.Value)}"))), SWConsoleStyle.TextDim));
                block.AddChild(SWConsoleStyle.Wrap("Выход за цикл: " + string.Join(", ", recipe.Outputs.Select(g => $"{GoodName(g.Key.Id)} {Number(g.Value)}")), SWConsoleStyle.TextBright));
            }
            var activity = live?.Production.FirstOrDefault(p => p.Recipe == recipe.ID);
            if (_economyView is not { Ready: true } || activity == null)
                block.AddChild(SWConsoleStyle.Wrap("Ожидание данных производства", SWConsoleStyle.TextDim));
            else
            {
                block.AddChild(SWConsoleStyle.Wrap($"Мощность: {Number(activity.EffectiveCyclesPerHour * (live?.Capacity?.ProductionMultiplier ?? 1))} циклов/ч", SWConsoleStyle.TextDim));
                block.AddChild(SWConsoleStyle.Wrap($"Фактически: {Number(activity.CompletedCyclesPerHour)} циклов/ч", SWConsoleStyle.Accent));
                block.AddChild(SWConsoleStyle.Wrap($"Начато: {Number(activity.StartedCyclesPerHour)} циклов/ч; в работе: {Number(activity.WorkInProgress)}", SWConsoleStyle.TextDim));
                foreach (var output in recipe.Outputs)
                    block.AddChild(Pair(GoodName(output.Key.Id), $"{Number(output.Value * activity.CompletedCyclesPerHour)} ед./ч", SWConsoleStyle.Accent));
                if (_economyView.Paused) block.AddChild(SWConsoleStyle.Wrap("Пауза: значения последнего расчёта", SWConsoleStyle.TextDim));
            }
            _detail.AddChild(block);
        }
        if (live == null || _economyView is not { Ready: true }) return;
        if (site.Kind == "port") FreightDetails(id);
        if (site.Kind == "settlement")
        {
            _detail.AddChild(SWConsoleStyle.Section("Снабжение планеты"));
            SupplyDetails(live);
        }
        if (site.Kind == "power" && live.Capacity is { } power)
        {
            _detail.AddChild(SWConsoleStyle.Section("Энергетика мира"));
            _detail.AddChild(Pair("Реакторная мощность", Number(power.ReactorCapacityPower), SWConsoleStyle.Accent));
            _detail.AddChild(Pair("Базовое потребление", Number(power.DomesticPower), SWConsoleStyle.TextBright));
        }
        if (site.Kind == "storage")
        {
            _detail.AddChild(SWConsoleStyle.Section("Складские ёмкости мира"));
            foreach (var good in live.Goods.Where(g => _commodityFilter == null || g.Good == _commodityFilter))
                _detail.AddChild(Pair(GoodName(good.Good), $"{Number(good.Stock)} / {Number(good.Capacity)}", SWConsoleStyle.Accent));
        }
    }

    private void SurfaceFlowDetails(IEnumerable<SWPlanetSurfaceFlow> flows)
    {
        if (Surface == null || _economyView is not { Ready: true }) return;
        var rows = flows.Where(f => f.UnitsPerHour > 0 && (_commodityFilter == null || f.Good == _commodityFilter)).ToArray();
        var heading = SWConsoleStyle.Section("Движение грузов");
        heading.ToolTip = "По данным последнего расчёта. Снабжение жилых районов распределено поровну.";
        _detail.AddChild(heading);
        if (rows.Length == 0) { Empty("Нет потоков выбранных товаров"); return; }
        foreach (var flow in rows.OrderBy(f => f.Good).ThenBy(f => f.From).ThenBy(f => f.To))
        {
            var block = VerticalStack(3);
            block.Name = $"SurfaceFlow_{flow.From}_{flow.To}_{flow.Good}";
            block.AddChild(Pair(GoodName(flow.Good), $"{Number(flow.UnitsPerHour)} ед./ч", SWConsoleStyle.Accent));
            block.AddChild(SWConsoleStyle.Wrap($"Откуда: {SurfaceSiteName(flow.From)}\nКуда: {SurfaceSiteName(flow.To)}", SWConsoleStyle.TextDim));
            _detail.AddChild(block);
        }
    }

    private string SurfaceSiteName(string id) => Surface?.Surface.Sites.FirstOrDefault(s => s.Id == id)?.Name ?? id;

}
