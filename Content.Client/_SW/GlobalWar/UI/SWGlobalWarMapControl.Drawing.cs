using System.Linq;
using System.Numerics;
using Content.Client._SW.UserInterface;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMapControl
{
    private const string ArtPath = "_SW/Interface/MegaMode/";
    private const int SphereBlock = 2;
    private const float SphereReach = 0.17f;
    private const float RunningDashSpeed = 24f;

    private static readonly float[,] Bayer =
    {
        { 0 / 16f, 8 / 16f, 2 / 16f, 10 / 16f },
        { 12 / 16f, 4 / 16f, 14 / 16f, 6 / 16f },
        { 3 / 16f, 11 / 16f, 1 / 16f, 9 / 16f },
        { 15 / 16f, 7 / 16f, 13 / 16f, 5 / 16f },
    };

    // The engine blends in linear light, so these alphas are lower or higher than the mock's to give the same result.
    private static readonly Color FaintRoute = Color.FromHex("#96A0BE0E");
    private static readonly Color FocusRoute = Color.FromHex("#96A0BE66");
    private static readonly Color Plate = Color.FromHex("#07090EE6");
    private static readonly Color Unreachable = Color.FromHex("#7B7B7B");
    private static readonly Color Freight = Color.FromHex("#E9C17E");
    private static readonly Color RepublicText = Color.FromHex("#6FB7FF");
    private static readonly Color EmpireText = Color.FromHex("#FF9991");
    private static readonly Color Reachable = Color.FromHex("#5CB353");

    // Reuse vertex storage to keep the content assembly compatible with the launcher's IL verifier.
    private readonly Vector2[] _strokeVertices = new Vector2[4];
    private Image<Rgba32>? _spaceImage;
    private Texture? _chart;
    private Texture? _chartBefore;
    private TimeSpan _chartBaked;
    private const float ChartFadeSeconds = .3f;
    private const float BreathSeconds = 1.2f;
    private const float RingPeriodSeconds = 3;
    private const float RingSeconds = 1.2f;
    private const float RingGrowth = 26;
    private string _spheresKey = string.Empty;

    /// <summary>Straight lanes between planets, directed when the return connection is not configured.</summary>
    private void RebuildRoutes()
    {
        _routes.Clear();
        foreach (var (id, planet) in _planets.OrderBy(pair => pair.Key.Id))
        foreach (var target in (_marketState == null ? planet.Connections :
                     _marketState.Connections.Where(link => link.From == id).Select(link => link.To)).OrderBy(target => target.Id))
        {
            if (target == id || !_planets.TryGetValue(target, out var other))
                continue;
            var bidirectional = _marketState == null ? other.Connections.Contains(id) :
                _marketState.Connections.Any(link => link.From == target && link.To == id);
            if (bidirectional && string.CompareOrdinal(id.Id, target.Id) > 0)
                continue;

            var points = new Vector2[RoutePointCount];
            for (var i = 0; i < points.Length; i++)
                points[i] = Vector2.Lerp(_positions[id], _positions[target], i / (float) (points.Length - 1));
            _routes.Add(new Route(id, target, bidirectional, points));
        }
    }

    private bool IsRouteFocused(Route route) =>
        route == _selectedRoute || route == _hoveredRoute
        || _selected != null && (route.From == _selected || route.To == _selected)
        || _hovered != null && (route.From == _hovered || route.To == _hovered);

    private bool IsRouteClickable(Route route) => IsMarketRouteVisible(route) && (IsRouteFocused(route) || _activeFreightRoutes.Contains(route));

    private static Texture Art(string rsi, string state) => IoCManager.Resolve<IEntityManager>()
        .System<SpriteSystem>().Frame0(new SpriteSpecifier.Rsi(new ResPath(ArtPath + rsi + ".rsi"), state));

    /// <summary>A planet's sprite from one of the three sets, by the sector code that ends its prototype ID.</summary>
    public static Texture PlanetTexture(ProtoId<SWGlobalWarPlanetPrototype> id, string set = "planets")
    {
        var code = id.Id.Replace("SWGlobalWar", string.Empty).ToLowerInvariant();
        var known = code.Length == 2 && code[0] is 'r' or 'e' or 'n' && char.IsDigit(code[^1]);
        return Art(set, known ? code : "r3");
    }

    private static Texture FactionIcon(SWGlobalWarFaction faction) => IoCManager.Resolve<IEntityManager>().System<SpriteSystem>()
        .Frame0(new SpriteSpecifier.Rsi(new ResPath("_SW/Interface/Misc/job_icons.rsi"),
            faction == SWGlobalWarFaction.Republic ? "RepublicSoldier" : "EmpireSoldier"));

    /// <summary>Art is drawn at a whole multiple of its pixels, and a close view steps up one multiple at a time.</summary>
    private int ArtScale => Math.Max(1, (int) MathF.Round(SWGlobalWarConsoleStyle.ArtScale * MathF.Min(_drawZoom, 2)));

    /// <summary>Half the drawn planet in real pixels, with the rim the sprite keeps around its disc.</summary>
    private float PlanetHalf(ProtoId<SWGlobalWarPlanetPrototype> id) => (PlanetDiameter(id) + 4) * ArtScale / 2f;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), SWGlobalWarConsoleStyle.Navigation);
        DrawSpace(handle);

        foreach (var route in _routes)
        {
            if (IsMarketRouteVisible(route))
                DrawLane(handle, route);
        }

        if (_marketState == null)
            DrawJumpLines(handle);
        DrawMarketTraffic(handle);
        foreach (var id in _planets.Keys)
            DrawPlanet(handle, id);
        foreach (var id in _planets.Keys)
        {
            DrawMarks(handle, id);
            DrawLabel(handle, id);
        }

        DrawZoneTitles(handle);

        if (_planets.Count == 0)
        {
            var text = Loc.GetString("sw-global-war-travel-empty-map");
            var size = handle.GetDimensions(SWGlobalWarConsoleStyle.Body, text, UIScale);
            handle.DrawString(SWGlobalWarConsoleStyle.Body, (PixelSize - size) / 2, text, UIScale, SWGlobalWarConsoleStyle.TextDim);
        }
    }

    /// <summary>The chart fills the panel and follows the camera.</summary>
    private void DrawSpace(DrawingHandleScreen handle)
    {
        BakeChart();
        if (_chart == null)
            return;

        var center = (Size / 2 + _pan) * UIScale;
        var half = (Vector2) PixelSize * _drawZoom / 2;
        var box = new UIBox2(center - half, center + half);
        handle.DrawTextureRect(_chart, box);

        // A moved border fades over the old one.
        if (_chartBefore == null)
            return;
        var fade = (float) (_timing.RealTime - _chartBaked).TotalSeconds / ChartFadeSeconds;
        if (fade >= 1 || _configuration.GetCVar(CCVars.ReducedMotion))
        {
            (_chartBefore as IDisposable)?.Dispose();
            _chartBefore = null;
            return;
        }

        handle.DrawTextureRect(_chartBefore, box, Color.White.WithAlpha(1 - fade));
    }

    /// <summary>
    /// Space with the spheres of influence painted in. One field per side from the planets it holds now, stepped and
    /// dithered, with a line where two sides meet. Painted here and not drawn over, because blending in linear light
    /// would wash the picture out. This runs only when an owner changes, so that a captured world moves the border.
    /// </summary>
    private void BakeChart()
    {
        var key = string.Join(',', _positions.Keys.OrderBy(id => id.Id).Select(id => (int) _owners.GetValueOrDefault(id)));
        if (key == _spheresKey || Width <= 0 || Height <= 0)
            return;

        _spheresKey = key;
        if (_spaceImage == null)
        {
            // A plain PNG, not an RSI state: the release client packs every RSI into one file, so a state's own PNG
            // is not there to read, and the chart stayed black.
            using var stream = IoCManager.Resolve<IResourceManager>().ContentFileRead(new ResPath("/Textures/" + ArtPath + "windows/space.png"));
            _spaceImage = Image.Load<Rgba32>(stream);
        }

        var width = _spaceImage.Width / SphereBlock;
        var height = _spaceImage.Height / SphereBlock;
        var aspect = Width / Height;
        var best = new int[width, height];
        var strength = new float[width, height];
        var reach = SphereReach * SphereReach;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var u = x / (float) width;
            var v = y / (float) height;
            float republic = 0, empire = 0, neutral = 0;
            foreach (var (id, position) in _positions)
            {
                var big = PlanetDiameter(id) > PlanetDiameterSmall ? 1.25f : 1f;
                var dx = (u - position.X) * aspect;
                var dy = v - position.Y;
                var field = big * MathF.Exp(-(dx * dx + dy * dy) / reach);
                switch (_owners.GetValueOrDefault(id))
                {
                    case SWGlobalWarFaction.Republic: republic += field; break;
                    case SWGlobalWarFaction.Empire: empire += field; break;
                    default: neutral += field; break;
                }
            }

            neutral *= .8f;
            var value = MathF.Max(republic, MathF.Max(empire, neutral));
            best[x, y] = value == republic ? 0 : value == empire ? 1 : 2;
            strength[x, y] = value;
        }

        var colours = new[] { new Rgba32(62, 155, 224), new Rgba32(224, 29, 30), new Rgba32(142, 151, 168) };
        using var image = new Image<Rgba32>(_spaceImage.Width, _spaceImage.Height);
        // The sandbox forbids the Image<T> pixel getter, so read the source through its span.
        var space = _spaceImage.GetPixelSpan();
        for (var py = 0; py < image.Height; py++)
        for (var px = 0; px < image.Width; px++)
        {
            var x = Math.Min(px / SphereBlock, width - 1);
            var y = Math.Min(py / SphereBlock, height - 1);
            var side = best[x, y];
            var level = Math.Clamp(MathF.Floor((strength[x, y] - .12f) * 3.2f + Bayer[y % 4, x % 4] * .5f), 0, 4) / 4;
            var paint = colours[side];
            var alpha = level * (side < 2 ? 44f : 20f) / 255f;
            var edge = (x + 1 < width && best[x + 1, y] != side || y + 1 < height && best[x, y + 1] != side) && strength[x, y] > .03f;
            if (edge)
            {
                paint = new Rgba32(190, 196, 214);
                alpha = Math.Clamp(strength[x, y] / .34f, 0, 1) * 80f / 255f;
            }

            var under = space[py * _spaceImage.Width + px];
            image[px, py] = new Rgba32(
                (byte) (under.R + (paint.R - under.R) * alpha),
                (byte) (under.G + (paint.G - under.G) * alpha),
                (byte) (under.B + (paint.B - under.B) * alpha));
        }

        (_chartBefore as IDisposable)?.Dispose();
        _chartBefore = _chart;
        _chartBaked = _timing.RealTime;
        _chart = Texture.LoadFromImage(image);
    }

    private void DrawLane(DrawingHandleScreen handle, Route route)
    {
        var scale = ArtScale;
        var (a, b) = ClippedLine(_positions[route.From], _positions[route.To], 18 * scale, 18 * scale);
        var focused = IsRouteFocused(route);
        var color = MarketRouteColor(route, focused ? FocusRoute : FaintRoute, focused);
        if (_marketState != null && _activeFreightRoutes.Contains(route) && _showTraffic)
            Stroke(handle, a, b, color, MathF.Max(2, scale) / UIScale);
        else
            handle.DrawLine(a, b, color);
        if (!route.Bidirectional && focused)
            DrawDirection(handle, route, color);
    }

    /// <summary>The way out of here: green dashes to every jump target, white to the chosen one, running outward.</summary>
    private void DrawJumpLines(DrawingHandleScreen handle)
    {
        if (_current is not { } current || !_positions.TryGetValue(current, out var from))
            return;

        var scale = ArtScale;
        var run = _configuration.GetCVar(CCVars.ReducedMotion) ? 0 : (float) _timing.RealTime.TotalSeconds * RunningDashSpeed * scale;
        foreach (var target in _available)
        {
            if (target == current || !_positions.TryGetValue(target, out var to))
                continue;
            var (a, b) = ClippedLine(from, to, 24 * scale, 15 * scale);
            DrawDashes(handle, a, b, target == _selected ? Color.White : Reachable, scale, run);
        }
    }

    private void DrawDashes(DrawingHandleScreen handle, Vector2 a, Vector2 b, Color color, int scale, float run)
    {
        var delta = b - a;
        var length = delta.Length();
        if (length < 1)
            return;

        var direction = delta / length;
        var dash = 5f * scale;
        var period = 8f * scale;
        for (var position = run % period - period; position < length; position += period)
        {
            var start = MathF.Max(position, 0);
            var end = MathF.Min(position + dash, length);
            if (end > start)
                Stroke(handle, a + direction * start, a + direction * end, color, scale / UIScale);
        }
    }

    /// <summary>A line between two planets in real pixels, stopped short of each so that it does not cross the sprite.</summary>
    private (Vector2 A, Vector2 B) ClippedLine(Vector2 from, Vector2 to, float cutFrom, float cutTo)
    {
        var a = MapToLocal(from) * UIScale;
        var b = MapToLocal(to) * UIScale;
        var length = MathF.Max(1, Vector2.Distance(a, b));
        var direction = (b - a) / length;
        return (a + direction * MathF.Min(cutFrom, length / 2), b - direction * MathF.Min(cutTo, length / 2));
    }

    private void DrawPlanet(DrawingHandleScreen handle, ProtoId<SWGlobalWarPlanetPrototype> id)
    {
        var point = Snap(MapToLocal(_positions[id]) * UIScale);
        var scale = ArtScale;
        var selected = id == _selected;
        var current = id == _current;
        var target = _marketState == null && _available.Contains(id);

        // In the console a planet out of reach steps back. On the market every planet is equal.
        var dim = !Overview && _marketState == null && !current && !target && !selected && id != _hovered;
        var texture = PlanetTexture(id);
        var half = new Vector2(texture.Width, texture.Height) * scale / 2;
        handle.DrawTextureRect(texture, new UIBox2(point - half, point + half), dim ? Unreachable : Color.White);

    }

    /// <summary>Marks and the owner's sign go over all planets, so that a neighbour never covers them.</summary>
    private static readonly Color RingColor = Color.FromHex("#9CC4F0");

    private void DrawMarks(DrawingHandleScreen handle, ProtoId<SWGlobalWarPlanetPrototype> id)
    {
        var point = Snap(MapToLocal(_positions[id]) * UIScale);
        var scale = ArtScale;
        var selected = id == _selected;
        var target = _marketState == null && _available.Contains(id);
        var marks = PlanetHalf(id);
        var moving = !_configuration.GetCVar(CCVars.ReducedMotion);
        var now = (float) _timing.RealTime.TotalSeconds;
        if (moving && _marketState == null && id == _current)
        {
            // One ring leaves the world the player is on, every few seconds.
            var age = now % RingPeriodSeconds;
            if (age < RingSeconds)
            {
                var share = age / RingSeconds;
                handle.DrawCircle(point, marks + RingGrowth * scale * share, RingColor.WithAlpha(.5f * (1 - share)), false);
            }
        }

        if (selected)
        {
            var breath = moving ? .75f + .25f * MathF.Sin(now * MathF.Tau / BreathSeconds) : 1;
            DrawCornerMarks(handle, point, marks + scale, Color.White.WithAlpha(breath), UIScale);
        }
        else if (target)
            DrawCornerMarks(handle, point, marks, Reachable, UIScale);
        else if (id == _hovered)
            DrawCornerMarks(handle, point, marks, SWGlobalWarConsoleStyle.TextBright, UIScale);

        if (_split.TryGetValue(id, out var surface))
            DrawSplitRing(handle, point, marks + 3 * scale, SWGlobalWarConsoleStyle.SideText(surface), scale);

        var owner = _owners.GetValueOrDefault(id);
        if (owner != _planets[id].InitialOwner && owner != SWGlobalWarFaction.Neutral)
        {
            // A world that changed hands carries its new owner's sign, beside it and not over it.
            var icon = FactionIcon(owner);
            var size = new Vector2(icon.Width, icon.Height) * scale;
            var corner = point + new Vector2(marks + scale);
            handle.DrawTextureRect(icon, new UIBox2(corner - size, corner));
        }
    }

    /// <summary>
    /// A ring of slanted hatches in the colour of the side that holds the surface, around a world whose station belongs to
    /// the other one. Hatches and not a solid ring, so that it never reads as the corners of a selection.
    /// </summary>
    private void DrawSplitRing(DrawingHandleScreen handle, Vector2 center, float radius, Color color, int scale)
    {
        const int hatches = 20;
        var length = 4f * scale;
        for (var i = 0; i < hatches; i++)
        {
            var angle = i * MathF.Tau / hatches;
            var at = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var slant = new Vector2(length / 2, -length / 2);
            Stroke(handle, at - slant, at + slant, color, MathF.Max(1, scale) / UIScale);
        }
    }

    /// <summary>Four small corners around a point. A frame this light says "this one" and leaves the sprite visible.</summary>
    public static void DrawCornerMarks(DrawingHandleScreen handle, Vector2 center, float half, Color color, float uiScale)
    {
        var arm = MathF.Max(5, half / 3);
        var weight = MathF.Max(2, MathF.Round(2 * uiScale));
        for (var sx = -1; sx <= 1; sx += 2)
        for (var sy = -1; sy <= 1; sy += 2)
        {
            var corner = center + new Vector2(sx * half, sy * half);
            handle.DrawRect(Box(corner, corner - new Vector2(sx * arm, sy * weight)), color);
            handle.DrawRect(Box(corner, corner - new Vector2(sx * weight, sy * arm)), color);
        }
    }

    private static UIBox2 Box(Vector2 a, Vector2 b) =>
        new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));

    private static Vector2 Snap(Vector2 point) => new(MathF.Round(point.X), MathF.Round(point.Y));

    private void DrawLabel(DrawingHandleScreen handle, ProtoId<SWGlobalWarPlanetPrototype> id)
    {
        var point = Snap(MapToLocal(_positions[id]) * UIScale);
        var marks = PlanetHalf(id);
        var selected = id == _selected;
        var current = _marketState == null && id == _current;
        var target = _marketState == null && _available.Contains(id);
        var strong = PlanetDiameter(id) >= PlanetDiameterLarge || current || target;
        var font = strong ? SWGlobalWarConsoleStyle.Strong : SWGlobalWarConsoleStyle.Small;
        var color = selected || current ? Color.White : target ? Reachable
            : strong ? SWGlobalWarConsoleStyle.TextBright : SWTerminalStyle.TextBody;

        var y = point.Y + marks + ArtScale + UIScale;
        foreach (var line in NameLines(handle, font, _planets[id].Name, Width * .105f * UIScale))
            y = DrawPlated(handle, font, line, point.X, y, color);

        // A row of zeroes is noise: the metric shows when it says something, or when the planet is in focus.
        var metric = MarketPlanetColor(id);
        if (_marketLabels.TryGetValue(id, out var value) && (metric != null && metric != SWGlobalWarConsoleStyle.TextDim || selected || id == _hovered))
            DrawPlated(handle, SWGlobalWarConsoleStyle.SmallStrong, value, point.X, y, Freight);

        if (current && _marketState == null)
            DrawHereTag(handle, point, marks);
    }

    private void DrawHereTag(DrawingHandleScreen handle, Vector2 point, float marks)
    {
        var text = Loc.GetString("sw-global-war-travel-here-tag");
        var font = SWGlobalWarConsoleStyle.SmallStrong;
        var size = handle.GetDimensions(font, text, UIScale);
        var accent = SWTerminalStyle.AccentOf(SWGlobalWarConsoleStyle.LocalFaction());
        var (fill, _, border) = SWTerminalStyle.AccentFills(accent);
        var height = 19 * UIScale;
        var top = point.Y - marks - 23 * UIScale;
        var half = size.X / 2 + 6 * UIScale;
        var box = new UIBox2(point.X - half, top, point.X + half, top + height);
        handle.DrawRect(box, fill);
        handle.DrawRect(box, border, false);
        handle.DrawString(font, new Vector2(point.X - size.X / 2, top + (height - size.Y) / 2), text, UIScale, Color.White);
    }

    private float DrawPlated(DrawingHandleScreen handle, Font font, string text, float x, float y, Color color)
    {
        var size = handle.GetDimensions(font, text, UIScale);
        var left = x - size.X / 2;

        // The engine has no text outline, so a dark plate keeps a name readable over any nebula or lane.
        handle.DrawRect(new UIBox2(left - 3 * UIScale, y, left + size.X + 3 * UIScale, y + size.Y), Plate);
        handle.DrawString(font, new Vector2(left, y), text, UIScale, color);
        return y + size.Y;
    }

    /// <summary>The names of the sides stand over the space, so that the colours are never alone. They do not follow the camera.</summary>
    private void DrawZoneTitles(DrawingHandleScreen handle)
    {
        if (!_planets.Values.Any(planet => planet.InitialOwner == SWGlobalWarFaction.Republic))
            return;

        var font = SWGlobalWarConsoleStyle.SmallStrong;
        var scale = SWGlobalWarConsoleStyle.ArtScale;
        var top = 10 * UIScale;
        var republic = FactionIcon(SWGlobalWarFaction.Republic);
        var empire = FactionIcon(SWGlobalWarFaction.Empire);
        var icon = new Vector2(republic.Width, republic.Height) * scale;
        var left = new Vector2(10 * UIScale, top);
        handle.DrawTextureRect(republic, new UIBox2(left, left + icon));
        var title = Loc.GetString("sw-global-war-travel-zone-republic");
        var size = handle.GetDimensions(font, title, UIScale);
        handle.DrawString(font, new Vector2(left.X + icon.X + 4 * UIScale, top + (icon.Y - size.Y) / 2), title, UIScale, RepublicText);

        title = Loc.GetString("sw-global-war-travel-zone-neutral");
        size = handle.GetDimensions(font, title, UIScale);
        handle.DrawString(font, new Vector2((PixelWidth - size.X) / 2, top + (icon.Y - size.Y) / 2), title, UIScale, SWGlobalWarConsoleStyle.TextDim);

        var right = new Vector2(PixelWidth - 10 * UIScale - icon.X, top);
        handle.DrawTextureRect(empire, new UIBox2(right, right + icon));
        title = Loc.GetString("sw-global-war-travel-zone-empire");
        size = handle.GetDimensions(font, title, UIScale);
        handle.DrawString(font, new Vector2(right.X - 4 * UIScale - size.X, top + (icon.Y - size.Y) / 2), title, UIScale, EmpireText);
    }

    /// <summary>A long name breaks at its space, so that neighbours keep their distance.</summary>
    private string[] NameLines(DrawingHandleScreen handle, Font font, string name, float width)
    {
        if (handle.GetDimensions(font, name, UIScale).X <= width)
            return [name];

        var space = name.IndexOf(' ');
        return space < 0 ? [name] : [name[..space], name[(space + 1)..]];
    }

    private void DrawDirection(DrawingHandleScreen handle, Route route, Color color)
    {
        var index = route.Points.Length / 2;
        var point = MapToLocal(route.Points[index]) * UIScale;
        var delta = MapToLocal(route.Points[index + 1]) - MapToLocal(route.Points[index - 1]);
        if (delta.LengthSquared() < .001f)
            return;
        var direction = Vector2.Normalize(delta);
        var normal = new Vector2(-direction.Y, direction.X);
        var tip = point + direction * 5 * UIScale;
        Stroke(handle, tip - direction * 9 * UIScale + normal * 4 * UIScale, tip, color, 1.8f);
        Stroke(handle, tip - direction * 9 * UIScale - normal * 4 * UIScale, tip, color, 1.8f);
    }

    private void Stroke(DrawingHandleScreen handle, Vector2 a, Vector2 b, Color color, float width)
    {
        var delta = b - a;
        if (delta.LengthSquared() < .0001f)
            return;
        var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * width * UIScale / 2;
        _strokeVertices[0] = a - normal;
        _strokeVertices[1] = a + normal;
        _strokeVertices[2] = b + normal;
        _strokeVertices[3] = b - normal;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _strokeVertices, Color.ToSrgb(color));
    }
}
