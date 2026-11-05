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

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        return _aspect > 0f && float.IsFinite(availableSize.X)
            ? new Vector2(availableSize.X, availableSize.X / _aspect)
            : size;
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
    private readonly Label _caption;
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
        _caption = SWConsoleStyle.Text(Loc.GetString(Frames[0].Caption), SWHolonetPalette.Bright, SWConsoleStyle.Strong);
        text.AddChild(_caption);
        AddChild(text);
        // 5:3 of the 4:3 source stays inside the central 80% safe area listed in the asset manifest.
        _art = new SWHolonetArt(Frames[0].File) { SetSize = new Vector2(120, 72) };
        AddChild(_art);
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
            _caption.Text = Loc.GetString(Frames[_frame].Caption);
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
