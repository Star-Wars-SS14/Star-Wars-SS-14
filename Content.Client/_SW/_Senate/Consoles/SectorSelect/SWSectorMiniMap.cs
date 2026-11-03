using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Consoles.SectorSelect;

/// <summary>Public galaxy projection with a schematic shared political border and no economic data.</summary>
public sealed class SWSectorMiniMap : Control
{
    private readonly SWGalacticMapPrototype? _map;
    private readonly IConfigurationManager _configuration;
    private readonly Dictionary<string, SWGalacticMapWorld> _worlds = new();
    private readonly List<(Vector2 A, Vector2 B)> _boundary = new();
    private float _motion;
    private SWGalacticMapWorld[] _selected = Array.Empty<SWGalacticMapWorld>();
    private string? _sector;
    private float _intro;
    private float _marker;
    private float _angle;
    private readonly Vector2[] _disk = new Vector2[65];
    public string Caption { get; private set; } = "Положение сектора неизвестно";

    public SWSectorMiniMap()
    {
        MinHeight = 100;
        HorizontalAlignment = HAlignment.Stretch;
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
        _configuration = IoCManager.Resolve<IConfigurationManager>();
        IoCManager.Resolve<IPrototypeManager>().TryIndex<SWGalacticMapPrototype>("SenateRepublic", out _map);
        if (_map != null)
        {
            foreach (var world in _map.Worlds.Where(Valid)) _worlds.TryAdd(world.Id, world);
            // Only edges literally shared by the two non-contested public political polygons.
            var empire = _map.PoliticalAreas.Where(a => a.Faction == "empire" && !a.Contested).ToArray();
            foreach (var area in _map.PoliticalAreas.Where(a => a.Faction == "republic" && !a.Contested))
            for (var i = 0; i < area.Boundary.Count; i++)
            {
                var a = area.Boundary[i]; var b = area.Boundary[(i + 1) % area.Boundary.Count];
                if (empire.Any(other => Enumerable.Range(0, other.Boundary.Count).Any(j =>
                    (other.Boundary[j] == a && other.Boundary[(j + 1) % other.Boundary.Count] == b) ||
                    (other.Boundary[j] == b && other.Boundary[(j + 1) % other.Boundary.Count] == a))))
                    _boundary.Add((a, b));
            }
        }
    }

    public bool HasBoundary => _boundary.Count > 0;

    private bool Valid(SWGalacticMapWorld w) => _map != null && float.IsFinite(w.Position.X) && float.IsFinite(w.Position.Y)
        && w.Position.X >= 0 && w.Position.Y >= 0 && w.Position.X <= _map.Size.X && w.Position.Y <= _map.Size.Y;

    public void Select(string? sector)
    {
        if (_sector == sector) return;
        _sector = sector;
        _marker = 0;
        var matches = _worlds.Values.Where(w => sector != null && w.Sector?.Id == sector).ToArray();
        _selected = matches;
        var anchors = matches.Where(w => w.Anchor).ToArray();
        Caption = matches.Length == 0 ? "Положение сектора неизвестно" : anchors.Length == 1
            ? $"Ориентир представительства: {anchors[0].Name}" : "Связанные миры представительства";
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree) return;
        if (_configuration.GetCVar(CCVars.ReducedMotion))
        {
            _intro = _marker = 1;
            return;
        }
        var delta = Math.Max(0, args.DeltaSeconds);
        _intro = Math.Min(1, _intro + delta / 0.75f);
        if (_intro >= 1) _marker = Math.Min(1, _marker + delta / 0.35f);
        _angle = (_angle + delta * (2 * MathF.PI / 105f)) % (2 * MathF.PI);
        _motion = (_motion + delta) % (10 * MathF.PI);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_map == null || _map.Size.X <= 0 || _map.Size.Y <= 0 || PixelWidth <= 0 || PixelHeight <= 0) return;
        var center = PixelSize / 2;
        var radiusX = Math.Max(0, MathF.Min(PixelWidth / 2 - 3 * UIScale, (PixelHeight / 2 - 3 * UIScale) / .43f));
        var radius = new Vector2(radiusX, radiusX * .43f);
        Vector2 ProjectDisk(Vector2 p) => center + p * radius;
        Vector2 Galaxy(Vector2 p)
        {
            var n = Vector2.Clamp(p / _map.Size * 2 - Vector2.One, -Vector2.One, Vector2.One);
            // Continuous mapping of the rectangular global extent onto the disk; all geometry uses it.
            var disk = new Vector2(n.X * MathF.Sqrt(1 - n.Y * n.Y / 2), n.Y * MathF.Sqrt(1 - n.X * n.X / 2));
            var c = MathF.Cos(_angle); var s = MathF.Sin(_angle);
            return new Vector2(disk.X * c - disk.Y * s, disk.X * s + disk.Y * c);
        }
        for (var i = 0; i < _disk.Length; i++)
        {
            var a = 2 * MathF.PI * i / (_disk.Length - 1);
            _disk[i] = ProjectDisk(new Vector2(MathF.Cos(a), MathF.Sin(a)));
        }
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _disk, Color.ToSrgb(Color.FromHex("#10252B")));
        var grid = Color.FromHex("#284A60");
        var reduced = _configuration.GetCVar(CCVars.ReducedMotion);
        const int rings = 6;
        for (var ring = 1; ring <= rings; ring++)
        {
            var fraction = ring / (float) rings;
            if (_intro < fraction) continue;
            var wave = reduced || _intro < 1 ? 0 : (MathF.Sin(_motion * 1.4f - ring * .8f) + 1) * .5f;
            var ringColor = new Color(.13f + .08f * wave, .25f + .11f * wave, .32f + .13f * wave, 1f);
            Vector2? previous = null;
            for (var i = 0; i <= 64; i++)
            {
                var a = 2 * MathF.PI * i / 64;
                var point = ProjectDisk(new Vector2(MathF.Cos(a), MathF.Sin(a)) * fraction);
                if (previous != null) handle.DrawLine(previous.Value, point, ringColor);
                previous = point;
            }
        }
        for (var i = 0; i < 12; i++)
        {
            var a = 2 * MathF.PI * i / 12 + _angle;
            handle.DrawLine(center, ProjectDisk(new Vector2(MathF.Cos(a), MathF.Sin(a)) * _intro), grid);
        }
        foreach (var route in _map.Routes)
        {
            if (route.Local || route.Proposed || !_worlds.TryGetValue(route.From, out var a) || !_worlds.TryGetValue(route.To, out var b)) continue;
            var control = route.Control ?? (a.Position + b.Position) / 2;
            var previous = Galaxy(a.Position);
            for (var i = 1; i <= 16; i++)
            {
                var t = i / 16f;
                var next = Galaxy((1 - t) * (1 - t) * a.Position + 2 * (1 - t) * t * control + t * t * b.Position);
                if (previous.Length() <= _intro && next.Length() <= _intro)
                    handle.DrawLine(ProjectDisk(previous), ProjectDisk(next), Color.FromHex("#55797A"));
                previous = next;
            }
        }
        foreach (var (start, end) in _boundary)
        {
            var previous = Galaxy(start);
            for (var i = 1; i <= 8; i++)
            {
                var next = Galaxy(Vector2.Lerp(start, end, i / 8f));
                if (previous.Length() <= _intro && next.Length() <= _intro)
                    handle.DrawLine(ProjectDisk(previous), ProjectDisk(next), Color.FromHex("#99A9B6"));
                previous = next;
            }
        }
        foreach (var world in _worlds.Values.Where(w => w.Overview))
        {
            var p = Galaxy(world.Position);
            if (p.Length() <= _intro) handle.DrawCircle(ProjectDisk(p), 1.5f * UIScale, Color.ToSrgb(Color.FromHex("#87A8A7")));
        }
        if (_intro < 1) return;
        var ink = new Color(0.23f + 0.60f * _marker, 0.22f + 0.49f * _marker, 0.16f + 0.33f * _marker, 1f);
        var uniqueAnchor = _selected.Count(w => w.Anchor) == 1;
        var pulse = reduced ? 0 : (MathF.Sin(_motion * 2f) + 1) * .5f;
        foreach (var world in _selected)
        {
            var p = ProjectDisk(Galaxy(world.Position));
            handle.DrawCircle(p, ((uniqueAnchor && world.Anchor ? 6 : 4) + pulse * 1.5f) * UIScale, Color.ToSrgb(ink), false);
            handle.DrawCircle(p, 2 * UIScale, Color.ToSrgb(ink));
        }
    }
}

/// <summary>Thin cool metal rim around the terminal.</summary>
public sealed class SWSectorFrame : StyleBox
{
    protected override float GetDefaultContentMargin(Margin margin) => 0;
    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        handle.DrawRect(box, Color.FromHex("#1A2B3B"));
        handle.DrawRect(box, Color.FromHex("#66889D"), false);
        var inset = 4 * uiScale;
        if (box.Width > inset * 2 && box.Height > inset * 2)
            handle.DrawRect(new UIBox2(box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset), Color.FromHex("#364D5E"), false);
    }
}

/// <summary>Inset terminal screen with a fine steel border.</summary>
public sealed class SWSectorInset : StyleBox
{
    public Color Background { get; init; }
    protected override float GetDefaultContentMargin(Margin margin) => 0;
    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        handle.DrawRect(box, Background);
        handle.DrawRect(box, Color.FromHex("#344D60"), false);
    }
}
