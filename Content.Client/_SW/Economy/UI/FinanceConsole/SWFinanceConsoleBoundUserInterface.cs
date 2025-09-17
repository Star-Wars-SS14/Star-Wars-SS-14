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
namespace Content.Client._SW.Economy.UI.FinanceConsole;

public sealed partial class SWFinanceConsoleBoundUserInterface : SWBasePayAbleBoundUserInterface
{
    private SWFinanceConsoleMenu? _menu = null;
    private SWWithdrawalWindow? _withdrawal = null;
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
        ISawmill _s = Logger.GetSawmill("asdf");
        _s.Fatal("asdfgh");
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
        _withdrawal = new SWWithdrawalWindow();
        _withdrawal.PopulateCurrencies(_menu.Balance.Keys.ToList());
        _withdrawal.OpenCenteredRight();
        _withdrawal.OnConfirmButtonPressedAction += OnWithdrawal;
    }

    public void SendPayBillMessage(SWBill bill)
    {
        SendMessage(new SWTryPayBillMessage(bill));
    }

    protected override void OnWithdrawal(Dictionary<ProtoId<SWCurrencyPrototype>, float> dict)
    {
        base.OnWithdrawal(dict);
        _withdrawal?.Close();
    }

    //protected override void Dispose(bool disposing)
    //{
    //    base.Dispose(disposing);

    //    if (!disposing)
    //        return;

    //    _menu?.Orphan();
    //    _withdrawal?.Orphan();
    //}
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        ISawmill _s = Logger.GetSawmill("jjjjj");
        _s.Fatal("here");
        switch (state)
        {
            case SWFinanceConsoleBounderUserInterfaceState msg:
                if (_menu == null)
                {
                    break;
                }
                _s.Fatal(msg.Bills.Count.ToString());
                _menu.PopulateBills(msg.Bills);
                break;
        }
        _s.Fatal("move forward fin");
    }
}
