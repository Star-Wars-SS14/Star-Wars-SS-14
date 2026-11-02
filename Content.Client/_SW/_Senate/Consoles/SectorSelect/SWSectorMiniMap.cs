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

/// <summary>Public galaxy in a fixed global projection, without political or economic overlays.</summary>
public sealed class SWSectorMiniMap : Control
{
    private readonly SWGalacticMapPrototype? _map;
    private readonly IConfigurationManager _configuration;
    private readonly Dictionary<string, SWGalacticMapWorld> _worlds = new();
    private SWGalacticMapWorld[] _selected = Array.Empty<SWGalacticMapWorld>();
    private string? _sector;
    private float _intro;
    private float _marker;
    private float _angle;
    private readonly Vector2[] _disk = new Vector2[65];
    public string Caption { get; private set; } = "Положение сектора неизвестно";

    public SWSectorMiniMap()
    {
        MaxWidth = 390;
        HorizontalAlignment = HAlignment.Center;
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
        _configuration = IoCManager.Resolve<IConfigurationManager>();
        IoCManager.Resolve<IPrototypeManager>().TryIndex<SWGalacticMapPrototype>("SenateRepublic", out _map);
        if (_map != null)
            foreach (var world in _map.Worlds.Where(Valid)) _worlds.TryAdd(world.Id, world);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = float.IsFinite(availableSize.X) ? Math.Clamp(availableSize.X, 0, 390) : 390;
        return new Vector2(width, width * 0.58f);
    }

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
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_map == null || _map.Size.X <= 0 || _map.Size.Y <= 0 || PixelWidth <= 0 || PixelHeight <= 0) return;
        var center = PixelSize / 2;
        var radius = new Vector2(Math.Max(0, PixelWidth / 2 - 3 * UIScale), Math.Max(0, PixelHeight / 2 - 3 * UIScale));
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
        var grid = Color.FromHex("#35585B");
        const int rings = 6;
        for (var ring = 1; ring <= rings; ring++)
        {
            var fraction = ring / (float) rings;
            if (_intro < fraction) continue;
            Vector2? previous = null;
            for (var i = 0; i <= 64; i++)
            {
                var a = 2 * MathF.PI * i / 64;
                var point = ProjectDisk(new Vector2(MathF.Cos(a), MathF.Sin(a)) * fraction);
                if (previous != null) handle.DrawLine(previous.Value, point, grid);
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
        foreach (var world in _worlds.Values.Where(w => w.Overview))
        {
            var p = Galaxy(world.Position);
            if (p.Length() <= _intro) handle.DrawCircle(ProjectDisk(p), 1.5f * UIScale, Color.ToSrgb(Color.FromHex("#87A8A7")));
        }
        if (_intro < 1) return;
        var ink = new Color(0.23f + 0.60f * _marker, 0.22f + 0.49f * _marker, 0.16f + 0.33f * _marker, 1f);
        var uniqueAnchor = _selected.Count(w => w.Anchor) == 1;
        foreach (var world in _selected)
        {
            var p = ProjectDisk(Galaxy(world.Position));
            handle.DrawCircle(p, (uniqueAnchor && world.Anchor ? 6 : 4) * UIScale, Color.ToSrgb(ink), false);
            handle.DrawCircle(p, 2 * UIScale, Color.ToSrgb(ink));
        }
    }
}

/// <summary>Metal housing with a second rim and recessed brass inlays.</summary>
public sealed class SWSectorFrame : StyleBox
{
    private readonly Vector2[] _shape = new Vector2[8];
    protected override float GetDefaultContentMargin(Margin margin) => 0;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var cut = MathF.Min(16 * uiScale, MathF.Min(box.Width, box.Height) / 4);
        var inset = MathF.Min(5 * uiScale, cut / 3);
        void Shape(float left, float top, float right, float bottom, float corner)
        {
            _shape[0] = new(left + corner, top); _shape[1] = new(right - corner, top);
            _shape[2] = new(right, top + corner); _shape[3] = new(right, bottom - corner);
            _shape[4] = new(right - corner, bottom); _shape[5] = new(left + corner, bottom);
            _shape[6] = new(left, bottom - corner); _shape[7] = new(left, top + corner);
        }
        Shape(box.Left, box.Top, box.Right, box.Bottom, cut);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _shape, Color.ToSrgb(Color.FromHex("#24323A")));
        for (var i = 0; i < _shape.Length; i++) handle.DrawLine(_shape[i], _shape[(i + 1) % _shape.Length], SWConsoleStyle.Edge);
        Shape(box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset, Math.Max(0, cut - inset));
        for (var i = 0; i < _shape.Length; i++) handle.DrawLine(_shape[i], _shape[(i + 1) % _shape.Length], Color.FromHex("#756C55"));
        var y = box.Top + inset + 4 * uiScale;
        var left = box.Left + cut * 2; var right = box.Right - cut * 2;
        var midLeft = left + (right - left) * 0.31f; var midRight = left + (right - left) * 0.69f;
        var depth = 6 * uiScale;
        handle.DrawLine(new Vector2(left, y), new Vector2(midLeft, y), SWConsoleStyle.Brass);
        handle.DrawLine(new Vector2(midLeft, y), new Vector2(midLeft + depth, y + depth), SWConsoleStyle.Brass);
        handle.DrawLine(new Vector2(midLeft + depth, y + depth), new Vector2(midRight - depth, y + depth), SWConsoleStyle.Brass);
        handle.DrawLine(new Vector2(midRight - depth, y + depth), new Vector2(midRight, y), SWConsoleStyle.Brass);
        handle.DrawLine(new Vector2(midRight, y), new Vector2(right, y), SWConsoleStyle.Brass);
        var bottom = box.Bottom - inset - 4 * uiScale;
        handle.DrawLine(new Vector2(box.Left + box.Width * .40f, bottom), new Vector2(box.Right - box.Width * .40f, bottom), SWConsoleStyle.Edge);
        handle.DrawLine(new Vector2(box.Left + box.Width * .44f, bottom), new Vector2(box.Right - box.Width * .44f, bottom), SWConsoleStyle.Brass);
    }
}

/// <summary>Inset display with the catalogue's clipped upper-right corner.</summary>
public sealed class SWSectorInset : StyleBox
{
    private readonly Vector2[] _shape = new Vector2[5];
    public Color Background { get; init; }
    protected override float GetDefaultContentMargin(Margin margin) => 0;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var cut = MathF.Min(12 * uiScale, MathF.Min(box.Width, box.Height) / 4);
        _shape[0] = new(box.Left, box.Top); _shape[1] = new(box.Right - cut, box.Top);
        _shape[2] = new(box.Right, box.Top + cut); _shape[3] = new(box.Right, box.Bottom);
        _shape[4] = new(box.Left, box.Bottom);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _shape, Color.ToSrgb(Background));
        for (var i = 0; i < _shape.Length; i++) handle.DrawLine(_shape[i], _shape[(i + 1) % _shape.Length], SWConsoleStyle.Edge);
    }
}
