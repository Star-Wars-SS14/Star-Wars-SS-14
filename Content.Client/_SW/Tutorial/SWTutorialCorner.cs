using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.Tutorial;

/// <summary>
/// Everything that is not the current task, kept out of the way and always in one place: one small button with a menu.
/// </summary>
public sealed class SWTutorialCorner : UIWidget
{
    private readonly BoxContainer _menu;

    public event Action? OnExitPressed;
    public event Action? OnRetryPressed;
    public event Action? OnShowAllPressed;

    public SWTutorialCorner()
    {
        MouseFilter = MouseFilterMode.Ignore;

        _menu = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            Visible = false,
        };
        _menu.AddChild(MakeItem("sw-tutorial-exit-button", () => OnExitPressed?.Invoke()));
        _menu.AddChild(MakeItem("sw-tutorial-retry-button", () => OnRetryPressed?.Invoke()));
        _menu.AddChild(MakeItem("sw-tutorial-show-all-button", () => OnShowAllPressed?.Invoke()));

        var open = new Button
        {
            Text = "≡",
            ToolTip = Loc.GetString("sw-tutorial-menu-button"),
            StyleClasses = { "SWTermButton" },
            MinSize = new(28, 28),
            HorizontalAlignment = HAlignment.Left,
        };
        open.OnPressed += _ => _menu.Visible = !_menu.Visible;

        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        column.AddChild(_menu);
        column.AddChild(open);
        AddChild(column);
    }

    private Button MakeItem(string text, Action action)
    {
        var button = new Button { Text = Loc.GetString(text), StyleClasses = { "SWTermButton" } };
        button.OnPressed += _ =>
        {
            _menu.Visible = false;
            action();
        };
        return button;
    }
}
