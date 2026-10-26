using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Economy;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._SW._Senate.Consoles.Economy;

/// <summary>Client-only territory atlas. Sites refer to existing world/recipe telemetry.</summary>
public sealed partial class SWPlanetSurfaceControl : Control
{
    private readonly Dictionary<Vector2i, SWPlanetSurfaceSite> _sites;
    private readonly SWPlanetSurfaceNetwork _network;
    private readonly Dictionary<string, SWProductionRecipePrototype> _recipes;
    private readonly Dictionary<string, SWEconomyProductionView> _production = new();
    private Vector2 _pan;
    private Vector2? _press;
    private Vector2 _panAtPress;
    private Vector2i? _hover;
    private bool _dragged;
    private bool _ready;
    private bool _paused;
    private string? _commodity;
    public SWPlanetSurfacePrototype Surface { get; }
    public string? SelectedSiteId { get; private set; }
    public Vector2i? SelectedCell { get; private set; }
    public float Zoom { get; private set; } = 1;
    public event Action<string?>? SiteSelected;
    public event Action? ViewChanged;

    public SWPlanetSurfaceControl(SWPlanetSurfacePrototype surface)
    {
        Surface = surface;
        _network = new SWPlanetSurfaceNetwork(surface);
        _sites = surface.Sites.ToDictionary(s => s.Cell);
        var proto = IoCManager.Resolve<IPrototypeManager>();
        _recipes = proto.EnumeratePrototypes<SWProductionRecipePrototype>().ToDictionary(r => r.ID);
        InitializeTraffic(proto);
        HorizontalExpand = VerticalExpand = true;
        MinHeight = 220;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void SetEconomy(SWEconomyWorldView? world, bool paused = false, bool ready = false)
    {
        _ready = ready;
        _paused = paused;
        _production.Clear();
        _trafficWorld = world?.World == Surface.ID ? world : null;
        RebuildTraffic();
        if (_trafficWorld == null) return;
        foreach (var line in _trafficWorld.Production) _production[line.Recipe] = line;
    }

    public void SetCommodityFilter(string? commodity) => SetTrafficFilter(commodity, _showTraffic);

    private float TileSize => Math.Max(1, Math.Min((Size.X - 64) / Surface.Width,
        (Size.Y - 56) / Surface.Height)) * Zoom;
    private Vector2 Origin => (Size - new Vector2(Surface.Width, Surface.Height) * TileSize) / 2 + _pan;
    public Vector2 CellToLocal(Vector2i cell) => Origin + new Vector2(cell.X + .5f, cell.Y + .5f) * TileSize;
    public SWPlanetSurfaceRoad? RoadAt(Vector2i cell) => _network.Roads.GetValueOrDefault(cell);
    public Vector2i? HitTestCell(Vector2 position)
    {
        if (position.X < 0 || position.Y < 0 || position.X >= Size.X || position.Y >= Size.Y) return null;
        var p = (position - Origin) / TileSize;
        var cell = new Vector2i((int) MathF.Floor(p.X), (int) MathF.Floor(p.Y));
        return cell.X >= 0 && cell.Y >= 0 && cell.X < Surface.Width && cell.Y < Surface.Height ? cell : null;
    }

    public void SelectSite(string id)
    {
        var site = Surface.Sites.FirstOrDefault(s => s.Id == id);
        if (site == null) return;
        SelectedSiteId = site.Id;
        SelectedCell = site.Cell;
        SiteSelected?.Invoke(id);
    }

    public void ActivateAt(Vector2 position)
    {
        if (HitTestCell(position) is not { } cell) return;
        SelectedCell = cell;
        SelectedSiteId = _sites.GetValueOrDefault(cell)?.Id;
        SiteSelected?.Invoke(SelectedSiteId);
    }

    public void ResetView()
    {
        Zoom = 1;
        _pan = Vector2.Zero;
        _hover = null;
        ViewChanged?.Invoke();
    }

    public void ChangeZoom(float factor, Vector2? around = null)
    {
        if (!float.IsFinite(factor) || factor <= 0) return;
        var old = Zoom;
        Zoom = Math.Clamp(Zoom * factor, 1, 4);
        var point = (around ?? Size / 2) - Size / 2;
        _pan = point - (point - _pan) * (Zoom / old);
        ClampPan();
        _hover = null;
        ViewChanged?.Invoke();
    }

    private void ClampPan()
    {
        var extent = new Vector2(Surface.Width, Surface.Height) * TileSize;
        var limit = Vector2.Max(Vector2.Zero, (extent - Size) / 2 + new Vector2(24));
        _pan = Vector2.Clamp(_pan, -limit, limit);
    }

    protected override void Resized()
    {
        base.Resized();
        ClampPan();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) return;
        _press = args.RelativePosition;
        _panAtPress = _pan;
        _dragged = false;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if ((args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) || _press == null) return;
        if (!_dragged && Vector2.DistanceSquared(args.RelativePosition, _press.Value) < 25) ActivateAt(args.RelativePosition);
        _press = null;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_press is { } press)
        {
            var delta = args.RelativePosition - press;
            _dragged |= delta.LengthSquared() >= 25;
            if (_dragged) { _pan = _panAtPress + delta; ClampPan(); }
        }
        _hover = HitTestCell(args.RelativePosition);
        ToolTip = _hover is { } cell ? (_sites.TryGetValue(cell, out var site) ? site.Name
            : RoadAt(cell) is { } road ? RoadName(road.Kind) : TerrainName(Surface.Terrain[cell.Y][cell.X])) : null;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _press = null;
        _hover = null;
        ToolTip = null;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        ChangeZoom(MathF.Pow(1.2f, args.Delta.Y), args.RelativePosition);
        args.Handle();
    }

    public static string RoadName(string kind) => kind switch
    {
        "bridge" => "Мост", "tunnel" => "Тоннель", "orbital" => "Орбитальная транспортная линия",
        "transfer" => "Перегрузочный узел поверхности и орбиты", _ => "Дорога",
    };

    public static string TerrainName(char terrain) => terrain switch
    {
        'f' => "Лес", 'm' => "Горный массив", 'w' => "Водоём", 'd' => "Засушливые земли",
        'c' => "Городская территория", 'i' => "Ледяное плато", 's' => "Заболоченные земли",
        'r' => "Скалистое плато", 'v' => "Вулканическая область", 'o' => "Орбитальная зона", _ => "Равнина",
    };

    private bool Matches(SWPlanetSurfaceSite site) => _commodity == null || _trafficSites.Contains(site.Id) || site.Recipes.Any(r =>
        _recipes[r.Id].Inputs.Keys.Any(g => g.Id == _commodity) || _recipes[r.Id].Outputs.Keys.Any(g => g.Id == _commodity));
}
