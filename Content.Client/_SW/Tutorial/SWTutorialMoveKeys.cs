using System.Numerics;
using Content.Client._SW.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.Tutorial;

/// <summary>
/// The four movement keys as a cross above the character, in the player's own bindings. It goes away on the first step,
/// not on a timer: moving is the proof that it was read.
/// </summary>
public sealed class SWTutorialMoveKeys : Control
{
    private const float CapSize = 24f;
    private const float Gap = 3f;

    public SWTutorialMoveKeys(string up, string left, string down, string right)
    {
        MouseFilter = MouseFilterMode.Ignore;

        var layout = new LayoutContainer { MouseFilter = MouseFilterMode.Ignore };
        AddChild(layout);
        SetSize = new Vector2(CapSize * 3f + Gap * 2f, CapSize * 2f + Gap);

        AddCap(layout, up, 1, 0);
        AddCap(layout, left, 0, 1);
        AddCap(layout, down, 1, 1);
        AddCap(layout, right, 2, 1);
    }

    /// <summary>
    /// Centres the cross above a point of the screen, in screen pixels.
    /// </summary>
    public void Place(Vector2 screen)
    {
        var size = SetSize * UIScale;
        LayoutContainer.SetPosition(this, new Vector2(screen.X - size.X / 2f, screen.Y - size.Y) / UIScale);
    }

    private static void AddCap(LayoutContainer layout, string key, int column, int row)
    {
        var cap = new PanelContainer
        {
            MouseFilter = MouseFilterMode.Ignore,
            SetSize = new Vector2(CapSize, CapSize),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0C0E0F").WithAlpha(0.92f),
                BorderColor = Color.White,
                BorderThickness = new Thickness(1),
            },
        };
        cap.AddChild(new Label
        {
            Text = key,
            Align = Label.AlignMode.Center,
            VAlign = Label.VAlignMode.Center,
            StyleClasses = { SWTerminalStyle.Value },
        });
        layout.AddChild(cap);
        LayoutContainer.SetPosition(cap, new Vector2(column * (CapSize + Gap), row * (CapSize + Gap)));
    }
}
