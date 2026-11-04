using Content.Client.UserInterface.Screens;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client._SW.UserInterface.Hud;

/// <summary>Puts the HUD's metal on the standard game screen, between the world and the widgets.</summary>
public sealed class SWHudStyleUIController : UIController
{
    private const string TerminalTheme = "SWTerminalTheme";

    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();
        UIManager.OnScreenChanged += OnScreenChanged;
        LeaveBaseLooks();
    }

    /// <summary>
    /// The options offer only our two looks. A theme or layout of the base game saved earlier would hide them, so it
    /// gives way to the console look, once.
    /// </summary>
    private void LeaveBaseLooks()
    {
        var theme = _cfg.GetCVar(CVars.InterfaceTheme);
        var style = _cfg.GetCVar(SWVars.HudStyle);
        if ((theme.Length == 0 || theme == TerminalTheme)
            && _cfg.GetCVar(CCVars.UILayout) == nameof(ScreenType.Default)
            && style is "light" or "console")
        {
            return;
        }

        _cfg.SetCVar(CVars.InterfaceTheme, TerminalTheme);
        _cfg.SetCVar(CCVars.UILayout, nameof(ScreenType.Default));
        _cfg.SetCVar(SWVars.HudStyle, "console");
        _cfg.SaveToFile();
    }

    private void OnScreenChanged((UIScreen? Old, UIScreen? New) screens)
    {
        if (screens.New is not DefaultGameScreen screen)
            return;

        var decor = new SWHudDecor(screen);
        screen.AddChild(decor);
        LayoutContainer.SetAnchorPreset(decor, LayoutContainer.LayoutPreset.Wide);
        // Just over the viewport, which is always the first child.
        decor.SetPositionInParent(1);
    }
}
