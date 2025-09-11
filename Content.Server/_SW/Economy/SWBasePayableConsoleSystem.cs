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

namespace Content.Server._SW.Economy;
[Virtual]
public class SWBasePayableConsoleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] SharedStackSystem _stackSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] SharedHandsSystem _handsSystem = default!;
    [Dependency] SWSharedBankSystem _bankSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWBankAccountAccessorComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryPayoutMessage>(OnTryPayout);
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryAddMoneyMessage>(OnAddMoney);
    }
    protected void OnStartup(Entity<SWBankAccountAccessorComponent> ent, ref ComponentStartup args)
    {
        UpdateInterface(ent);
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
    protected void OnTryPayout(Entity<SWBankAccountAccessorComponent> ent, ref SWTryPayoutMessage args)
    {
        if (ent.Comp.Account == null)
        {
            return;
        }
        if (_bankSystem.TryPay(args.Requests, ent.Comp.Account.Value.Comp.CurrentAmount))
        {
            foreach (var (currency, amount) in args.Requests)
            {
                var proto = _prototypeManager.Index(currency);
                var spawnedCash = Spawn(proto.CashEntity, Transform(ent).Coordinates);
                _stackSystem.SetCount(spawnedCash, (int)amount);
            }
        }
        UpdateInterface(ent);
    }
    protected void OnAddMoney(Entity<SWBankAccountAccessorComponent> ent, ref SWTryAddMoneyMessage args)
    {
        if (ent.Comp.Account == null)
        {
            return;
        }

        if (args.Actor is not { Valid: true } user)
            return;

        foreach (var entity in _handsSystem.EnumerateHeld(user))
        {
            if (!TryComp<StackComponent>(entity, out var comp))
            {
                continue;
            }
            var proto = _bankSystem.GetCurrencyProto(entity);
            if (proto != null)
            {
                ent.Comp.Account.Value.Comp.CurrentAmount[proto] += comp.Count;
                QueueDel(entity);
            }
        }
        UpdateInterface(ent);
    }
    protected virtual void UpdateInterface(Entity<SWBankAccountAccessorComponent> ent)
    {
        if (ent.Comp.Account == null)
        {
            return;
        }
        var state = new SWBasePayableBounderUserInterfaceState();
        state.CurrentAmount = ent.Comp.Account.Value.Comp.CurrentAmount;
        ISawmill _s = Logger.GetSawmill("dsfg");
        _s.Fatal(ent.Comp.Account.Value.Comp.CurrentAmount.Keys.Count.ToString());

        var comp = EnsureComp<UserInterfaceComponent>(ent);
        _uiSystem.SetUiState(ent.Owner, ent.Comp.UIKey, state);
    }
}
