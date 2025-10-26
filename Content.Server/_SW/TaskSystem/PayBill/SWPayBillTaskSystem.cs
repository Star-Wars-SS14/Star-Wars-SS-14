using Content.Server._SW.Economy.BankSystem;
using Content.Server._SW.TaskSystem.AcquireMoneyAmount;
using Content.Shared._SW.Economy;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.TaskSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.PayBill;
public sealed class SWPayBillTaskSystem : EntitySystem
{
    [Dependency] private readonly SWBankSystem _bankSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWPayBillTaskComponent, SWTaskCheckRequirementsEvent>(OnCheck);
        SubscribeLocalEvent<SWPayBillTaskComponent, SWTaskInitEvent>(OnInit);
        SubscribeLocalEvent<SWPayBillTaskComponent, SWGetProgressEvent>(OnGetProgress);

    }

    private void OnCheck(Entity<SWPayBillTaskComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        var taskComp = EnsureComp<SWTaskComponent>(ent);
        var account = _bankSystem.TryGetFactionAccount(taskComp.OwningFaction);
        if (account == null)
        {
            args.Cancel();
            return;
        }

        if (!HasComp<SWBillContainerComponent>(account))
        {
            args.Cancel();
        }
    }

    private void OnInit(Entity<SWPayBillTaskComponent> ent, ref SWTaskInitEvent args)
    {
        var taskComp = EnsureComp<SWTaskComponent>(ent);
        var account = _bankSystem.GetFactionAccount(taskComp.OwningFaction);
        _bankSystem.SendBill(account.Owner, ref ent.Comp.Bill);
    }

    private void OnGetProgress(Entity<SWPayBillTaskComponent> ent, ref SWGetProgressEvent args)
    {
        var taskComp = EnsureComp<SWTaskComponent>(ent);
        var account = _bankSystem.GetFactionAccount(taskComp.OwningFaction);
        args.Progress = _bankSystem.BillIsPaid(account.Owner, ent.Comp.Bill) ? 1.0 : 0.0;
        args.Handled = true;
    }
}
