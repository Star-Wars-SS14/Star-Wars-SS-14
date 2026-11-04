using Content.Client._SW.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._SW.Lobby;

/// <summary>Two sides meeting in one line, with the sword of the menu art between them.</summary>
public sealed class SWLobbySideLine : Control
{
    private static readonly Color Republic = SWTerminalStyle.AccentColor("Republic");
    private static readonly Color Empire = SWTerminalStyle.AccentColor("Empire");

    public SWLobbySideLine()
    {
        MinHeight = 3;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var middle = PixelWidth / 2f;
        handle.DrawRect(new UIBox2(0, 0, middle, PixelHeight), Republic);
        handle.DrawRect(new UIBox2(middle, 0, PixelWidth, PixelHeight), Empire);
        handle.DrawRect(new UIBox2(middle - 2 * UIScale, 0, middle + 2 * UIScale, PixelHeight), Color.White);
    }
}
