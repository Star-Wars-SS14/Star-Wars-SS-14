using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate;

/// <summary>Datapad controls with feedback that follows live permission and budget changes.</summary>
public sealed class SWConsoleButton : Button
{
    private Color _color = SWConsoleStyle.TextBright;
    private bool _primary;
    private bool _selected;
    private bool _quiet;

    public SWConsoleButton()
    {
        // The default game stylesheet tints textured buttons; these surfaces already have their final color.
        ModulateSelfOverride = Color.White;
        Label.FontOverride = SWConsoleStyle.Strong;
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
        var background = _primary ? SWConsoleStyle.Accent
            : _selected ? SWConsoleStyle.Selected
            : _quiet ? Color.Transparent : SWConsoleStyle.PillDim;
        if (Disabled)
            background = _selected ? SWConsoleStyle.Selected : SWConsoleStyle.BgPanel;
        else if (DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed)
            background = _primary ? Color.FromHex("#C0EBFA") : SWConsoleStyle.Hover;

        Label.FontColorOverride = Disabled && !_selected ? SWConsoleStyle.TextDim
            : _primary ? SWConsoleStyle.Navigation
            : _selected ? SWConsoleStyle.TextBright : _color;
        StyleBoxOverride = new SWConsoleSurface
        {
            Background = background,
            Accent = SWConsoleStyle.Accent,
            CutCorner = _primary,
            Selected = _selected,
            ContentMarginLeftOverride = 12,
            ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 7,
            ContentMarginBottomOverride = 7,
        };
    }
}
