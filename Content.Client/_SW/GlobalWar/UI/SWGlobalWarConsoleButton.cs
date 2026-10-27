using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Senate datapad controls with feedback that follows travel availability.</summary>
public sealed class SWGlobalWarConsoleButton : Button
{
    private Color _color = SWGlobalWarConsoleStyle.TextBright;
    private bool _primary;
    private bool _selected;
    private bool _quiet;

    public SWGlobalWarConsoleButton()
    {
        // The default game stylesheet tints textured buttons; these surfaces already have their final color.
        ModulateSelfOverride = Color.White;
        Label.FontOverride = SWGlobalWarConsoleStyle.Strong;
        RefreshAppearance();
    }

    public void SetAppearance(Color color, bool primary = false, bool selected = false, bool quiet = false)
    {
        _color = color;
        _primary = primary;
        _selected = selected;
        _quiet = quiet;
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
        var background = _primary ? SWGlobalWarConsoleStyle.Accent
            : _selected ? SWGlobalWarConsoleStyle.Selected
            : _quiet ? Color.Transparent : SWGlobalWarConsoleStyle.PillDim;
        if (Disabled)
            background = _selected ? SWGlobalWarConsoleStyle.Selected : SWGlobalWarConsoleStyle.BgPanel;
        else if (DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed)
            background = _primary ? Color.FromHex("#C0EBFA") : SWGlobalWarConsoleStyle.Hover;

        Label.FontColorOverride = Disabled && !_selected ? SWGlobalWarConsoleStyle.TextDim
            : _primary ? SWGlobalWarConsoleStyle.Navigation
            : _selected ? SWGlobalWarConsoleStyle.TextBright : _color;
        StyleBoxOverride = new SWGlobalWarConsoleSurface
        {
            Background = background,
            Accent = SWGlobalWarConsoleStyle.Accent,
            CutCorner = _primary,
            Selected = _selected,
            ContentMarginLeftOverride = 12,
            ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 7,
            ContentMarginBottomOverride = 7,
        };
    }
}
