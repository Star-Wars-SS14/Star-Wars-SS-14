using Content.Client.Cargo.UI;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Events;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Content.Shared._SW.Economy;

namespace Content.Client._SW.Economy.UI.SellConsole;

public sealed class SWCargoPalletConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWCargoPalletMenu? _menu;

    public SWCargoPalletConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SWCargoPalletMenu>();
        _menu.AppraiseRequested += OnAppraisal;
        _menu.SellRequested += OnSell;
    }

    private void OnAppraisal()
    {
        SendMessage(new CargoPalletAppraiseMessage());
    }

    private void OnSell()
    {
        SendMessage(new CargoPalletSellMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWCargoPalletConsoleInterfaceState palletState)
            return;

        _menu?.SetEnabled(palletState.Enabled);
        _menu?.SetAppraisal(palletState.Appraisal);
        _menu?.SetCount(palletState.Count);
    }
}
