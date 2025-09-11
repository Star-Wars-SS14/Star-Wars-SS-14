using Content.Server._SW.Economy;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.TurretConsole;
using Content.Shared._SW;
using Content.Shared._SW.Economy;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
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
    [Dependency] SWSharedBankSystem _bankSystem = default!;
    
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWFinanceConsoleComponent, SWGetAccountEvent>(OnGetAccount);
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryPayBillMessage>(OnTryPay);
    }
    private void OnGetAccount(Entity<SWFinanceConsoleComponent> ent, ref SWGetAccountEvent args)
    {
        var factEnt = _faction.GetFactionEntity(ent.Comp.Faction);
        if (factEnt == null)
        {
            return;
        }

        var bankComp = EnsureComp<SWBankAccountContainerComponent>(factEnt.Value.Owner);

        var comp = EnsureComp<SWBankAccountAccessorComponent>(ent);
        args.Account =
            new Entity<SWBankAccountContainerComponent>(factEnt.Value, bankComp);
        comp.Account = new Entity<SWBankAccountContainerComponent>(factEnt.Value, bankComp);
        UpdateInterface(new Entity<SWBankAccountAccessorComponent>(ent, comp));
        args.Handled = true;
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWBankAccountAccessorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            UpdateInterface(new Entity<SWBankAccountAccessorComponent>(uid, comp));
            comp.AccountInterfaceRequiresUpdate = false;
        }
    }

    private void OnTryPay(Entity<SWBankAccountAccessorComponent> ent, ref SWTryPayBillMessage args)
    {
        if (ent.Comp.Account == null)
        {
            return;
        }
        var bankComp = EnsureComp<SWBankAccountContainerComponent>(ent.Comp.Account.Value);
        var billComp = EnsureComp<SWBillContainerComponent>(ent.Comp.Account.Value);
        if (_bankSystem.TryPay(args.Bill.CurrencyRequests, bankComp.CurrentAmount))
        {
            billComp.Bills.Remove(args.Bill);
        }
        UpdateInterface(ent);
    }
    private void UpdateInterface(Entity<SWBankAccountAccessorComponent> ent)
    {
        if (ent.Comp.Account == null)
        {
            return;
        }
        var state = new SWFinanceConsoleBounderUserInterfaceState();
        if (TryComp<SWBillContainerComponent>(ent.Comp.Account, out var comp))
        {
            state.Bills = comp.Bills;
        }
        ISawmill _s = Logger.GetSawmill("sdf");
        _s.Fatal(state.Bills.Count.ToString());
        _uiSystem.SetUiState(ent.Owner, SWFinanceConsoleUIKey.Key, state);
    }
}
