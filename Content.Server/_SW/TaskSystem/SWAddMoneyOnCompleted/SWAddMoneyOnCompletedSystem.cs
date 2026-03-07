using Content.Server._SW.Economy.BankSystem;
using Content.Shared._SW.TaskSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.SWAddMoneyOnCompleted;
public sealed class SWAddMoneyOnCompletedSystem : EntitySystem
{
    [Dependency] private readonly SWBankSystem _bankSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWAddMoneyOnCompletedComponent, SWOnTaskCompleteEvent>(OnComplete);
    }

    private void OnComplete(Entity<SWAddMoneyOnCompletedComponent> ent, ref SWOnTaskCompleteEvent args)
    {
        if (ent.Comp.Added)
        {
            return;
        }

        if (!TryComp<SWTaskComponent>(ent, out var taskComp))
        {
            return;
        }

        var account = _bankSystem.GetFactionAccount(taskComp.OwningFaction);
        account.Comp.CurrentAmount = _bankSystem.Sum(account.Comp.CurrentAmount, ent.Comp.Amount);
        ent.Comp.Added = true;
        _bankSystem.UpdateAllInterfaces();
    }
}
