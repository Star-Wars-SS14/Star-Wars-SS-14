using System.Numerics;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Holonet;

public static class SWHolonetPalette
{
    public static readonly Color Background = Color.FromHex("#07161D");
    public static readonly Color Panel = Color.FromHex("#0C2129");
    public static readonly Color Band = Color.FromHex("#14333A");
    public static readonly Color Selected = Color.FromHex("#1D4A50");
    public static readonly Color Mint = Color.FromHex("#7DD9C8");
    public static readonly Color Gold = Color.FromHex("#D4AD63");
    public static readonly Color Bright = Color.FromHex("#F2F6F5");
    public static readonly Color Dim = Color.FromHex("#A9BABB");
    public static readonly Color Edge = Color.FromHex("#27444C");
    public static readonly Color Warn = Color.FromHex("#E2B9A0");
}

/// <summary>
/// Approved illustration, drawn as the actual PNG. A missing or unreadable file hides the control instead of
/// showing a substitute. It never accepts the mouse, so it can not act as a link or a false notification.
/// </summary>
public sealed class SWHolonetArt : Control
{
    private const string Root = "/Textures/_SW/Interface/Holonet/";

    private readonly TextureRect _image;
    private readonly IResourceCache _cache = IoCManager.Resolve<IResourceCache>();
    private readonly float _aspect;

    /// <param name="file">File name inside the Holonet texture directory.</param>
    /// <param name="aspect">Width to height ratio that follows the available width; zero uses the set size.</param>
    public SWHolonetArt(string file, float aspect = 0f)
    {
        _aspect = aspect;
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
        _image = new TextureRect
        {
            MouseFilter = MouseFilterMode.Ignore,
            Stretch = TextureRect.StretchMode.KeepAspectCovered,
            // Without this the texture would demand its full 960 or 400 pixels and widen the column.
            CanShrink = true,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        AddChild(_image);
        SetFile(file);
    }

    public bool Loaded => _image.Texture != null;

    public void SetFile(string file)
    {
        var path = new ResPath(Root + file);
        // Checking first keeps an absent asset from logging a resource error on every screen.
        _image.Texture = _cache.ContentFileExists(path) && _cache.TryGetResource<TextureResource>(path, out var resource)
            ? resource.Texture
            : null;
        Visible = Loaded;
    }

    protected override void Resized()
    {
        base.Resized();
        if (_aspect > 0f)
            InvalidateMeasure();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (_aspect <= 0f)
            return base.MeasureOverride(availableSize);

        // The column layout supplies the image's allocated width even on the first pass.
        var width = float.IsFinite(availableSize.X) ? Math.Max(0f, availableSize.X) : Width > 1f ? Width : 320f;
        var height = width / _aspect;
        _image.Measure(new Vector2(width, height));
        return new Vector2(0f, height);
    }
}

/// <summary>
/// Measures fixed and flexible columns using their own allocated widths. BoxContainer measures children
/// in order, so a flexible middle column can otherwise starve a fixed trailing column during measure.
/// </summary>
public sealed class SWHolonetColumns : Control
{
    private const float Gap = 12f;
    private readonly List<(Control Control, float FixedWidth)> _columns = new();

    /// <param name="fixedWidth">Zero gives this column an equal share of the remaining width.</param>
    public void AddColumn(Control child, float fixedWidth = 0f)
    {
        _columns.Add((child, fixedWidth));
        AddChild(child);
    }

    private float[] Widths(float width)
    {
        var sizes = new float[_columns.Count];
        var available = Math.Max(0f, width - Gap * Math.Max(0, _columns.Count - 1));
        var fixedTotal = 0f;
        var flexible = 0;
        foreach (var (_, fixedWidth) in _columns)
            if (fixedWidth > 0f)
                fixedTotal += fixedWidth;
            else
                flexible++;

        // On an unexpectedly narrow arrange, shrink the fixed columns as well; no child has a
        // hard MinWidth that could force it outside the slot supplied here.
        var scale = fixedTotal > available && fixedTotal > 0f ? available / fixedTotal : 1f;
        var remaining = Math.Max(0f, available - fixedTotal * scale);
        for (var i = 0; i < _columns.Count; i++)
            sizes[i] = _columns[i].FixedWidth > 0f
                ? _columns[i].FixedWidth * scale
                : flexible > 0 ? remaining / flexible : 0f;
        return sizes;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = float.IsFinite(availableSize.X) ? Math.Max(0f, availableSize.X) : 880f;
        var sizes = Widths(width);
        var height = 0f;
        for (var i = 0; i < _columns.Count; i++)
        {
            var child = _columns[i].Control;
            child.Measure(new Vector2(sizes[i], availableSize.Y));
            height = Math.Max(height, child.DesiredSize.Y);
        }
        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var sizes = Widths(finalSize.X);
        var x = 0f;
        for (var i = 0; i < _columns.Count; i++)
        {
            var child = _columns[i].Control;
            // A scrollbar or resize may have changed the actual width since the measure pass.
            child.Measure(new Vector2(sizes[i], float.PositiveInfinity));
            child.Arrange(UIBox2.FromDimensions(new Vector2(x, 0f), new Vector2(sizes[i], finalSize.Y)));
            x += sizes[i] + Gap;
        }
        return finalSize;
    }
}

/// <summary>Non-clickable neutral advert. Reduced Motion keeps one static frame.</summary>
public sealed class SWHolonetAdvert : BoxContainer
{
    private const float FrameSeconds = 14f;
    private const float FadeSeconds = 0.6f;
    private static readonly (string File, string Caption)[] Frames =
    {
        ("ad-carrier.png", "sw-holonet-ad-carrier"),
        ("ad-shipyard.png", "sw-holonet-ad-shipyard"),
        ("ad-drink.png", "sw-holonet-ad-drink"),
        ("ad-alderaan.png", "sw-holonet-ad-alderaan"),
    };

    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private readonly SWHolonetArt _art;
    private readonly RichTextLabel _caption;
    private float _time;
    private int _frame;

    public SWHolonetAdvert()
    {
        Orientation = LayoutOrientation.Horizontal;
        MouseFilter = MouseFilterMode.Ignore;
        SeparationOverride = 12;
        var text = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        text.AddChild(SWConsoleStyle.Text(Loc.GetString("sw-holonet-ad-label"), SWHolonetPalette.Dim, SWConsoleStyle.Small));
        _caption = SWConsoleStyle.Wrap(Loc.GetString(Frames[0].Caption), SWHolonetPalette.Bright, 160f);
        text.AddChild(_caption);
        // 5:3 of the 4:3 source stays inside the central 80% safe area listed in the asset manifest.
        _art = new SWHolonetArt(Frames[0].File) { SetSize = new Vector2(120, 72) };
        var columns = new SWHolonetColumns { HorizontalExpand = true };
        columns.AddColumn(text);
        columns.AddColumn(_art, 120f);
        AddChild(columns);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree)
            return;

        if (_configuration.GetCVar(CCVars.ReducedMotion))
        {
            _art.Modulate = Color.White;
            return;
        }

        _time += args.DeltaSeconds;
        if (_time >= FrameSeconds)
        {
            _time = 0f;
            _frame = (_frame + 1) % Frames.Length;
            _art.SetFile(Frames[_frame].File);
            var message = new FormattedMessage();
            message.PushColor(SWHolonetPalette.Bright);
            message.AddText(Loc.GetString(Frames[_frame].Caption));
            message.Pop();
            _caption.SetMessage(message);
        }

        // Gentle fade in at the frame start and out at its end.
        var alpha = MathF.Min(1f, MathF.Min(_time, FrameSeconds - _time) / FadeSeconds);
        _art.Modulate = Color.White.WithAlpha(alpha);
    }
}

/// <summary>Broadcast strip: rotates real headlines, pausing while hovered. Reduced Motion disables rotation.</summary>
public sealed class SWHolonetTicker : PanelContainer
{
    private const float ItemSeconds = 8f;
    private const float FadeSeconds = 0.4f;
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private readonly Label _label;
    private readonly List<string> _items = new();
    private float _time;
    private int _index;
    private bool _hovered;

    public SWHolonetTicker()
    {
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = SWHolonetPalette.Band,
            ContentMarginLeftOverride = 16,
            ContentMarginRightOverride = 16,
            ContentMarginTopOverride = 7,
            ContentMarginBottomOverride = 7,
        };
        MouseFilter = MouseFilterMode.Stop;
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 24 };
        row.AddChild(SWConsoleStyle.Text(Loc.GetString("sw-holonet-ticker-label"), SWHolonetPalette.Mint, SWConsoleStyle.Strong));
        _label = SWConsoleStyle.Text(string.Empty, SWHolonetPalette.Bright, SWConsoleStyle.Strong);
        _label.ClipText = true;
        _label.HorizontalExpand = true;
        row.AddChild(_label);
        AddChild(row);
    }

    public void SetItems(IReadOnlyList<string> items)
    {
        _items.Clear();
        _items.AddRange(items);
        if (_items.Count == 0)
            _items.Add(Loc.GetString("sw-holonet-ticker-empty"));
        if (_index >= _items.Count)
            _index = 0;
        _label.Text = _items[_index];
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        _hovered = true;
        _label.Modulate = Color.White;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hovered = false;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree || _items.Count == 0)
            return;

        if (_configuration.GetCVar(CCVars.ReducedMotion))
        {
            _label.Modulate = Color.White;
            if (_index != 0)
            {
                _index = 0;
                _label.Text = _items[0];
            }

            return;
        }

        if (_hovered || _items.Count == 1)
            return;

        _time += args.DeltaSeconds;
        if (_time >= ItemSeconds)
        {
            _time = 0f;
            _index = (_index + 1) % _items.Count;
            _label.Text = _items[_index];
        }

        var alpha = MathF.Min(1f, MathF.Min(_time, ItemSeconds - _time) / FadeSeconds);
        _label.Modulate = Color.White.WithAlpha(alpha);
    }
}
