using System.Linq;
using System.Numerics;
using Content.Client._SW.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A band of dim stars behind a window's main figures. It takes the height of what it holds.</summary>
public sealed class SWGlobalWarBand : Control
{
    private static readonly Color Dim = Color.FromHex("#737373");
    private readonly Texture _stars = SWGlobalWarConsoleStyle.Art("capture_scene", "off");

    /// <summary>A scene for this window, at full light. Without one the band shows dim stars.</summary>
    public Texture? Scene;

    public SWGlobalWarBand()
    {
        RectClipContent = true;
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var art = Scene ?? _stars;
        var size = (Vector2) art.Size;
        var cover = MathF.Max(PixelWidth / size.X, PixelHeight / size.Y);
        var part = new Vector2(PixelWidth, PixelHeight) / cover;
        var corner = (size - part) / 2;
        handle.DrawTextureRectRegion(art, PixelSizeBox, UIBox2.FromDimensions(corner, part), Scene == null ? Dim : Color.White);
    }
}

/// <summary>A dark plate for words that lie over a scene: the engine draws no outline around text.</summary>
public sealed class SWGlobalWarPlate : PanelContainer
{
    public SWGlobalWarPlate()
    {
        PanelOverride = SWTerminalStyle.Fill(Color.FromHex("#07090E").WithAlpha(.86f));
    }
}

/// <summary>A bar of coloured parts, each as wide as its share of the whole.</summary>
public sealed class SWGlobalWarShareBar : Control
{
    private (Color Color, double Value)[] _parts = [];

    public SWGlobalWarShareBar()
    {
        MouseFilter = MouseFilterMode.Ignore;
        HorizontalExpand = true;
    }

    public void Set(params (Color Color, double Value)[] parts) => _parts = parts;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, Color.FromHex("#14161B"));
        var total = _parts.Sum(p => p.Value);
        if (total <= 0)
            return;

        var x = 0f;
        foreach (var (color, value) in _parts)
        {
            var width = (float) (PixelWidth * value / total);
            handle.DrawRect(new UIBox2(x, 0, x + width, PixelHeight), color);
            x += width;
        }
    }
}

/// <summary>A small coloured state: good, bad, waiting or off, with an optional icon.</summary>
public sealed class SWGlobalWarChip : PanelContainer
{
    private readonly TextureRect _icon = SWGlobalWarMarketPresentation.SmallIcon("status", "waiting");
    private readonly Label _text = new() { FontOverride = SWGlobalWarConsoleStyle.Strong, FontColorOverride = Color.White };
    private string? _look;

    public SWGlobalWarChip()
    {
        VerticalAlignment = VAlignment.Center;
        var body = new BoxContainer { SeparationOverride = 4, Margin = new Thickness(8, 3) };
        _icon.Visible = false;
        body.AddChild(_icon);
        body.AddChild(_text);
        AddChild(body);
    }

    public void Set(string text, string look, string? statusIcon = null)
    {
        if (_text.Text != text)
            _text.Text = text;
        _icon.Visible = statusIcon != null;
        if (statusIcon != null)
            _icon.Texture = SWGlobalWarConsoleStyle.Art("status", statusIcon);
        if (_look == look)
            return;

        _look = look;
        PanelOverride = look switch
        {
            "good" => SWTerminalStyle.Box(Color.FromHex("#2F7D32"), Color.FromHex("#5CB353"), 1),
            "bad" => SWTerminalStyle.Box(Color.FromHex("#A01A1A"), Color.FromHex("#D04A4A"), 1),
            "wait" => SWTerminalStyle.Box(Color.FromHex("#A85A10"), SWGlobalWarConsoleStyle.Amber, 1),
            _ => SWTerminalStyle.Box(Color.FromHex("#343842"), Color.FromHex("#6A7080"), 1),
        };
    }
}

/// <summary>One figure with its caption under it, on a dark strip.</summary>
public sealed class SWGlobalWarStatTile : PanelContainer
{
    public readonly Label Value;
    public readonly TextureRect? Icon;

    public SWGlobalWarStatTile(string caption, int size, string? metricIcon = null)
    {
        HorizontalExpand = true;
        MinHeight = 44;
        PanelOverride = SWGlobalWarConsoleStyle.RowBox(false);
        Value = new Label { FontOverride = SWGlobalWarConsoleStyle.Bold(size), FontColorOverride = Color.White, ClipText = true };
        var line = new BoxContainer { SeparationOverride = 3 };
        if (metricIcon != null)
        {
            Icon = SWGlobalWarMarketPresentation.SmallIcon("metrics", metricIcon);
            line.AddChild(Icon);
        }

        line.AddChild(new Label
        {
            Text = caption,
            ClipText = true,
            HorizontalExpand = true,
            FontOverride = SWGlobalWarConsoleStyle.Small,
            FontColorOverride = SWGlobalWarConsoleStyle.TextDim,
        });
        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8, 4, 0, 4),
            SeparationOverride = 2,
            VerticalAlignment = VAlignment.Center,
        };
        body.AddChild(Value);
        body.AddChild(line);
        AddChild(body);
    }
}
