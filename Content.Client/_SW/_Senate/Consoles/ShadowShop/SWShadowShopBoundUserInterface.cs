using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.ShadowShop;

public sealed class SWShadowShopBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWShadowShopMenu? _menu;

    public SWShadowShopBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWShadowShopMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWShadowShopState castState)
            return;

        _menu?.SetState(castState);
    }

    public void Buy(int index)
    {
        SendMessage(new SWShadowShopBuyMessage(index));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
