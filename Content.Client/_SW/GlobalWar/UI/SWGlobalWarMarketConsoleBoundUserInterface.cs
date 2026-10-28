using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarMarketConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarMarketConsoleMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWGlobalWarMarketConsoleMenu>();
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SWGlobalWarMarketConsoleState market)
            _menu?.UpdateState(market);
    }
}
