using Content.Server._SW.Economy;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.TurretConsole;
using Content.Shared._SW;
using Content.Shared._SW.Economy;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Content.Server._SW.FinanceConsole;

public sealed class SWFinanceConsoleSystem : EntitySystem
{
    [Dependency] SWFactionGameruleSystem _faction = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWFinanceConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWFinanceConsoleComponent, SWTryPayBillMessage>(OnTryPay);
        _s = Logger.GetSawmill("SWFinanceConsole");
    }
    private void OnMapInit(Entity<SWFinanceConsoleComponent> ent, ref MapInitEvent args)
    {
        var factEnt = _faction.GetFactionEntity(ent.Comp.Faction);
        if (factEnt == null)
        {
            _s.Error($"{ent.Owner} Finance console doesn't have LinkedBankAccount");
            return;
        }

        var bankComp = EnsureComp<SWBankAccountContainerComponent>(factEnt.Value.Owner);

        ent.Comp.Account =
            new Entity<SWBankAccountContainerComponent>(factEnt.Value, bankComp);
        UpdateInterface(ent);
    }

    private void OnTryPay(Entity<SWFinanceConsoleComponent> ent, ref SWTryPayBillMessage args)
    {
        if (ent.Comp.Account == null)
        {
            return;
        }
        var bankComp = EnsureComp<SWBankAccountContainerComponent>(ent.Comp.Account.Value);
        var billComp = EnsureComp<SWBillContainerComponent>(ent.Comp.Account.Value);
        SWBill bill = args.Bill;
        if (TryPay(bill.CurrencyRequests, bankComp.CurrentAmount))
        {
            var a = billComp.Bills.Remove(bill);
            ISawmill _s = Logger.GetSawmill("sdfgh");
            _s.Fatal(a.ToString());
        }
        UpdateInterface(ent);
    }

    private bool TryPay(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
                        Dictionary<ProtoId<SWCurrencyPrototype>, float> balance)
    {
        foreach (var (currency, amount) in requests)
        {
            if (amount > balance[currency])
            {
                return false;
            }
        }
        ReduceBalanceAmount(requests, balance);
        return true;
    }

    private void ReduceBalanceAmount(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
                                     Dictionary<ProtoId<SWCurrencyPrototype>, float> balance)
    {
        foreach (var (currency, amount) in requests)
        {
            balance[currency] -= amount;
        }
    }

    private void UpdateInterface(Entity<SWFinanceConsoleComponent> ent)
    {
        var state = new SWFinanceConsoleBounderUserInterfaceState();
        if (TryComp<SWBillContainerComponent>(ent.Comp.Account, out var comp))
        {
            state.Bills = comp.Bills;
            _s.Fatal(comp.Bills.Count.ToString());
        }
        if (ent.Comp.Account != null)
        {
            state.CurrentAmount = ent.Comp.Account.Value.Comp.CurrentAmount;
        }

        _uiSystem.SetUiState(ent.Owner, SWFinanceConsoleUIKey.Key, state);
    }
}
