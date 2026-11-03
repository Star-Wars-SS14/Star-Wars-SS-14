using System.Numerics;
using Content.Client._SW.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>
/// A campaign console window: the terminal frame and font, with the colour and the name of the reader's side.
/// Declare it as the root of a menu's XAML instead of a <c>FancyWindow</c>.
/// </summary>
[Virtual]
public class SWGlobalWarWindow : SWTerminalWindow
{
    private const float RootMargin = 12;
    private const float BracketLength = 22;
    private const float BracketWeight = 2;
    private const float BracketOffset = 3;

    /// <summary>The marks outside the frame. The mocks draw them on the chart windows and the verdict.</summary>
    protected bool CornerBrackets = true;

    private bool _named;
    private Vector2 _rootSize;
    private bool _fitPosition;

    public SWGlobalWarWindow()
    {
        Stylesheet = SWGlobalWarConsoleStyle.Sheet();
        WindowTitle.FontOverride = SWGlobalWarConsoleStyle.TitleLarge;
        AccentLine.Margin = new Thickness(0);
    }

    /// <summary>A window with the star chart takes most of the screen: 1237x707 at 720p, 1560x920 at 1080p.</summary>
    protected void UseChartSize()
    {
        var root = IoCManager.Resolve<IUserInterfaceManager>().WindowRoot.Size;
        if (root.X > 0 && root.Y > 0)
            SetSize = Vector2.Min(new Vector2(1560, 920), root * new Vector2(.875f, .889f));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (!CornerBrackets)
            return;

        // The marks stand just outside the frame, in the colour of the side.
        var color = SWTerminalStyle.AccentColor(Accent);
        var offset = BracketOffset * UIScale;
        var length = BracketLength * UIScale;
        var weight = MathF.Max(2, BracketWeight * UIScale);
        var size = (Vector2) PixelSize;
        for (var sx = 0; sx < 2; sx++)
        for (var sy = 0; sy < 2; sy++)
        {
            var corner = new Vector2(sx == 0 ? -offset : size.X + offset, sy == 0 ? -offset : size.Y + offset);
            var dx = sx == 0 ? 1 : -1;
            var dy = sy == 0 ? 1 : -1;
            handle.DrawRect(new UIBox2(Vector2.Min(corner, corner + new Vector2(dx * length, dy * weight)),
                Vector2.Max(corner, corner + new Vector2(dx * length, dy * weight))), color);
            handle.DrawRect(new UIBox2(Vector2.Min(corner, corner + new Vector2(dx * weight, dy * length)),
                Vector2.Max(corner, corner + new Vector2(dx * weight, dy * length))), color);
        }
    }

    protected override void Opened()
    {
        base.Opened();

        SetFaction(SWGlobalWarConsoleStyle.LocalFaction());
        if (!_named)
            SetCampaignTitle(Title ?? string.Empty);
    }

    /// <summary>
    /// Sets the title. Some accents look alike with colour blindness, so the side is also named.
    /// </summary>
    protected void SetCampaignTitle(string title)
    {
        _named = true;
        var name = IoCManager.Resolve<IUserInterfaceManager>().GetUIController<SWTerminalStyleUIController>()
            .GetFactionName(SWGlobalWarConsoleStyle.LocalFaction());
        Title = name == null ? title : Loc.GetString("sw-terminal-owner-title", ("title", title), ("faction", name));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        FitToRoot();
    }

    /// <summary>
    /// Keeps the window on the screen at any resolution and UI scale, so that its title and cross stay in reach.
    /// </summary>
    private void FitToRoot()
    {
        if (Root == null || Size.X <= 0 || Size.Y <= 0)
            return;

        var margin = new Vector2(RootMargin);
        if (Root.Size != _rootSize)
        {
            _rootSize = Root.Size;
            SetSize = Vector2.Min(Size, Vector2.Max(MinSize, Root.Size - margin * 2));
            _fitPosition = true;
            return;
        }

        if (!_fitPosition)
            return;

        // Clamp after Arrange has applied the size change to the centered window.
        _fitPosition = false;
        LayoutContainer.SetPosition(this, Vector2.Clamp(Position, margin, Vector2.Max(margin, Root.Size - Size - margin)));
    }
}
