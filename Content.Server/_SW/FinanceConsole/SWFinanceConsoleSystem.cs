using Content.Server._SW.Economy;
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
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] SharedStackSystem _stackSystem = default!;
    [Dependency] SharedHandsSystem _handsSystem = default!;
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWFinanceConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWFinanceConsoleComponent, SWTryPayBillMessage>(OnTryPay);
        SubscribeLocalEvent<SWFinanceConsoleComponent, SWTryPayoutMessage>(OnTryPayout);
        SubscribeLocalEvent<SWFinanceConsoleComponent, SWTryAddMoneyMessage>(OnAddMoney);
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

        ent.Comp.Account =
            new Entity<SWBankAccountContainerComponent>(factEnt.Value, bankComp);
        UpdateInterface(ent);
    }

    private void OnTryPay(Entity<SWFinanceConsoleComponent> ent, ref SWTryPayBillMessage args)
    {
        if (ent.Comp.Account == null)
        {
            return;
        }
        var bankComp = EnsureComp<SWBankAccountContainerComponent>(ent.Comp.Account.Value);
        var billComp = EnsureComp<SWBillContainerComponent>(ent.Comp.Account.Value);
        if (TryPay(args.Bill.CurrencyRequests, bankComp.CurrentAmount))
        {
            var a = billComp.Bills.Remove(args.Bill);
            ISawmill _s = Logger.GetSawmill("sdfgh");
            _s.Fatal(a.ToString());
            _s.Fatal((billComp.Bills.Contains(args.Bill)).ToString());
        }
        UpdateInterface(ent);
    }

    private void OnTryPayout(Entity<SWFinanceConsoleComponent> ent, ref SWTryPayoutMessage args)
    {
        if (ent.Comp.Account == null){
            return;
        }
        if (TryPay(args.Requests, ent.Comp.Account.Value.Comp.CurrentAmount))
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

    private void OnAddMoney(Entity<SWFinanceConsoleComponent> ent, ref SWTryAddMoneyMessage args)
    {
        _s.Fatal("0");
        if (ent.Comp.Account == null)
        {
            return;
        }
        _s.Fatal("01");
        if (args.Actor is not { Valid: true } user)
            return;
        _s.Fatal("1");
        foreach (var entity in _handsSystem.EnumerateHeld(user))
        {
            if (!TryComp<StackComponent>(entity, out var comp))
            {
                continue;
            }
            _s.Fatal("2");
            var proto = GetCurrencyProto(entity);
            _s.Fatal($"{proto}");
            if (proto != null)
            {
                ent.Comp.Account.Value.Comp.CurrentAmount[proto] += comp.Count;
                QueueDel(entity);
            }
        }
        UpdateInterface(ent);
    }
    private bool TryPay(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
                        Dictionary<ProtoId<SWCurrencyPrototype>, float> balance)
    {
        foreach (var (currency, amount) in requests)
        {
            if (amount > balance[currency])
            {
                return false;
            }
        }
        ReduceBalanceAmount(requests, balance);
        return true;
    }

    private void ReduceBalanceAmount(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
                                     Dictionary<ProtoId<SWCurrencyPrototype>, float> balance)
    {
        foreach (var (currency, amount) in requests)
        {
            balance[currency] -= amount;
        }
    }

    private SWCurrencyPrototype? GetCurrencyProto(EntityUid cashEntity)
    {
        var proto = Prototype(cashEntity);
        if (proto == null)
        {
            return null;
        }
        var currencies = _prototypeManager.EnumeratePrototypes <SWCurrencyPrototype>();        
        foreach (var currency in currencies)
        {
            _s.Fatal($"{currency.CashEntity} == {proto.ID}");
            if (currency.CashEntity == proto.ID)
            {
                return currency;
            }
        }
        return null;
    }
    private void UpdateInterface(Entity<SWFinanceConsoleComponent> ent)
    {
        var state = new SWFinanceConsoleBounderUserInterfaceState();
        if (TryComp<SWBillContainerComponent>(ent.Comp.Account, out var comp))
        {
            state.Bills = comp.Bills;
            _s.Fatal(comp.Bills.Count.ToString());
        }
        if (ent.Comp.Account != null)
        {
            state.CurrentAmount = ent.Comp.Account.Value.Comp.CurrentAmount;
        }

        _uiSystem.SetUiState(ent.Owner, SWFinanceConsoleUIKey.Key, state);
    }
}
