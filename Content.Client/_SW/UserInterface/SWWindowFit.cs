using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._SW.UserInterface;

/// <summary>
/// Shrinks an upstream window that is bigger than the screen at the player's UI scale, and centres it, so that its title
/// and close button stay reachable. At 1280x720 with the automatic scale of 1.36 the screen is about 941x529.
/// </summary>
public static class SWWindowFit
{
    private const float Margin = 8;

    public static void Fit(BaseWindow window)
    {
        var screen = IoCManager.Resolve<IUserInterfaceManager>().RootControl.Size - new Vector2(Margin * 2);
        if (screen.X <= 0 || screen.Y <= 0 || window.MinSize.X <= screen.X && window.MinSize.Y <= screen.Y && window.SetSize.X <= screen.X && window.SetSize.Y <= screen.Y)
            return;

        window.MinSize = Vector2.Min(window.MinSize, screen);
        window.SetSize = Vector2.Min(window.SetSize == Vector2.Zero ? window.Size : window.SetSize, screen);
        window.RecenterWindow(new Vector2(0.5f, 0.5f));

        // Content with a tall minimum of its own still overflows: keep at least the title and the close button on screen.
        window.OnResized += () => KeepTitleOnScreen(window);
        KeepTitleOnScreen(window);
    }

    private static void KeepTitleOnScreen(BaseWindow window)
    {
        if (window.Position.X >= 0 && window.Position.Y >= 0)
            return;
        LayoutContainer.SetPosition(window, Vector2.Max(window.Position, new Vector2(Margin)));
    }
}
