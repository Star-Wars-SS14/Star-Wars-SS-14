using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWGalacticMapControl
{
    // Match the server's ordinal route tie-break without referencing StringComparer,
    // which is unavailable to launcher-loaded content in the engine sandbox.
    private static readonly IComparer<string> DefenseWorldIdComparer = Comparer<string>.Create(string.CompareOrdinal);
    private SWDefenseMapState? _defense;
    private double _defenseElapsed;
    private int? _defenseFleet;
    private string? _defenseDestination;
    private SWDefenseFleetOrder? _defenseDestinationOrder;
    private readonly List<string> _defensePreviewWorlds = new();
    private readonly HashSet<string> _defensePreviewEdges = new();
    private readonly Dictionary<(Vector2 Anchor, int Count), Vector2> _defenseClusterOrigins = new();
    public IReadOnlyList<string> DefensePreviewWorldIds => _defensePreviewWorlds;
    public IReadOnlyCollection<string> DefensePreviewEdgeIds => _defensePreviewEdges;
    public Vector2? DefenseDestinationPosition
    {
        get
        {
            var fleet = _defense?.Fleets.FirstOrDefault(f => f.Id == _defenseFleet);
            return fleet != null && DefenseWorld(_defenseDestination ?? fleet.TargetWorldId) is { } world
                ? MapToLocal(world.Position) : null;
        }
    }
    public event Action<int>? DefenseFleetSelected;
    public event Action<int>? DefenseEventSelected;

    public void SetDefense(SWDefenseMapState? state, int? selectedFleet = null)
    {
        _defense = state;
        _defenseElapsed = 0;
        _defenseFleet = selectedFleet;
        InvalidateDefenseLayout();
        _labelsDirty = true;
        _crisisLayoutDirty = true;
        UpdateDefenseSupply(state);
        RebuildDefenseRoute();
        UpdateHover();
    }

    /// <summary>A draft destination is presentation only. Null restores the selected fleet's current order.</summary>
    public void SetDefenseDestination(string? worldId, SWDefenseFleetOrder? order = null)
    {
        var draftOrder = worldId == null ? null : order;
        if (_defenseDestination == worldId && _defenseDestinationOrder == draftOrder) return;
        _defenseDestination = worldId;
        _defenseDestinationOrder = draftOrder;
        RebuildDefenseRoute();
    }

    /// <summary>Locate a fleet without changing the drafted world/route target or the zoom level.</summary>
    public bool FocusDefenseFleet(int id)
    {
        if (DefenseFleetPosition(id) is not { } position) return false;
        _defenseFleet = id;
        RebuildDefenseRoute();
        _pan += Size / 2 - position;
        ClampPan();
        ViewUpdated();
        return true;
    }

    private void AdvanceDefense(float seconds)
    {
        if (_defense == null || !_defense.ClockRunning || !float.IsFinite(seconds) || seconds <= 0 || _defense.RealSecondsPerHour <= 0) return;
        _defenseElapsed += Math.Min(seconds, .25f) / _defense.RealSecondsPerHour;
        AdvanceDefenseSupply(seconds);
        AdvanceDefenseTasks(seconds);
    }

    /// <summary>Uses the authoritative current leg, including its actual quadratic hyperlane.</summary>
    public Vector2? DefenseFleetPosition(int id)
    {
        var fleet = _defense?.Fleets.FirstOrDefault(f => f.Id == id);
        if (fleet == null || fleet.Phase == SWDefenseOperationPhase.Lost) return null;
        if (DefenseTravelPosition(fleet) is { } travel)
        {
            // Simultaneous ships share one physical point, but retain separate click targets.
            var peers = _defense!.Fleets.Where(f => f.Phase == SWDefenseOperationPhase.Travel
                && f.CurrentWorldId == fleet.CurrentWorldId && f.NextWorldId == fleet.NextWorldId
                && Math.Abs(f.LegStartedAt - fleet.LegStartedAt) < .0001 && Math.Abs(f.EtaHours - fleet.EtaHours) < .0001)
                .OrderBy(f => f.Id).ToArray();
            return peers.Length == 1 ? travel : DefenseClusterPosition(travel,
                Array.FindIndex(peers, f => f.Id == id), peers.Length, false);
        }
        if (DefenseWorld(fleet.CurrentWorldId) is not { } world) return null;
        var cluster = _defense!.Fleets.Where(f => f.Phase != SWDefenseOperationPhase.Lost
            && DefenseWorld(f.CurrentWorldId)?.Id == world.Id && DefenseTravelPosition(f) == null).OrderBy(f => f.Id).ToArray();
        var index = Array.FindIndex(cluster, f => f.Id == id);
        var anchor = MapToLocal(world.Position);
        return DefenseClusterPosition(anchor, index, cluster.Length, true);
    }

    private Vector2 DefenseClusterPosition(Vector2 anchor, int index, int count, bool atWorld)
    {
        var columns = Math.Min(count, Math.Clamp((int) ((Width - 40) / 72) + 1, 1, 3));
        var rows = (count + columns - 1) / columns;
        var first = anchor + new Vector2(-(columns - 1) * 36, atWorld ? -54 - (rows - 1) * 60 : -(rows - 1) * 30);
        if (atWorld && _defenseClusterOrigins.TryGetValue((anchor, count), out var cached))
            return cached + new Vector2(index % columns * 72, index / columns * 60);
        if (UIBox2.FromDimensions(Vector2.Zero, Size).Contains(anchor))
        {
            if (atWorld && first.Y < 18) first.Y = anchor.Y + 34;
            first.X = Math.Clamp(first.X, 18, Math.Max(18, Width - (columns - 1) * 72 - 42));
            first.Y = Math.Clamp(first.Y, 18, Math.Max(18, Height - (rows - 1) * 60 - 39));
        }
        if (atWorld)
        {
            var preferred = first;
            var bestCost = float.PositiveInfinity;
            // A fixed offset can put Coruscant's ship over Anaxes. Find a nearby
            // free cluster footprint, preserving both actual nodes as click targets.
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
            {
                var candidate = preferred + new Vector2(x * 56, y * 60);
                if (UIBox2.FromDimensions(Vector2.Zero, Size).Contains(anchor))
                {
                    candidate.X = Math.Clamp(candidate.X, 18, Math.Max(18, Width - (columns - 1) * 72 - 42));
                    candidate.Y = Math.Clamp(candidate.Y, 18, Math.Max(18, Height - (rows - 1) * 60 - 39));
                }
                var cost = Vector2.DistanceSquared(candidate, preferred);
                for (var cell = 0; cell < count; cell++)
                {
                    var point = candidate + new Vector2(cell % columns * 72, cell / columns * 60);
                    var footprint = new UIBox2(point - new Vector2(27), point + new Vector2(48, 43));
                    foreach (var world in _map.Worlds)
                        if (footprint.Contains(MapToLocal(world.Position))) cost += 1000000;
                    foreach (var other in _defenseClusterOrigins)
                    {
                        var otherColumns = Math.Min(other.Key.Count, Math.Clamp((int) ((Width - 40) / 72) + 1, 1, 3));
                        var otherRows = (other.Key.Count + otherColumns - 1) / otherColumns;
                        var otherBounds = new UIBox2(other.Value - new Vector2(20),
                            other.Value + new Vector2((otherColumns - 1) * 72 + 48, (otherRows - 1) * 60 + 39));
                        if (footprint.Intersects(otherBounds)) cost += 1000000;
                    }
                }
                if (cost >= bestCost) continue;
                bestCost = cost;
                first = candidate;
            }
            _defenseClusterOrigins[(anchor, count)] = first;
        }
        return first + new Vector2(index % columns * 72, index / columns * 60);
    }

    private void InvalidateDefenseLayout() => _defenseClusterOrigins.Clear();

    private Vector2? DefenseTravelPosition(SWDefenseFleetView fleet)
    {
        if (fleet.Phase != SWDefenseOperationPhase.Travel || fleet.NextWorldId is not { } next) return null;
        var path = _paths.FirstOrDefault(p => p.Route.From == fleet.CurrentWorldId && p.Route.To == next
            || p.Route.To == fleet.CurrentWorldId && p.Route.From == next);
        if (path.Route == null) return null;
        var duration = fleet.EtaHours - fleet.LegStartedAt;
        var fraction = duration <= 0 ? 1f : (float) Math.Clamp((_defense!.NowHours + _defenseElapsed - fleet.LegStartedAt) / duration, 0, 1);
        if (path.Route.From != fleet.CurrentWorldId) fraction = 1 - fraction;
        var index = fraction * (path.Points.Length - 1);
        var start = Math.Min((int) index, path.Points.Length - 2);
        return MapToLocal(Vector2.Lerp(path.Points[start], path.Points[start + 1], index - start));
    }

    /// <summary>UI-coordinate bounds include the ship, number badge and active task tile.</summary>
    public UIBox2? DefenseFleetHitBox(int id)
    {
        if (DefenseFleetPosition(id) is not { } position) return null;
        var badge = DefenseBadgeBox(position + new Vector2(16, -9), id.ToString());
        var bottom = DefenseFleetTaskIndicator(id) == null ? 17 : 38;
        return new UIBox2(position - new Vector2(17), new Vector2(Math.Max(position.X + 17, badge.Right + 3), position.Y + bottom));
    }

    public int? HitTestDefenseFleet(Vector2 point)
    {
        if (_defense == null || !UIBox2.FromDimensions(Vector2.Zero, Size).Contains(point)) return null;
        return _defense.Fleets.Where(f => DefenseFleetHitBox(f.Id)?.Contains(point) == true)
            .OrderBy(f => Vector2.DistanceSquared(DefenseFleetPosition(f.Id)!.Value, point)).ThenBy(f => f.Id)
            .Select(f => (int?) f.Id).FirstOrDefault();
    }

    private SWGalacticMapWorld? DefenseWorld(string id)
    {
        if (_worlds.TryGetValue(id, out var world)) return world;
        // Local legs stay at their real sector anchor on the galaxy projection.
        var sector = _defense?.Worlds.FirstOrDefault(w => w.Id == id)?.SectorId;
        return sector == null ? null : _map.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == sector);
    }

    private Vector2? DefenseEventPosition(SWDefenseEventView incident)
    {
        if (!string.IsNullOrEmpty(incident.EdgeId))
        {
            var path = _paths.FirstOrDefault(p => SWTransitLinkPrototype.LinkId(p.Route.From, p.Route.To) == incident.EdgeId);
            if (path.Route != null) return MapToLocal(path.Points[path.Points.Length / 2]);
        }
        return DefenseWorld(incident.WorldId) is { } world ? MapToLocal(world.Position) + new Vector2(-15, 17) : null;
    }

    private bool ActivateDefenseAt(Vector2 point)
    {
        if (_defense == null) return false;
        if (HitTestDefenseFleet(point) is { } fleetId)
        {
            _defenseFleet = fleetId;
            RebuildDefenseRoute();
            DefenseFleetSelected?.Invoke(fleetId);
            return true;
        }
        // A world node is a precise target. Adjacent event padding or a wide border
        // caption must not turn a direct node click into another kind of order.
        if (DefenseWorldNodeAt(point)) return false;
        foreach (var incident in _defense.Events.Where(e => !e.Resolved))
        {
            if (DefenseEventPosition(incident) is not { } position || Vector2.Distance(point, position) > 12) continue;
            DefenseEventSelected?.Invoke(incident.Id);
            return true;
        }
        if (DefenseBorderAt(point) is { } border)
        {
            var route = _paths.FirstOrDefault(p => SWTransitLinkPrototype.LinkId(p.Route.From, p.Route.To) == border.Id).Route;
            if (route == null) return false;
            SelectRoute(route);
            RouteSelected?.Invoke(route);
            return true;
        }
        return false;
    }

    private string? DefenseToolTip(Vector2 point)
    {
        if (_defense == null) return null;
        if (HitTestDefenseFleet(point) is { } fleetId)
        {
            var fleet = _defense.Fleets.First(f => f.Id == fleetId);
            var eta = fleet.Phase is SWDefenseOperationPhase.Standing or SWDefenseOperationPhase.Lost ? ""
                : $"\nДо следующего этапа: {SWSenateDuration.FormatHours(Math.Max(0, fleet.EtaHours - _defense.NowHours - _defenseElapsed))}";
            return $"{fleet.Name}\n{fleet.Status}{eta}\nСила {fleet.Strength:0}; снабжение {fleet.Supply:0}\nНажмите, чтобы выбрать";
        }
        if (DefenseWorldNodeAt(point)) return null;
        foreach (var incident in _defense.Events.Where(e => !e.Resolved))
        {
            if (DefenseEventPosition(incident) is not { } position || Vector2.Distance(point, position) > 12) continue;
            return $"Событие {incident.Id}: {incident.Title}\n{incident.Status}\nУгроза {incident.Severity:0}; противник {incident.EnemyStrength:0}\n{incident.Risk}";
        }
        if (DefenseBorderAt(point) is { } border)
            return $"{DefenseWorldName(border.RepublicWorldId)} — {DefenseWorldName(border.ForeignWorldId)}\nПрикрытие {border.Coverage:0}; давление {border.Pressure:0}";
        return null;
    }

    private bool DefenseWorldNodeAt(Vector2 point) => _map.Worlds.Any(w =>
        Vector2.DistanceSquared(MapToLocal(w.Position), point) <= 8 * 8);

    private string DefenseWorldName(string id) => _defense?.Worlds.FirstOrDefault(w => w.Id == id)?.Name
        ?? _worlds.GetValueOrDefault(id)?.Name ?? id;

    private static string BorderBadgeText(SWDefenseBorderView border) => border.Coverage > 0 && border.Coverage >= border.Pressure
        ? "Прикрыт" : border.Pressure >= 50 ? "Угроза" : "Без прикрытия";

    private Vector2? DefenseBorderPosition(SWDefenseBorderView border)
    {
        var path = _paths.FirstOrDefault(p => p.Route.From == border.RepublicWorldId && p.Route.To == border.ForeignWorldId
            || p.Route.To == border.RepublicWorldId && p.Route.From == border.ForeignWorldId);
        return path.Route == null ? null : MapToLocal(path.Points[path.Points.Length / 2]) + new Vector2(0, -24);
    }

    private SWDefenseBorderView? DefenseBorderAt(Vector2 point) => _defense?.Borders.FirstOrDefault(b =>
        DefenseBorderPosition(b) is { } position && DefenseBadgeBox(position, BorderBadgeText(b)).Contains(point));

    private void RebuildDefenseRoute()
    {
        _defensePreviewWorlds.Clear();
        _defensePreviewEdges.Clear();
        var fleet = _defense?.Fleets.FirstOrDefault(f => f.Id == _defenseFleet && f.Phase != SWDefenseOperationPhase.Lost);
        if (fleet == null) return;
        var target = _defenseDestination ?? fleet.TargetWorldId;
        // Redirects, including new movement drafts, replace only the route after the
        // committed crossing. The physical current leg is never cancelled by a click.
        var origin = fleet.Phase == SWDefenseOperationPhase.Travel && fleet.NextWorldId != null
            ? fleet.NextWorldId : fleet.CurrentWorldId;
        var path = DefenseShortestPath(origin, target, _defenseDestinationOrder ?? fleet.Order);
        if (origin != fleet.CurrentWorldId) _defensePreviewWorlds.Add(fleet.CurrentWorldId);
        if (path != null)
        {
            _defensePreviewWorlds.AddRange(path);
        }
        else if (origin != fleet.CurrentWorldId) _defensePreviewWorlds.Add(origin);
        for (var i = 1; i < _defensePreviewWorlds.Count; i++)
            _defensePreviewEdges.Add(SWTransitLinkPrototype.LinkId(_defensePreviewWorlds[i - 1], _defensePreviewWorlds[i]));
        if (_defenseDestination == null && !string.IsNullOrEmpty(fleet.PatrolEdgeId))
            _defensePreviewEdges.Add(fleet.PatrolEdgeId);
    }

    private List<string>? DefenseShortestPath(string origin, string target, SWDefenseFleetOrder order)
    {
        if (_defense == null || string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(target)) return null;
        var foreignOrigin = _defense.Worlds.FirstOrDefault(w => w.Id == origin)?.Faction == "empire";
        var military = order == SWDefenseFleetOrder.Respond || foreignOrigin && order is SWDefenseFleetOrder.Return or SWDefenseFleetOrder.Move;
        var distance = new Dictionary<string, double> { [origin] = 0 };
        var previous = new Dictionary<string, string>();
        var pending = new HashSet<string> { origin };
        var visited = new HashSet<string>();
        while (pending.Count > 0)
        {
            var current = pending.OrderBy(id => distance[id]).ThenBy(id => id, DefenseWorldIdComparer).First();
            pending.Remove(current);
            if (current == target)
            {
                var result = new List<string> { current };
                while (previous.TryGetValue(current, out var parent)) { result.Add(parent); current = parent; }
                result.Reverse();
                return result;
            }
            visited.Add(current);
            foreach (var link in _defense.Links.Where(l => double.IsFinite(l.TravelHours)
                         && l.TravelHours >= 0 && (l.From == current || l.To == current)))
            {
                var next = link.From == current ? link.To : link.From;
                if (visited.Contains(next)) continue;
                var nextEmpire = _defense.Worlds.FirstOrDefault(w => w.Id == next)?.Faction == "empire";
                var imperial = nextEmpire || _defense.Worlds.FirstOrDefault(w => w.Id == current)?.Faction == "empire";
                // Traversable includes DEFCON-authorized military edges. Ordinary
                // movement must still use civilian-accessible lines and avoid entry
                // into imperial space; fleets already abroad retain a return route.
                if (!(link.Traversable && !link.MilitaryOnly || military && imperial && (link.Traversable || foreignOrigin))) continue;
                if (nextEmpire && !military && !foreignOrigin) continue;
                var hours = distance[current] + link.TravelHours;
                if (distance.TryGetValue(next, out var old) && old <= hours) continue;
                distance[next] = hours;
                previous[next] = current;
                pending.Add(next);
            }
        }
        return null;
    }

    private void DrawDefenseRoutes(DrawingHandleScreen handle)
    {
        if (_defense == null || _defenseFleet == null) return;
        var visible = new HashSet<string>(_defensePreviewEdges);
        // The overview omits local nodes. Project each actual leg to its real anchor;
        // never invent a straight cross-galaxy connection for a hidden local leg.
        foreach (var link in _defense.Links.Where(l => _defensePreviewEdges.Contains(l.Id)))
            if (DefenseWorld(link.From) is { } from && DefenseWorld(link.To) is { } to && from.Id != to.Id)
                visible.Add(SWTransitLinkPrototype.LinkId(from.Id, to.Id));
        var color = _defenseDestination == null ? SWConsoleStyle.Accent : SWConsoleStyle.Brass;
        foreach (var path in _paths.Where(p => visible.Contains(SWTransitLinkPrototype.LinkId(p.Route.From, p.Route.To))))
            for (var i = 1; i < path.Points.Length; i++)
            {
                var a = MapToLocal(path.Points[i - 1]) * UIScale;
                var b = MapToLocal(path.Points[i]) * UIScale;
                Stroke(handle, a, b, SWConsoleStyle.Navigation, 7);
                Stroke(handle, a, b, color, 3.5f);
            }
        if (DefenseDestinationPosition is { } destination)
            handle.DrawCircle(destination * UIScale, 12 * UIScale, color, false);
    }

    private void DrawDefense(DrawingHandleScreen handle)
    {
        if (_defense == null) return;
        foreach (var border in _defense.Borders)
        {
            if (DefenseBorderPosition(border) is not { } position) continue;
            var color = border.Coverage > 0 && border.Coverage >= border.Pressure ? SWConsoleStyle.Good : SWConsoleStyle.Warn;
            Badge(handle, position, BorderBadgeText(border), color);
        }
        foreach (var incident in _defense.Events.Where(e => !e.Resolved))
        {
            if (DefenseEventPosition(incident) is not { } position) continue;
            var point = position * UIScale;
            var r = 7 * UIScale;
            var color = incident.Severity >= 60 ? SWConsoleStyle.Bad : SWConsoleStyle.Warn;
            var diamond = new[] { point + new Vector2(0, -r), point + new Vector2(r, 0), point + new Vector2(0, r), point + new Vector2(-r, 0) };
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, diamond, SWConsoleStyle.Navigation);
            for (var i = 0; i < diamond.Length; i++) handle.DrawLine(diamond[i], diamond[(i + 1) % diamond.Length], color);
            Badge(handle, position + new Vector2(11, -5), $"{incident.Id}", color);
        }
        foreach (var fleet in _defense.Fleets)
        {
            if (DefenseFleetPosition(fleet.Id) is not { } position) continue;
            var point = position * UIScale;
            var selected = fleet.Id == _defenseFleet;
            var hovered = _pointer is { } pointer && HitTestDefenseFleet(pointer) == fleet.Id;
            var color = selected ? SWConsoleStyle.TextBright : fleet.Damage >= 60 ? SWConsoleStyle.Warn : SWConsoleStyle.Accent;
            if (DefenseFleetHitBox(fleet.Id) is { } bounds)
            {
                handle.DrawRect(new UIBox2(bounds.TopLeft * UIScale, bounds.BottomRight * UIScale), selected
                    ? SWConsoleStyle.Selected : hovered ? SWConsoleStyle.Hover : SWConsoleStyle.Navigation.WithAlpha(.92f));
                if (selected || hovered)
                    handle.DrawRect(new UIBox2(bounds.TopLeft * UIScale, bounds.BottomRight * UIScale), selected ? SWConsoleStyle.Accent : SWConsoleStyle.Edge, false);
            }
            if (DefenseTravelPosition(fleet) is { } physical && Vector2.Distance(physical, position) > 2)
                handle.DrawLine(physical * UIScale, point, SWConsoleStyle.Edge);
            else if (DefenseWorld(fleet.CurrentWorldId) is { } world)
            {
                var anchor = MapToLocal(world.Position);
                if (Vector2.Distance(anchor, position) > 25)
                {
                    var direction = Vector2.Normalize(position - anchor);
                    handle.DrawLine((anchor + direction * 8) * UIScale, (position - direction * 12) * UIScale, SWConsoleStyle.Edge);
                }
            }
            var r = 11 * UIScale;
            if (selected) handle.DrawCircle(point, 15 * UIScale, SWConsoleStyle.Accent, false);
            else if (hovered) handle.DrawCircle(point, 14 * UIScale, SWConsoleStyle.TextDim, false);
            var ship = new[] { point + new Vector2(0, -r), point + new Vector2(r * .8f, r * .8f),
                point + new Vector2(r * .25f, r * .55f), point + new Vector2(0, r),
                point + new Vector2(-r * .25f, r * .55f), point + new Vector2(-r * .8f, r * .8f) };
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, ship, color);
            handle.DrawLine(point + new Vector2(0, -5) * UIScale, point + new Vector2(0, 5) * UIScale, SWConsoleStyle.Navigation);
            Badge(handle, position + new Vector2(16, -9), $"{fleet.Id}", color);
        }
        // Draw activity last: another fleet's silhouette or number cannot erase it.
        foreach (var fleet in _defense.Fleets)
            DrawDefenseTask(handle, fleet.Id);
    }

    private void Badge(DrawingHandleScreen handle, Vector2 position, string text, Color color)
    {
        var box = DefenseBadgeBox(position, text);
        handle.DrawRect(new UIBox2(box.TopLeft * UIScale, box.BottomRight * UIScale), SWConsoleStyle.Navigation);
        handle.DrawString(SWConsoleStyle.Small, (position + new Vector2(0, 2)) * UIScale, text, UIScale, color);
    }

    private static UIBox2 DefenseBadgeBox(Vector2 position, string text) =>
        UIBox2.FromDimensions(position - new Vector2(3, 1), new Vector2(Math.Max(10, text.Length * 7) + 6, 20));
}
