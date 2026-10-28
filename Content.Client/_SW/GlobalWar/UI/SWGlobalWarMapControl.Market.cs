using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

public enum SWGlobalWarMarketMapMode : byte
{
    Demand,
    Supply,
    Stock,
    Consumption,
    Incoming,
    Coverage,
    Balance,
}

public sealed partial class SWGlobalWarMapControl
{
    private const int MaxFreightMarkers = 256;
    private SWGlobalWarMarketConsoleState? _marketState;
    private TimeSpan _marketSampleTime;
    private ProtoId<SWGlobalWarCommodityPrototype>? _marketCommodity;
    private SWGlobalWarMarketMapMode _marketMode;
    private bool _showTraffic = true;
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarPlanetMarketView> _marketPlanets = new();
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, string> _marketLabels = new();
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarMarketTotals> _marketTotals = new();
    private readonly Dictionary<(ProtoId<SWGlobalWarPlanetPrototype>, ProtoId<SWGlobalWarPlanetPrototype>), SWGlobalWarMarketConnectionView> _marketLinks = new();
    private readonly List<(SWGlobalWarShipmentView Shipment, Route Path, bool Reverse, TimeSpan TravelTime)> _freight = new();
    private readonly HashSet<Route> _activeFreightRoutes = new();
    private readonly HashSet<Route> _filteredFreightRoutes = new();

    public TimeSpan MarketDisplayTime => _marketState == null ? TimeSpan.Zero : _marketState.Time
        + TimeSpan.FromSeconds(Math.Clamp((_timing.RealTime - _marketSampleTime).TotalSeconds, 0, 1));

    /// <summary>Updates the economic layer without replacing the atlas, its selection or its camera.</summary>
    /// <param name="state">Authoritative market inventories, occupied links and current shipment legs.</param>
    public void SetMarketState(SWGlobalWarMarketConsoleState state)
    {
        if (_marketState == null || _marketState.Time != state.Time || _marketState.Ready != state.Ready)
            _marketSampleTime = _timing.RealTime;
        _marketState = state;
        _marketPlanets.Clear();
        foreach (var planet in state.Planets)
            _marketPlanets[planet.Planet] = planet;
        SetState(state.Planets.Select(planet => new SWGlobalWarTravelDestination(planet.Planet, planet.Name, false, string.Empty)).ToList(),
            state.CurrentPlanet);
        if (_selectedRoute != null && !_routes.Contains(_selectedRoute))
            _selectedRoute = null;
        _marketLinks.Clear();
        foreach (var link in state.Connections)
            _marketLinks[(link.From, link.To)] = link;
        RefreshMarketLayer();
    }

    public void SetMarketFilter(ProtoId<SWGlobalWarCommodityPrototype>? commodity, SWGlobalWarMarketMapMode mode, bool showTraffic)
    {
        _marketCommodity = commodity;
        _marketMode = mode;
        _showTraffic = showTraffic;
        RefreshMarketLayer();
    }

    public void ClearSelection()
    {
        _selected = null;
        _selectedRoute = null;
        _lastClickPlanet = null;
        _labelsDirty = true;
    }

    private void RefreshMarketLayer()
    {
        _marketTotals.Clear();
        foreach (var (id, planet) in _marketPlanets)
        {
            var goods = planet.Goods.Where(good => _marketCommodity == null || good.Commodity == _marketCommodity).ToArray();
            var totals = SWGlobalWarMarketTotals.From(goods);
            _marketTotals[id] = totals;
            var text = goods.Length == 0 ? "—" : _marketMode switch
            {
                SWGlobalWarMarketMapMode.Demand => Loc.GetString("sw-global-war-market-map-demand", ("value", totals.Demand)),
                SWGlobalWarMarketMapMode.Supply => Loc.GetString("sw-global-war-market-map-supply", ("value", totals.Supply)),
                SWGlobalWarMarketMapMode.Stock => Loc.GetString("sw-global-war-market-map-stock", ("value", totals.Stock)),
                SWGlobalWarMarketMapMode.Consumption => Loc.GetString("sw-global-war-market-map-consumption", ("value", totals.Consumption)),
                SWGlobalWarMarketMapMode.Incoming => Loc.GetString("sw-global-war-market-map-incoming", ("value", totals.Incoming)),
                SWGlobalWarMarketMapMode.Balance => Loc.GetString("sw-global-war-market-map-balance", ("value", totals.Balance.ToString("+0;-0;0"))),
                _ => Loc.GetString("sw-global-war-market-map-coverage", ("value", SWGlobalWarMarketPresentation.Coverage(totals.Coverage))),
            };
            if (!_marketLabels.TryGetValue(id, out var previous) || previous != text)
                _labelsDirty = true;
            _marketLabels[id] = text;
        }
        foreach (var id in _marketLabels.Keys.Where(id => !_marketPlanets.ContainsKey(id)).ToArray())
        {
            _marketLabels.Remove(id);
            _labelsDirty = true;
        }

        // Senate-style colored trails follow real legs; a bounded marker budget keeps large economies readable.
        _freight.Clear();
        _activeFreightRoutes.Clear();
        _filteredFreightRoutes.Clear();
        if (_marketState != null)
        {
            var paths = new Dictionary<(ProtoId<SWGlobalWarPlanetPrototype>, ProtoId<SWGlobalWarPlanetPrototype>), (Route Path, bool Reverse)>();
            foreach (var route in _routes)
            {
                paths[(route.From, route.To)] = (route, false);
                if (route.Bidirectional)
                    paths[(route.To, route.From)] = (route, true);
            }
            foreach (var shipment in _marketState.Shipments)
            {
                if (_marketCommodity is { } commodity && shipment.Commodity != commodity
                    || !paths.TryGetValue((shipment.From, shipment.To), out var path)
                    || !_marketLinks.TryGetValue((shipment.From, shipment.To), out var link))
                    continue;
                _filteredFreightRoutes.Add(path.Path);
                if (shipment.ArrivesAt == null)
                    continue;
                _freight.Add((shipment, path.Path, path.Reverse, link.TravelTime));
                _activeFreightRoutes.Add(path.Path);
            }
        }
        UpdateHover();
    }

    private Color? MarketPlanetColor(ProtoId<SWGlobalWarPlanetPrototype> id)
    {
        if (!_marketTotals.TryGetValue(id, out var totals))
            return null;
        return _marketMode switch
        {
            SWGlobalWarMarketMapMode.Demand => totals.Demand > 0 ? SWGlobalWarConsoleStyle.Bad : SWGlobalWarConsoleStyle.TextDim,
            SWGlobalWarMarketMapMode.Supply => totals.Supply > 0 ? SWGlobalWarConsoleStyle.Good : SWGlobalWarConsoleStyle.TextDim,
            SWGlobalWarMarketMapMode.Balance => totals.Balance > 0 ? SWGlobalWarConsoleStyle.Good
                : totals.Balance < 0 ? SWGlobalWarConsoleStyle.Bad : SWGlobalWarConsoleStyle.TextDim,
            SWGlobalWarMarketMapMode.Incoming => totals.Incoming > 0 ? SWGlobalWarConsoleStyle.Accent : SWGlobalWarConsoleStyle.TextDim,
            SWGlobalWarMarketMapMode.Consumption => totals.Consumption > 0 ? SWGlobalWarConsoleStyle.Brass : SWGlobalWarConsoleStyle.TextDim,
            _ => totals.Demand > 0 ? SWGlobalWarConsoleStyle.Bad : totals.Stock > 0 ? SWGlobalWarConsoleStyle.Good : SWGlobalWarConsoleStyle.TextDim,
        };
    }

    private Color MarketRouteColor(Route route, Color color, bool focused) =>
        _marketState == null || !_showTraffic ? color : _activeFreightRoutes.Contains(route)
            ? (focused ? SWGlobalWarConsoleStyle.Accent : SWGlobalWarConsoleStyle.Accent.WithAlpha(.55f))
            : color.WithAlpha(focused ? .8f : .4f);

    private bool IsMarketRouteVisible(Route route) => _marketState == null || !_showTraffic || _marketCommodity == null
        || _filteredFreightRoutes.Contains(route);

    private string MarketPlanetToolTip(ProtoId<SWGlobalWarPlanetPrototype> id) =>
        _marketTotals.TryGetValue(id, out var totals) ? SWGlobalWarMarketPresentation.Summary(totals) : string.Empty;

    private string MarketRouteToolTip(Route route)
    {
        if (!_marketLinks.TryGetValue((route.From, route.To), out var link))
            return string.Empty;
        return Loc.GetString("sw-global-war-market-link-info", ("used", link.InFlightVolume.ToString("0.##")),
            ("capacity", link.Capacity.ToString("0.##")), ("seconds", link.TravelTime.TotalSeconds.ToString("0.##")));
    }

    private void DrawMarketTraffic(DrawingHandleScreen handle)
    {
        if (!_showTraffic || _marketState == null)
            return;
        var reduced = _configuration.GetCVar(CCVars.ReducedMotion);
        var count = 0;
        foreach (var (shipment, path, reverse, travelTime) in _freight.OrderByDescending(entry => entry.Path == _selectedRoute
                     || entry.Shipment.From == _selected || entry.Shipment.To == _selected))
        {
            if (count++ >= MaxFreightMarkers)
                break;
            var progress = reduced ? .5f : Math.Clamp(1 - (float) ((shipment.ArrivesAt!.Value - MarketDisplayTime).TotalSeconds
                / travelTime.TotalSeconds), 0, 1);
            var color = SWGlobalWarMarketPresentation.CommodityColor(shipment.Commodity);
            for (var segment = 4; segment > 0; segment--)
            {
                var a = FreightPoint(path, progress - segment * .015f, reverse, out _);
                var b = FreightPoint(path, progress - (segment - 1) * .015f, reverse, out _);
                Stroke(handle, a, b, color.WithAlpha(.10f), 7);
                Stroke(handle, a, b, color.WithAlpha(.3f + (4 - segment) * .15f), 1.5f);
            }
            var point = FreightPoint(path, progress, reverse, out var tangent);
            var normal = new Vector2(-tangent.Y, tangent.X);
            var bright = Color.InterpolateBetween(color, Color.White, .55f);
            Stroke(handle, point - tangent * 6 * UIScale + normal * 3 * UIScale, point, bright, 1.5f);
            Stroke(handle, point - tangent * 6 * UIScale - normal * 3 * UIScale, point, bright, 1.5f);
        }
    }

    private Vector2 FreightPoint(Route route, float progress, bool reverse, out Vector2 tangent)
    {
        var t = Math.Clamp(reverse ? 1 - progress : progress, 0, 1) * (route.Points.Length - 1);
        var index = Math.Min((int) t, route.Points.Length - 2);
        var a = MapToLocal(route.Points[index]) * UIScale;
        var b = MapToLocal(route.Points[index + 1]) * UIScale;
        var delta = b - a;
        tangent = delta.LengthSquared() < .001f ? Vector2.UnitX : Vector2.Normalize(delta) * (reverse ? -1 : 1);
        return Vector2.Lerp(a, b, t - index) + new Vector2(-tangent.Y, tangent.X) * 3 * UIScale;
    }
}
