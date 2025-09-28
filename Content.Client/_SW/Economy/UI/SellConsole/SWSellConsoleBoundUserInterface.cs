using Content.Client.Cargo.UI;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Events;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Content.Shared._SW.Economy.Messages;

namespace Content.Client._SW.Economy.UI.SellConsole;

public sealed class SWSellConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    [ViewVariables]
    private SWCargoPalletMenu? _menu;

    public SWSellConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
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

        if (_menu != null)
        {
            _menu.CurrencyProto = _prototypeManager.Index(palletState.OutputCurId);
            _menu.SetEnabled(palletState.Enabled);
            _menu.SetAppraisal(palletState.Appraisal);
            _menu.SetCount(palletState.Count);
        }
    }
}
