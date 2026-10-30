using Content.Client._SW.Economy.UI.BasePayAble;
using Content.Client._SW.Economy.UI.FinanceConsole;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Messages;
using Content.Shared._SW.Economy.Prototypes;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Client._SW.Economy.UI.ExchangeConsole;
public sealed class SWExchangeConsoleBoundUserInterface : SWBasePayableBoundUserInterface
{
    [Dependency] IPrototypeManager _prototypeManager = default!;
    private SWExchangeMenu? _menu;

    public SWExchangeConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
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
        _menu = this.CreateWindow<SWExchangeMenu>();
        _menu.SharedBankSystem = EntMan.System<SWSharedBankSystem>();
        _menu.OpenCenteredRight();
        _menu.OnWithdrawalButtonPressed += OpenWithdrawal;
        _menu.OnAddMoneyButtonPressed += OnAddMoney;
        _menu.OnConfirmButtonPressed += OnTryExchange;
    }
    private void OnTryExchange(SWCurrencyPrototype from,
                               SWCurrencyPrototype to,
                               int amount)
    {
        SendMessage(new SWTryExchangeMessage(from, to, amount));
    }
    private void OpenWithdrawal()
    {
        if (_menu == null)
        {
            return;
        }
        OpenWithdrawal(_menu.Balance.Keys.ToList());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SWExchangeConsoleState msg)
        {
            var curs = msg.AvailableCurrencies.Select
                (c => _prototypeManager.Index(c)).ToList();
            _menu?.PopulateCurrencies(curs);
            _menu?.UpdateExchange();
        }
    }
}
