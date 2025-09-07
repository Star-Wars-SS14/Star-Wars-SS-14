using Content.Server._SW.Economy;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.TurretConsole;
using Content.Shared._SW;
using Content.Shared._SW.Economy;
using Robust.Server.GameObjects;
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

        ent.Comp.LinkedBankAccount =
            new Entity<SWBankAccountContainerComponent>(factEnt.Value, bankComp);
        UpdateInterface(ent);
    }

    private void UpdateInterface(Entity<SWFinanceConsoleComponent> ent)
    {
        var state = new SWFinanceConsoleBounderUserInterfaceState();
        if (TryComp<SWBillContainerComponent>(ent.Comp.LinkedBankAccount, out var comp))
        {
            state.Bills = comp.Bills;
            _s.Fatal(comp.Bills.Count.ToString());
        }
        if (ent.Comp.LinkedBankAccount != null) {
            state.CurrentAmount = ent.Comp.LinkedBankAccount.Value.Comp.CurrentAmount;
        }

        _uiSystem.SetUiState(ent.Owner, SWFinanceConsoleUIKey.Key, state);
    }
}
