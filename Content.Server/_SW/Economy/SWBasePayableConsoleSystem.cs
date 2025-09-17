using Content.Server._SW.Economy.FinanceConsole;
using Content.Server._SW.FinanceConsole;
using Content.Shared._SW;
using Content.Shared._SW.Economy;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Random.Rules;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Server.GameObjects;
namespace Content.Server._SW.Economy;

public sealed class SWBasePayableConsoleSystem : EntitySystem
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
        //SubscribeLocalEvent<SWBankAccountAccessorComponent, BeforeActivatableUIOpenEvent>(OnOpen, before: [typeof(SWFinanceConsoleSystem)]);
        _s = Logger.GetSawmill("SWPayable");
    }

    //public override void Update(float frameTime)
    //{
    //    base.Update(frameTime);
    //    var query = EntityQueryEnumerator<SWBankAccountAccessorComponent>();
    //    while (query.MoveNext(out var uid, out var comp))
    //    {
    //        if (comp.Account != null)
    //        {
    //            UpdateInterface(new Entity<SWBankAccountAccessorComponent>(uid, comp));
    //            comp.AccountInterfaceRequiresUpdate = false;
    //        }
    //    }
    //}
    private void OnMapInit(Entity<SWBankAccountAccessorComponent> ent, ref MapInitEvent args)
    {
        var ev = new SWGetAccountEvent();
        RaiseLocalEvent(ent, ref ev);
        if (!ev.Handled)
        {
            if (TryComp<SWBankAccountContainerComponent>(ent, out var comp))
            {
                ev.Account = new Entity<SWBankAccountContainerComponent>(ent, comp);
                ev.Handled = true;
            }
            else
            {
                _s.Error($"{ent.Owner} account is not set");
            }
        }
        if (ev.Handled)
        {
            ent.Comp.Account = ev.Account;
        }
        ent.Comp.AccountInterfaceRequiresUpdate = true;
        //UpdateInterface(ent);
    }
    private void OnTryPayout(Entity<SWBankAccountAccessorComponent> ent, ref SWTryPayoutMessage args)
    {
        if (ent.Comp.Account == null)
        {
            return;
        }
        if (_bankSystem.TryPayAll(args.Requests, ent.Comp.Account.Value.Comp.CurrentAmount))
        {
            foreach (var (currency, amount) in args.Requests)
            {
                var proto = _prototypeManager.Index(currency);
                var spawnedCash = Spawn(proto.CashEntity, Transform(ent).Coordinates);
                _stackSystem.SetCount(spawnedCash, (int)amount);
            }
        }
        ent.Comp.AccountInterfaceRequiresUpdate = true;
        //UpdateInterface(ent);
    }
    private void OnAddMoney(Entity<SWBankAccountAccessorComponent> ent, ref SWTryAddMoneyMessage args)
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
            _s.Fatal((proto==null).ToString());
            if (proto != null)
            {
                ent.Comp.Account.Value.Comp.CurrentAmount[proto] += comp.Count;
                QueueDel(entity);
            }
        }
        ent.Comp.AccountInterfaceRequiresUpdate = true;
        //UpdateInterface(ent);
    }
    //private void UpdateInterface(Entity<SWBankAccountAccessorComponent> ent)
    //{
    //    if (ent.Comp.Account == null)
    //    {
    //        return;
    //    }

    //    var state = new SWBasePayableBounderUserInterfaceState();
    //    state.CurrentAmount = ent.Comp.Account.Value.Comp.CurrentAmount;
    //    _s.Fatal(state.CurrentAmount.Keys.Count.ToString());
    //    //_s.Fatal(ent.Comp.UiKey.ToString());
    //    //_uiSystem.SetUiState(ent.Owner, SWFinanceConsoleUIKey.Key, state);
    //    //_resolver.RequestUiUpdate(ent.Owner, SWFinanceConsoleUIKey.Key, state);
    //}

    public Dictionary<ProtoId<SWCurrencyPrototype>, float> GetBalance(Entity<SWBankAccountAccessorComponent?> console)
    {
        console.Comp = Comp<SWBankAccountAccessorComponent>(console);
        if (console.Comp.Account == null)
        {
            return new Dictionary<ProtoId<SWCurrencyPrototype>, float>();
        }
        return console.Comp.Account.Value.Comp.CurrentAmount;
    }
}

[ByRefEvent]
public sealed class SWGetAccountEvent : HandledEntityEventArgs
{
    public Entity<SWBankAccountContainerComponent> Account;
}

