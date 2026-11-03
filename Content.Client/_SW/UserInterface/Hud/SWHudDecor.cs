using System.Numerics;
using Content.Client._SW.GlobalWar.UI;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Client.UserInterface.Systems.Actions.Widgets;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Client.UserInterface.Systems.Inventory.Widgets;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.Client._SW.UserInterface.Hud;

/// <summary>
/// Metal behind the HUD's widgets, under them and over the world. It follows the widgets and never takes a click, so
/// the widgets themselves stay exactly as the game made them. The style is read every frame: a change in the options
/// shows at once. Plates follow the mocks "17 HUD: пульт" and "18 HUD: лёгкий пульт", measured by the pixel; both looks
/// take the console's dashed line inside the plate, as the owner chose for the light look too.
/// </summary>
public sealed class SWHudDecor : Control
{
    private const string Path = "/Textures/_SW/Interface/Terminal/Hud/";
    private const float ReachOffScreen = 24;
    private const float ChatGap = 10;
    private const float ChatEdge = 10;
    private const float ChatSmallest = 280;
    private const float ChatLargest = 465;

    /// <summary>Space kept free on each side of the screen's middle, where the player's own body and what lies by it are.</summary>
    private const float CentreGap = 56;
    private const float LightCut = 6;
    private const float ConsoleCut = 16;
    private const float LineWeight = 2;
    private const float LineInset = 8;
    private const float DashLength = 10;
    private const float DashGap = 2;
    private const float DashShine = 6;

    /// <summary>Metal between a widget and the plate's edge where a line runs inside: the mock's 18 px, with the line 8 px from the edge.</summary>
    private const float LineLane = 18;

    /// <summary>Metal above the first action or icon and below the last one.</summary>
    private const float SidePad = 12;

    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private readonly DefaultGameScreen _screen;
    private readonly StyleBoxTexture _light = Plate("light", 10);
    private readonly StyleBoxTexture _console = Plate("console", 24);
    private readonly Texture _vent = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(Path + "vent.png").Texture;
    private Color _line = SWTerminalStyle.AccentColor(SWTerminalStyle.NeutralAccent);
    private Color _shine = Color.White;
    private Texture? _crest;
    private float _sideCheck;
    private readonly SWHudKeys _keys = new();
    private readonly SWHudHands _hands = new();
    private SWGlobalWarFaction _side;

    public SWHudDecor(DefaultGameScreen screen)
    {
        _screen = screen;
        MouseFilter = MouseFilterMode.Ignore;
    }

    private static StyleBoxTexture Plate(string name, float margin)
    {
        var box = new StyleBoxTexture { Texture = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(Path + name + ".png").Texture };
        box.SetPatchMargin(StyleBox.Margin.All, margin);
        return box;
    }

    /// <summary>
    /// The chat's minimum width is fixed in logical pixels, so at a large UI scale it runs over the top bar.
    /// It gets what is left of the row instead, without saving that as the player's chosen size.
    /// </summary>
    private void KeepChatOffTopBar()
    {
        var chat = _screen.ChatBox;
        // Clear of the top bar, and clear of the middle: at a large UI scale a wide chat covered what lay just above the player.
        var limit = Math.Max(_screen.TopBar.GlobalPosition.X - GlobalPosition.X + _screen.TopBar.Size.X + ChatGap, Size.X / 2 + CentreGap);
        chat.MinWidth = Math.Clamp(Size.X - limit - ChatEdge, ChatSmallest, ChatLargest);
        if (chat.GlobalPosition.X - GlobalPosition.X < limit && Size.X - limit - ChatEdge >= ChatSmallest)
            LayoutContainer.SetMarginLeft(chat, limit - Size.X);
    }

    protected override void FrameUpdate(Robust.Shared.Timing.FrameEventArgs args)
    {
        base.FrameUpdate(args);
        KeepChatOffTopBar();
        _keys.Apply(_screen.TopBar, _configuration.GetCVar(SWVars.HudStyle) == "console");
        if (_screen.GetWidget<HotbarGui>() is { } hands)
            _hands.Prepare(hands);
        _sideCheck -= args.DeltaSeconds;
        if (_sideCheck > 0)
            return;

        _sideCheck = 1;
        var faction = SWGlobalWarConsoleStyle.LocalFaction();
        var side = _side = SWGlobalWarConsoleStyle.LocalSide();
        // The mocks' line colours, a step brighter than the window accents so that a 2 px line still reads.
        (_line, _shine) = side switch
        {
            SWGlobalWarFaction.Republic => (Color.FromHex("#46A0FF"), Color.FromHex("#BEDDFF")),
            SWGlobalWarFaction.Empire => (Color.FromHex("#FF3E36"), Color.FromHex("#FFBBB8")),
            _ => (SWTerminalStyle.AccentColor(SWTerminalStyle.AccentOf(faction)), Color.White),
        };
        _crest = side == SWGlobalWarFaction.Neutral ? null : SWGlobalWarConsoleStyle.SideIcon(side);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_configuration.GetCVar(SWVars.HudStyle) == "console")
            DrawConsole(handle);
        else
            DrawLight(handle);
    }

    private UIBox2 Rect(Control control)
    {
        var at = control.GlobalPixelPosition - GlobalPixelPosition;
        return UIBox2.FromDimensions(at, control.PixelSize);
    }

    /// <summary>A side of the screen the plate is fixed to, so that its bevel and corners stay out of sight.</summary>
    private UIBox2 ToCorner(UIBox2 box, float pad, bool right, float? below = null)
    {
        var edge = ReachOffScreen * UIScale;
        var space = pad * UIScale;
        var under = (below ?? pad) * UIScale;
        return right
            ? new UIBox2(box.Left - space, -edge, PixelWidth + edge, box.Bottom + under)
            : new UIBox2(-edge, -edge, box.Right + space, box.Bottom + under);
    }

    private void DrawLight(DrawingHandleScreen handle)
    {
        var cut = LightCut * UIScale;

        // Each plate takes its line before the next plate is laid, so a plate on top hides the line under it.
        // The tutorial hides the top bar and chat until the student needs them, so their plates wait for them too.
        if (_screen.TopBar.VisibleInTree)
        {
            var bar = Snap(ToCorner(Rect(_screen.TopBar), 4, false, LineLane));
            _light.Draw(handle, bar, UIScale);
            BarLine(handle, bar, cut);
        }
        if (_screen.ChatBox.VisibleInTree)
        {
            var chat = Snap(ToCorner(Rect(_screen.ChatBox), 4, true, LineLane));
            _light.Draw(handle, chat, UIScale);
            ChatLine(handle, chat, cut);
        }
        if (SidePlate(handle, _light, ActionsRect(), SidePad, false, LineLane) is { } rail)
            RailLine(handle, rail, cut);
        if (SidePlate(handle, _light, AlertsRect(), SidePad, true, LineLane) is { } icons)
            IconsLine(handle, icons, cut);

        if (Row() is { } row)
        {
            var hotbar = Snap(new UIBox2(row.Left - 8 * UIScale, row.Top - 6 * UIScale, row.Right + 8 * UIScale, PixelHeight + ReachOffScreen * UIScale));
            _light.Draw(handle, hotbar, UIScale);
            Dashes(handle, new Vector2(hotbar.Left + cut + LineInset * UIScale, hotbar.Top + LineInset * UIScale), hotbar.Right - cut - LineInset * UIScale, true);
        }

        if (_screen.GetWidget<HotbarGui>() is { VisibleInTree: true } handsLight)
            _hands.Draw(handle, this, handsLight, false, _side);

        if (InventoryPlate(handle, _light, LineLane) is { } pack)
            PackLine(handle, pack, cut);
    }

    private void DrawConsole(DrawingHandleScreen handle)
    {
        var cut = ConsoleCut * UIScale;
        var inset = LineInset * UIScale;

        // Each plate takes its line before the next plate is laid, so a plate on top hides the line under it.
        // The tutorial hides the top bar and chat until the student needs them, so their plates wait for them too.
        if (_screen.TopBar.VisibleInTree)
        {
            var bar = Snap(ToCorner(Rect(_screen.TopBar), 8, false, LineLane));
            _console.Draw(handle, bar, UIScale);
            BarLine(handle, bar, cut);
        }
        if (_screen.ChatBox.VisibleInTree)
        {
            var chat = Snap(ToCorner(Rect(_screen.ChatBox), 10, true, LineLane));
            _console.Draw(handle, chat, UIScale);
            ChatLine(handle, chat, cut);
        }
        if (SidePlate(handle, _console, ActionsRect(), SidePad, false, LineLane) is { } rail)
            RailLine(handle, rail, cut);
        if (SidePlate(handle, _console, AlertsRect(), SidePad, true, LineLane) is { } icons)
            IconsLine(handle, icons, cut);

        if (Row() is { } row && _screen.GetWidget<HotbarGui>() is { } hotbar)
        {
            var floor = PixelHeight + ReachOffScreen * UIScale;
            var main = Snap(new UIBox2(row.Left - 70 * UIScale, row.Top - 14 * UIScale, row.Right + 70 * UIScale, floor));
            var hands = Rect(hotbar.HandContainer);
            var middle = (hands.Left + hands.Right) / 2;
            // The raised part holds both hands whole, with its slopes outside them.
            var reach = MathF.Max(170 * UIScale, (hands.Right - hands.Left) / 2 + 10 * UIScale + cut);
            var raised = Snap(new UIBox2(middle - reach, main.Top - cut, middle + reach, floor));
            _console.Draw(handle, main, UIScale);
            _console.Draw(handle, raised, UIScale);

            var vent = (Vector2) _vent.Size * UIScale;
            handle.DrawTextureRect(_vent, UIBox2.FromDimensions(new Vector2(main.Left + 12 * UIScale, main.Top + 14 * UIScale), vent));
            handle.DrawTextureRect(_vent, UIBox2.FromDimensions(new Vector2(main.Right - 12 * UIScale - vent.X, main.Top + 14 * UIScale), vent));

            // Dashes inside each flat top: none on the slopes and none behind the crest.
            Dashes(handle, new Vector2(main.Left + cut + inset, main.Top + inset), raised.Left - inset, true);
            Dashes(handle, new Vector2(raised.Right + inset, main.Top + inset), main.Right - cut - inset, true);
            var crest = _crest != null ? _crest.Size.X * 3 * UIScale / 2 + inset : 0;
            Dashes(handle, new Vector2(raised.Left + cut + inset, raised.Top + inset), middle - crest, true);
            Dashes(handle, new Vector2(middle + crest, raised.Top + inset), raised.Right - cut - inset, true);

            _hands.Draw(handle, this, hotbar, true, _side);

            if (_crest != null)
            {
                var size = (Vector2) _crest.Size * 3 * UIScale;
                handle.DrawTextureRect(_crest, UIBox2.FromDimensions(new Vector2(middle - size.X / 2, raised.Top - size.Y / 3), size));
            }
        }

        if (InventoryPlate(handle, _console, LineLane) is { } pack)
            PackLine(handle, pack, cut);
    }

    // The dashed lines inside the plates, each clear of its plate's cut corners.
    private void BarLine(DrawingHandleScreen handle, UIBox2 bar, float cut) =>
        Dashes(handle, new Vector2(LineInset * UIScale, bar.Bottom - (LineInset + LineWeight) * UIScale), bar.Right - cut - LineInset * UIScale, true);

    private void ChatLine(DrawingHandleScreen handle, UIBox2 chat, float cut) =>
        Dashes(handle, new Vector2(chat.Left + cut + LineInset * UIScale, chat.Bottom - (LineInset + LineWeight) * UIScale), PixelWidth - LineInset * UIScale, true);

    private void RailLine(DrawingHandleScreen handle, UIBox2 rail, float cut) =>
        Dashes(handle, new Vector2(rail.Right - LineInset * UIScale, rail.Top + LineInset * UIScale), rail.Bottom - LineInset * UIScale, false);

    private void IconsLine(DrawingHandleScreen handle, UIBox2 icons, float cut) =>
        Dashes(handle, new Vector2(icons.Left + (LineInset - LineWeight) * UIScale, icons.Top + LineInset * UIScale), icons.Bottom - LineInset * UIScale, false);

    private void PackLine(DrawingHandleScreen handle, UIBox2 pack, float cut) =>
        Dashes(handle, new Vector2(LineInset * UIScale, pack.Top + LineInset * UIScale), pack.Right - cut - LineInset * UIScale, true);

    /// <summary>
    /// A plate on one side of the screen behind a column of the HUD: the actions on the left edge, the state icons on the
    /// right one. Returns the plate, or null when the column shows nothing.
    /// </summary>
    private UIBox2? SidePlate(DrawingHandleScreen handle, StyleBox plate, UIBox2? content, float pad, bool right, float facing)
    {
        if (content is not { } box)
            return null;
        var edge = ReachOffScreen * UIScale;
        var space = pad * UIScale;
        var lane = facing * UIScale;
        // As much metal above the first item as below the last one.
        var outer = Snap(right
            ? new UIBox2(box.Left - lane, box.Top - space, PixelWidth + edge, box.Bottom + space)
            : new UIBox2(-edge, box.Top - space, box.Right + lane, box.Bottom + space));
        plate.Draw(handle, outer, UIScale);
        return outer;
    }

    /// <summary>The backpack button in the bottom left corner, on a plate of its own.</summary>
    private UIBox2? InventoryPlate(DrawingHandleScreen handle, StyleBox plate, float above)
    {
        if (_screen.GetWidget<InventoryGui>() is not { VisibleInTree: true } inventory)
            return null;

        var box = Snap(Rect(inventory));
        var edge = ReachOffScreen * UIScale;
        var outer = Snap(new UIBox2(-edge, box.Top - above * UIScale, box.Right + 6 * UIScale, PixelHeight + edge));
        plate.Draw(handle, outer, UIScale);
        return outer;
    }

    /// <summary>Whole pixels, so that a plate and its line land on the same ones at any UI scale.</summary>
    private static UIBox2 Snap(UIBox2 box) => new(MathF.Round(box.Left), MathF.Round(box.Top), MathF.Round(box.Right), MathF.Round(box.Bottom));

    /// <summary>
    /// The mock's dashed line: 2 px thick, dashes of 10 with gaps of 2, the gap a dark tint of the line and a highlight
    /// on the fourth pair of pixels of each flat dash. It starts with a whole dash and stops before <paramref name="end"/>.
    /// </summary>
    private void Dashes(DrawingHandleScreen handle, Vector2 start, float end, bool horizontal)
    {
        var weight = MathF.Round(LineWeight * UIScale);
        var dash = MathF.Round(DashLength * UIScale);
        var gap = MathF.Max(1, MathF.Round(DashGap * UIScale));
        var shine = MathF.Max(1, MathF.Round(2 * UIScale));
        var shineAt = MathF.Round(DashShine * UIScale);
        var at = MathF.Round(horizontal ? start.X : start.Y);
        var across = MathF.Round(horizontal ? start.Y : start.X);
        var dark = _line.WithAlpha(.28f);
        while (at + dash <= end)
        {
            handle.DrawRect(Piece(at, dash), _line);
            if (horizontal)
                handle.DrawRect(Piece(at + shineAt, shine), _shine);
            handle.DrawRect(Piece(at + dash, gap), dark);
            at += dash + gap;
        }

        UIBox2 Piece(float from, float length) => horizontal
            ? new UIBox2(from, across, from + length, across + weight)
            : new UIBox2(across, from, across + weight, from + length);
    }

    /// <summary>Only the buttons that hold an action: the bar itself and its grid are much larger than what shows.</summary>
    private UIBox2? ActionsRect()
    {
        if (_screen.GetWidget<ActionsBar>() is not { VisibleInTree: true } actions)
            return null;

        UIBox2? union = null;
        foreach (var child in actions.ActionsContainer.Children)
        {
            if (child is not ActionButton { Action: not null, VisibleInTree: true })
                continue;
            union = Union(union, Rect(child));
        }

        return union;
    }

    /// <summary>Only the icons that show: the widget runs down the whole right side.</summary>
    private UIBox2? AlertsRect()
    {
        if (_screen.GetWidget<AlertsUI>() is not { VisibleInTree: true } alerts)
            return null;

        UIBox2? union = null;
        foreach (var child in alerts.AlertContainer.Children)
        {
            if (child.VisibleInTree)
                union = Union(union, Rect(child));
        }

        return union;
    }

    private static UIBox2 Union(UIBox2? had, UIBox2 box) => had is { } a
        ? new UIBox2(MathF.Min(a.Left, box.Left), MathF.Min(a.Top, box.Top), MathF.Max(a.Right, box.Right), MathF.Max(a.Bottom, box.Bottom))
        : box;

    /// <summary>The row of slots and hands. The widget itself is as wide as the screen.</summary>
    private UIBox2? Row()
    {
        if (_screen.GetWidget<HotbarGui>() is not { VisibleInTree: true } hotbar || hotbar.HandContainer.Parent is not { } row)
            return null;
        return Rect(row);
    }
}
