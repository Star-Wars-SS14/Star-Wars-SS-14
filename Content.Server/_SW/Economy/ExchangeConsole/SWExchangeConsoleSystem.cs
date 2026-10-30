using Content.Server._SW.FinanceConsole;
using Content.Shared._SW.Economy;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Messages;
using Content.Shared._SW.Economy.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Economy.ExchangeConsole;
public sealed class SWExchangeConsoleSystem : EntitySystem
{
    [Dependency] SWBasePayableConsoleSystem _swPayable = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SWSharedBankSystem _bankSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWExchangeConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWExchangeConsoleComponent, SWTryExchangeMessage>(OnTryExchange);
    }

    private void OnMapInit(Entity<SWExchangeConsoleComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<SWBankAccountAccessorComponent>(ent, out var accComp)
            || accComp.Account == null)
        {
            return;
        }
        foreach (var id in ent.Comp.AllowedCurrencies)
        {
            accComp.Account.Value.Comp.CurrentAmount[id] = 0f;
        }
        UpdateInterface(ent);
    }

    private void OnTryExchange(Entity<SWExchangeConsoleComponent> ent, ref SWTryExchangeMessage args)
    {
        if (args.CurrencyFrom == args.CurrencyTo || args.Amount <= 0)
        {
            return;
        }

        if (!ent.Comp.AllowedCurrencies.Contains(args.CurrencyFrom)
            || !ent.Comp.AllowedCurrencies.Contains(args.CurrencyTo))
        {
            return;
        }

        if (!TryComp<SWBankAccountAccessorComponent>(ent, out var accComp)
            || accComp.Account == null)
        {
            return;
        }

        if (!_bankSystem.TransferCurrency(args.Amount, args.CurrencyFrom, args.CurrencyTo, out var resultAmount)) {
            return;
        }

        // Rounding down must not eat the money: 1 RC is worth 0 IC.
        if (resultAmount <= 0)
        {
            return;
        }

        var request = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();
        request[args.CurrencyFrom] = args.Amount;

        var balance = accComp.Account.Value.Comp.CurrentAmount;
        if (_bankSystem.TryPayAll(request, balance))
        {
            balance[args.CurrencyTo] = balance.GetValueOrDefault(args.CurrencyTo) + resultAmount;
        }

        UpdateInterface(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWExchangeConsoleComponent, SWBankAccountAccessorComponent>();
        while (query.MoveNext(out var uid, out var exComp, out var comp))
        {
            if (comp.AccountInterfaceRequiresUpdate && comp.Account != null)
            {
                UpdateInterface(new Entity<SWExchangeConsoleComponent>(uid, exComp)); ;
                comp.AccountInterfaceRequiresUpdate = false;
            }
        }
    }

    private void UpdateInterface(Entity<SWExchangeConsoleComponent> ent)
    {
        var state = new SWExchangeConsoleState(ent.Comp.AllowedCurrencies);

        state.CurrentAmount = _swPayable.GetBalance(ent.Owner);
        _uiSystem.SetUiState(ent.Owner, SWExchangeConsoleUiKey.Key, state);
    }
}
