using Content.Client._SW.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A terminal button whose look follows its role: main action, a payment in gold, selected tab, quiet tool.</summary>
public sealed class SWGlobalWarConsoleButton : Button
{
    private Color _color = SWGlobalWarConsoleStyle.TextBright;
    private bool _primary;
    private bool _selected;
    private bool _quiet;
    private bool _money;
    private bool _tab;
    private bool _danger;

    /// <summary>The colour of a side, for a button that belongs to that side and not to the reader's.</summary>
    public string? Accent;

    /// <summary>A bigger word for a main button that stands alone.</summary>
    public Font? MainFont;

    public SWGlobalWarConsoleButton()
    {
        // The default game stylesheet tints textured buttons; these boxes already have their final color.
        ModulateSelfOverride = Color.White;
        RefreshAppearance();
    }

    public void SetAppearance(Color color, bool primary = false, bool selected = false, bool quiet = false, bool money = false, bool tab = false, bool danger = false)
    {
        _color = color;
        _danger = danger;
        _primary = primary || money || danger;
        _money = money;
        _selected = selected;
        _quiet = quiet;
        _tab = tab;
        RefreshAppearance();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        // ContainerButton calls this from its constructor, before Button creates its label.
        if (Label != null)
            RefreshAppearance();
    }

    private void RefreshAppearance()
    {
        var hovered = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed;
        var selected = _selected || ToggleMode && Pressed;
        var accent = Accent ?? SWTerminalStyle.AccentOf(SWGlobalWarConsoleStyle.LocalFaction());
        var (mainFill, mainHover, mainBorder) = SWTerminalStyle.AccentFills(_money ? SWTerminalStyle.MoneyAccent : _danger ? "Empire" : accent);

        var fill = _tab ? hovered && !selected ? SWTerminalStyle.ButtonHover : Color.Transparent
            : Disabled ? SWTerminalStyle.Disabled
            : _primary ? hovered ? mainHover : mainFill
            : Accent != null && selected ? mainFill
            : hovered || selected ? SWTerminalStyle.ButtonHover
            : _quiet ? Color.Transparent : SWTerminalStyle.ButtonFill;
        var border = _tab ? Color.Transparent
            : Disabled ? SWTerminalStyle.DividerColor
            : _primary ? mainBorder : SWTerminalStyle.ButtonBorder;

        // A live button speaks in bold; a closed one, a quiet tool and an unselected tab do not.
        Label.FontOverride = Disabled || _quiet || _tab && !selected ? SWGlobalWarConsoleStyle.Body : MainFont ?? SWGlobalWarConsoleStyle.Strong;
        Label.FontColorOverride = Disabled ? SWTerminalStyle.TextOff
            : _primary ? Color.White
            : selected ? SWGlobalWarConsoleStyle.TextBright
            : _tab ? SWGlobalWarConsoleStyle.TextDim : _color;

        var box = SWTerminalStyle.Box(fill, border, 1, _tab ? 6 : 10, 6);
        if (selected)
        {
            // A selected tab keeps a line in the owner's colour. In the lobby there is no owner, so the line is white.
            box.BorderColor = accent == SWTerminalStyle.NeutralAccent ? SWGlobalWarConsoleStyle.TextBright : SWTerminalStyle.AccentColor(accent);
            box.BorderThickness = new Thickness(0, 0, 0, 3);
        }

        StyleBoxOverride = box;
    }
}
