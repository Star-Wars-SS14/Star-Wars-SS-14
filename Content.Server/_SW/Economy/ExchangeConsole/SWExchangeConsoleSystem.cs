using Content.Server._SW.FinanceConsole;
using Content.Shared._SW.Economy;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Messages;
using Robust.Server.GameObjects;
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
        if (args.CurrencyFrom == args.CurrencyTo)
        {
            return;
        }

        if (!TryComp<SWBankAccountAccessorComponent>(ent, out var accComp)
            || accComp.Account == null)
        {
            return;
        }
        int resultAmount = _bankSystem.TransferCurrency(args.Amount, args.CurrencyFrom, args.CurrencyTo);
        accComp.Account.Value.Comp.CurrentAmount[args.CurrencyFrom] -= args.Amount;
        accComp.Account.Value.Comp.CurrentAmount[args.CurrencyTo] += resultAmount;
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
