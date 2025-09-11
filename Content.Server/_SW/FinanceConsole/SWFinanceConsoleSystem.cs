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
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWFinanceConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryPayBillMessage>(OnTryPay);
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

        var comp = EnsureComp<SWBankAccountAccessorComponent>(ent);
        comp.Account =
            new Entity<SWBankAccountContainerComponent>(factEnt.Value, bankComp);
        UpdateInterface(new Entity<SWBankAccountAccessorComponent>(ent, comp));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWBankAccountAccessorComponent>();
        while(query.MoveNext(out var uid, out var comp))
        {
            if (comp.AccountInterfaceRequiresUpdate)
            {
                UpdateInterface(new Entity<SWBankAccountAccessorComponent>(uid, comp));
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

        state.CurrentAmount = ent.Comp.Account.Value.Comp.CurrentAmount;

        _uiSystem.SetUiState(ent.Owner, SWFinanceConsoleUIKey.Key, state);
    }
}
