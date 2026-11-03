using Content.Client.UserInterface.Screens;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.UserInterface.Hud;

/// <summary>Puts the HUD's metal on the standard game screen, between the world and the widgets.</summary>
public sealed class SWHudStyleUIController : UIController
{
    public override void Initialize()
    {
        base.Initialize();
        UIManager.OnScreenChanged += OnScreenChanged;
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
