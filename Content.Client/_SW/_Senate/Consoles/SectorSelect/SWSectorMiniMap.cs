using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Consoles.SectorSelect;

/// <summary>Read-only public galaxy geography. Coordinates stay in the global projection.</summary>
public sealed class SWSectorMiniMap : Control
{
    private readonly SWGalacticMapPrototype? _map;
    private readonly Dictionary<string, SWGalacticMapWorld> _worlds = new();
    private SWGalacticMapWorld[] _selected = Array.Empty<SWGalacticMapWorld>();
    private string? _sector;
    private float _reveal = 1;
    public string Caption { get; private set; } = "Положение сектора неизвестно";

    public SWSectorMiniMap()
    {
        SetSize = new Vector2(310, 180);
        MouseFilter = MouseFilterMode.Ignore;
        IoCManager.Resolve<IPrototypeManager>().TryIndex<SWGalacticMapPrototype>("SenateRepublic", out _map);
        if (_map != null)
            foreach (var world in _map.Worlds.Where(Valid))
                _worlds.TryAdd(world.Id, world);
    }

    private bool Valid(SWGalacticMapWorld w) => _map != null && float.IsFinite(w.Position.X) && float.IsFinite(w.Position.Y)
        && w.Position.X >= 0 && w.Position.Y >= 0 && w.Position.X <= _map.Size.X && w.Position.Y <= _map.Size.Y;

    public void Select(string? sector)
    {
        if (_sector == sector) return;
        _sector = sector;
        _reveal = 0;
        var matches = _worlds.Values.Where(w => sector != null && w.Sector?.Id == sector).ToArray();
        _selected = matches;
        var anchors = matches.Where(w => w.Anchor).ToArray();
        Caption = matches.Length == 0 ? "Положение сектора неизвестно" : anchors.Length == 1
            ? $"Ориентир представительства: {anchors[0].Name}" : "Связанные миры представительства";
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _reveal = Math.Min(1, _reveal + args.DeltaSeconds / 0.35f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), Color.FromHex("#0C2028"));
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), SWConsoleStyle.Edge, false);
        if (_map == null || _map.Size.X <= 0 || _map.Size.Y <= 0) return;
        var scale = MathF.Min((PixelWidth - 24 * UIScale) / _map.Size.X, (PixelHeight - 20 * UIScale) / _map.Size.Y);
        var offset = (PixelSize - _map.Size * scale) / 2;
        Vector2 Project(Vector2 p) => offset + p * scale;
        foreach (var route in _map.Routes)
        {
            if (route.Local || route.Proposed || !_worlds.TryGetValue(route.From, out var a) || !_worlds.TryGetValue(route.To, out var b)) continue;
            var control = route.Control ?? (a.Position + b.Position) / 2;
            var previous = a.Position;
            for (var i = 1; i <= 12; i++)
            {
                var t = i / 12f;
                var point = (1 - t) * (1 - t) * a.Position + 2 * (1 - t) * t * control + t * t * b.Position;
                handle.DrawLine(Project(previous), Project(point), Color.FromHex("#29444A"));
                previous = point;
            }
        }
        foreach (var world in _worlds.Values.Where(w => w.Overview))
            handle.DrawCircle(Project(world.Position), 1.5f * UIScale, Color.ToSrgb(Color.FromHex("#68878A")));
        var ink = new Color(0.35f + 0.48f * _reveal, 0.35f + 0.36f * _reveal, 0.30f + 0.19f * _reveal, 1f);
        var uniqueAnchor = _selected.Count(w => w.Anchor) == 1;
        foreach (var world in _selected)
        {
            var p = Project(world.Position);
            handle.DrawCircle(p, (uniqueAnchor && world.Anchor ? 6 : 4) * UIScale, Color.ToSrgb(ink), false);
            handle.DrawCircle(p, 2 * UIScale, Color.ToSrgb(ink));
        }
    }
}


/// <summary>Metal housing for this terminal, with clipped corners and a brass inlay.</summary>
public sealed class SWSectorFrame : StyleBox
{
    private readonly Vector2[] _shape = new Vector2[8];
    protected override float GetDefaultContentMargin(Margin margin) => 0;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var cut = MathF.Min(16 * uiScale, MathF.Min(box.Width, box.Height) / 4);
        _shape[0] = new(box.Left + cut, box.Top);
        _shape[1] = new(box.Right - cut, box.Top);
        _shape[2] = new(box.Right, box.Top + cut);
        _shape[3] = new(box.Right, box.Bottom - cut);
        _shape[4] = new(box.Right - cut, box.Bottom);
        _shape[5] = new(box.Left + cut, box.Bottom);
        _shape[6] = new(box.Left, box.Bottom - cut);
        _shape[7] = new(box.Left, box.Top + cut);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _shape, Color.ToSrgb(Color.FromHex("#24323A")));
        for (var i = 0; i < _shape.Length; i++)
            handle.DrawLine(_shape[i], _shape[(i + 1) % _shape.Length], SWConsoleStyle.Edge);
        handle.DrawLine(new Vector2(box.Left + cut * 2, box.Top + 5 * uiScale),
            new Vector2(box.Right - cut * 2, box.Top + 5 * uiScale), SWConsoleStyle.Brass);
    }
}
