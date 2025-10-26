using Content.Server._SW.Economy;
using Content.Server._SW.FinanceConsole;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.TurretConsole;
using Content.Shared._SW;
using Content.Shared._SW.Economy;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Messages;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Content.Server._SW.Economy.FinanceConsole;

public sealed class SWFinanceConsoleSystem : EntitySystem
{
    [Dependency] SWFactionGameruleSystem _faction = default!;    
    [Dependency] SWSharedBankSystem _bankSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] SWBasePayableConsoleSystem _swPayable = default!;

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
        args.Handled = true;
        UpdateInterface(ent, args.Account);
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWFinanceConsoleComponent, SWBankAccountAccessorComponent>();
        while (query.MoveNext(out var uid, out var _, out var comp))
        {
            if (comp.AccountInterfaceRequiresUpdate && comp.Account != null)
            {
                UpdateInterface(uid, comp.Account.Value);
                comp.AccountInterfaceRequiresUpdate = false;
            }
        }
    }

    private void OnTryPay(Entity<SWBankAccountAccessorComponent> ent, ref SWTryPayBillMessage args)
    {
        if (ent.Comp.Account == null)
        {
            return;
        }
        _bankSystem.TryPayBill(ent.Comp.Account.Value.Owner, args.Bill);
        UpdateInterface(ent, ent.Comp.Account.Value);
    }
    private void UpdateInterface(EntityUid console, Entity<SWBankAccountContainerComponent> account)
    {
        var state = new SWFinanceConsoleBounderUserInterfaceState();
        if (TryComp<SWBillContainerComponent>(account, out var comp))
        {
            state.Bills = comp.Bills;
        }
        state.CurrentAmount = _swPayable.GetBalance(console);
        _uiSystem.SetUiState(console, SWFinanceConsoleUIKey.Key, state);
    }
}
