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
using Content.Client._SW.Economy.UI.BasePayAble;
using Robust.Client.UserInterface.CustomControls;
using Content.Shared._SW.Economy.Messages;
using Content.Shared._SW.Economy.Prototypes;
namespace Content.Client._SW.Economy.UI.FinanceConsole;

public sealed partial class SWFinanceConsoleBoundUserInterface : SWBasePayableBoundUserInterface
{
    private SWFinanceConsoleMenu? _menu = null;
    public SWFinanceConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    public override void PopulateBalance(Dictionary<ProtoId<SWCurrencyPrototype>, float> amount)
    {
        //if (amount.Keys.Count == 0)
        //{
        //    return;
        //}
        _menu?.PopulateBalance(amount);
    }
    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWFinanceConsoleMenu>();
        _menu.OpenCenteredRight();
        _menu.OnPayButtonPressed += SendPayBillMessage;
        _menu.OnWithdrawalButtonPressed += OpenWithdrawal;
        _menu.OnAddMoneyButtonPressed += OnAddMoney;
    }

    private void OpenWithdrawal()
    {
        if (_menu == null)
        {
            return;
        }
        OpenWithdrawal(_menu.Balance.Keys.ToList());
    }

    public void SendPayBillMessage(SWBill bill)
    {
        SendMessage(new SWTryPayBillMessage(bill.Id));
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
                break;
        }
    }
}
