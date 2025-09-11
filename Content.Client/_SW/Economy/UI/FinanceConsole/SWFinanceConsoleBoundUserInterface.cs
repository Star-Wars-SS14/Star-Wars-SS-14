using Content.Client._SW.TurretConsole.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Robust.Client.UserInterface.Controls.MenuBar;
using Robust.Client.UserInterface;
using Content.Shared._SW;
using Content.Shared._SW.Economy;
using Robust.Shared.Prototypes;
namespace Content.Client._SW.Economy.UI.FinanceConsole;

public sealed partial class SWFinanceConsoleBoundUserInterface : BoundUserInterface
{
    private SWFinanceConsoleMenu? _menu = null;
    private SWWithdrawalWindow? _withdrawal = null;
    public SWFinanceConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWFinanceConsoleMenu>();
        _menu.OpenCenteredRight();
        _menu.OnPayButtonPressed += SendPayBillMessage;
        _menu.OnWithdrawalButtonPressed += OpenWitdrawal;
        _menu.OnAddMoneyButtonPressed += SendTryAddMoneyMessage;
    }

    private void OpenWitdrawal()
    {
        if (_menu == null)
        {
            return;
        }
        _withdrawal = this.CreateWindow<SWWithdrawalWindow>();
        _withdrawal.PopulateCurrencies(_menu.Balance.Keys.ToList());
        _withdrawal.OpenCenteredRight();
        _withdrawal.OnConfirmButtonPressedAction += OnConfirmButtonPressed;
    }

    public void SendPayBillMessage(SWBill bill)
    {
        SendMessage(new SWTryPayBillMessage(bill));
    }

    public void OnConfirmButtonPressed(Dictionary<ProtoId<SWCurrencyPrototype>, float> dict)
    {
        SendMessage(new SWTryPayoutMessage(dict));
        _withdrawal?.Close();
    }

    public void SendTryAddMoneyMessage()
    {
        SendMessage(new SWTryAddMoneyMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case SWFinanceConsoleBounderUserInterfaceState msg:
                if (_menu == null)
                {
                    break;
                }
                _menu.PopulateBills(msg.Bills);
                _menu.PopulateBalance(msg.CurrentAmount);
                break;
        }
    }
}
