using Content.Server._SW.Economy.BankSystem;
using Content.Server._SW.TaskSystem.GetScoreByTargetJob;
using Content.Server._SW.TaskSystem.TargetSelector;
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
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWAddMoneyOnCompletedComponent, SWOnTaskCompleteEvent>(OnComplete);
        SubscribeLocalEvent<SWAddMoneyBasedOnTargetScoreComponent, SWOnTaskCompleteEvent>(OnCompleteBasedOnScore);
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

    private void OnCompleteBasedOnScore(Entity<SWAddMoneyBasedOnTargetScoreComponent> ent, ref SWOnTaskCompleteEvent args)
    {
        if (ent.Comp.Added)
            return;

        if (!TryComp<SWTargetSelectorComponent>(ent, out var comp))
            return;

        if (!TryComp<SWTaskComponent>(ent, out var taskComp))
            return;

        if (!TryComp<SWGetScoreByTargetJobComponent>(ent, out var scoreComp))
            return;

        if (comp.JobOfTarget == null)
            return;

        var account = _bankSystem.GetFactionAccount(taskComp.OwningFaction);
        scoreComp.JobsToScores.TryGetValue(comp.JobOfTarget, out var scores);

        var amount = ent.Comp.MoneyPerScore.ToDictionary(kvp => kvp.Key, kvp => kvp.Value * scores);

        account.Comp.CurrentAmount = _bankSystem.Sum(account.Comp.CurrentAmount, amount);
        ent.Comp.Added = true;
        _bankSystem.UpdateAllInterfaces();
    }
}
