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
namespace Content.Client._SW.Economy.UI;

public sealed partial class SWFinanceConsoleBoundUserInterface : BoundUserInterface
{
    private SWFinanceConsoleMenu? _menu = null;
    public SWFinanceConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWFinanceConsoleMenu>();
        _menu.OpenCenteredRight();
        _menu.OnPaynButtonPressed += SendPayBillMessage;
    }

    public void SendPayBillMessage(SWBill bill)
    {
        SendMessage(new SWTryPayBillMessage(bill));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case SWFinanceConsoleBounderUserInterfaceState msg:
                _menu?.PopulateBills(msg.Bills);
                _menu?.PopulateBalance(msg.CurrentAmount);
                break;
        }
    }
}
