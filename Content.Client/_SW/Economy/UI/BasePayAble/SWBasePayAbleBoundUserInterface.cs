using Content.Shared._SW.Economy.Messages;
using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Robust.Client.UserInterface.Controls.MenuBar;

namespace Content.Client._SW.Economy.UI.BasePayAble;
public abstract partial class SWBasePayableBoundUserInterface : BoundUserInterface
{
    private SWWithdrawalWindow? _withdrawal;

    public SWBasePayableBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected void OpenWithdrawal(List<ProtoId<SWCurrencyPrototype>> currencies)
    {
        CloseWithdrawal();
        _withdrawal = new SWWithdrawalWindow();
        _withdrawal.PopulateCurrencies(currencies);
        _withdrawal.OpenCenteredRight();
        _withdrawal.OnConfirmButtonPressedAction += OnWithdrawal;
    }

    private void CloseWithdrawal()
    {
        _withdrawal?.Close();
        _withdrawal?.Dispose();
        _withdrawal = null;
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is SWConsoleActionSucceededMessage result)
        {
            OnActionSucceeded(result.Action);
        }
    }

    protected virtual void OnActionSucceeded(SWConsoleAction action)
    {
        if (action == SWConsoleAction.Payout)
        {
            CloseWithdrawal();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            CloseWithdrawal();
        }
    }

    protected virtual void OnWithdrawal(Dictionary<ProtoId<SWCurrencyPrototype>, float> dict)
    {
        SendMessage(new SWTryPayoutMessage(dict));
    }

    protected virtual void OnAddMoney()
    {
        SendMessage(new SWTryAddMoneyMessage());
    }
    public abstract void PopulateBalance(Dictionary<ProtoId<SWCurrencyPrototype>, float> amount);
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        switch (state)
        {
            case SWBasePayableBounderUserInterfaceState msg:
                PopulateBalance(msg.CurrentAmount);
                break;
        }
    }
}
