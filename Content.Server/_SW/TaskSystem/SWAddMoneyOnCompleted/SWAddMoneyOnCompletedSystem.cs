using Content.Server._SW.Economy.BankSystem;
using Content.Shared._SW.TaskSystem;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.SWAddMoneyOnCompleted;
public sealed class SWAddMoneyOnCompletedSystem : EntitySystem
{
    [Dependency] private readonly SWBankSystem _bankSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWAddMoneyOnCompletedComponent, SWTaskProvideAdditionalDescriptionEvent>(OnDescriptionProvided);
        SubscribeLocalEvent<SWAddMoneyOnCompletedComponent, SWOnTaskCompleteEvent>(OnComplete);
    }

    private void OnDescriptionProvided(Entity<SWAddMoneyOnCompletedComponent> ent, ref SWTaskProvideAdditionalDescriptionEvent args)
    {
        var comp = EnsureComp<SWTaskComponent>(ent.Owner);
        var message = $"\n{Loc.GetString(ent.Comp.DescriptionId)}\n";
        foreach (var (key, amount) in ent.Comp.Amount)
        {
            var proto = _prototypeManager.Index(key);
            if (proto == null)
            {
                continue;
            }

            message += $"-{Loc.GetString(proto.ShortName)}: {amount}\n";
        }

        args.Task.Comp.Description += message;
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
