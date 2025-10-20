using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.TaskSystem;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.AcquireMoneyAmount;
public sealed class SWAcquireMoneyAmountSystem : EntitySystem
{
    [Dependency] private readonly SWFactionGameruleSystem _factionSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWAcquireMoneyAmountComponent, SWTaskCheckRequirementsEvent>(OnCheck);
        SubscribeLocalEvent<SWAcquireMoneyAmountComponent, SWTaskInitEvent>(OnInit);
        SubscribeLocalEvent<SWAcquireMoneyAmountComponent, SWGetProgressEvent>(OnGetProgress);
    }

    private void OnCheck(Entity<SWAcquireMoneyAmountComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        var taskComp = EnsureComp<SWTaskComponent>(ent);
        var factEnt = _factionSystem.GetFactionEntity(taskComp.OwningFaction);
        if (factEnt == null) {
            args.Cancel();
            return;
        }
        if (!TryComp<SWBankAccountContainerComponent>(factEnt, out var comp))
        {
            args.Cancel();
            return;
        }
    }

    private void OnInit(Entity<SWAcquireMoneyAmountComponent> ent, ref SWTaskInitEvent args)
    {
        var taskComp = EnsureComp<SWTaskComponent>(ent);
        var factEnt = _factionSystem.GetFactionEntity(taskComp.OwningFaction);
        DebugTools.Assert(factEnt != null);
        var comp = Comp<SWBankAccountContainerComponent>(factEnt.Value.Owner);

        ent.Comp.Account = new Entity<SWBankAccountContainerComponent>(factEnt.Value, comp);
    }

    private void OnGetProgress(Entity<SWAcquireMoneyAmountComponent> ent, ref SWGetProgressEvent args)
    {
        DebugTools.Assert(ent.Comp.Account != null);
        float requestAll = ent.Comp.Amount.Values.Sum();
        float haveAll = 0;
        foreach (var (currency, amount) in ent.Comp.Account.Value.Comp.CurrentAmount)
        {
            if (ent.Comp.Amount.ContainsKey(currency))
            {
                haveAll += amount;
            }
        }

        var progress = Math.Clamp(haveAll / requestAll, 0, 1);
        args.Progress = progress;
        args.Handled = true;
    }
}
