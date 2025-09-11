using Content.Shared._SW.Economy;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Random.Rules;
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
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWBankAccountAccessorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryPayoutMessage>(OnTryPayout);
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryAddMoneyMessage>(OnAddMoney);
        _s = Logger.GetSawmill("SWPayable");
    }
    protected void OnMapInit(Entity<SWBankAccountAccessorComponent> ent, ref MapInitEvent args)
    {
        var ev = new SWGetAccountEvent();
        RaiseLocalEvent(ent, ref ev);
        if (!ev.Handled)
        {
            _s.Error($"{ent.Owner} account is not set");
        }
        ent.Comp.Account = ev.Account;
        UpdateInterface(ent);
    }
    //public override void Update(float frameTime)
    //{
    //    base.Update(frameTime);
    //    var query = EntityQueryEnumerator<SWBankAccountAccessorComponent>();
    //    while (query.MoveNext(out var uid, out var comp))
    //    {
    //        UpdateInterface(new Entity<SWBankAccountAccessorComponent>(uid, comp));
    //        comp.AccountInterfaceRequiresUpdate = false;
    //    }
    //}
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
    protected void UpdateInterface(Entity<SWBankAccountAccessorComponent> ent)
    {
        ISawmill _s = Logger.GetSawmill("asa");
        _s.Fatal("1");
        if (ent.Comp.Account == null)
        {
            return;
        }
        _s.Fatal("2");
        var state = new SWBasePayableBounderUserInterfaceState();
        state.CurrentAmount = ent.Comp.Account.Value.Comp.CurrentAmount;
        _s.Fatal(state.CurrentAmount.Keys.Count.ToString());
        _s.Fatal(ent.Comp.UiKey.ToString());
        _uiSystem.SetUiState(ent.Owner, ent.Comp.UiKey, state);
    }
}

[ByRefEvent]
public sealed class SWGetAccountEvent : HandledEntityEventArgs
{
    public Entity<SWBankAccountContainerComponent> Account;
}

