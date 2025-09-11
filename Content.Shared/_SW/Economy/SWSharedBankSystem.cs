using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy;

public sealed class SWSharedBankSystem : EntitySystem
{
    ISawmill _s = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] SharedStackSystem _stackSystem = default!;
    [Dependency] SharedHandsSystem _handsSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        _s = Logger.GetSawmill("SWBank");
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryPayoutMessage>(OnTryPayout);
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryAddMoneyMessage>(OnAddMoney);

    }
    private void OnTryPayout(Entity<SWBankAccountAccessorComponent> ent, ref SWTryPayoutMessage args)
    {
        if (ent.Comp.Account == null)
        {
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
            ent.Comp.AccountInterfaceRequiresUpdate = true;
        }
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
            var proto = GetCurrencyProto(entity);
            if (proto != null)
            {
                ent.Comp.Account.Value.Comp.CurrentAmount[proto] += comp.Count;
                QueueDel(entity);
            }
        }
        ent.Comp.AccountInterfaceRequiresUpdate = true;
    }
    public float TransferCurrency(float amount, SWCurrencyPrototype from, SWCurrencyPrototype to)
    {
        if (from.ExchangeRates.TryGetValue(to.ID, out var rate))
        {
            return amount * rate;
        }
        _s.Error($"Unspecified exchange rate: from {from.ID} to {to.ID}");
        return amount;
    }

    public void ReduceBalanceAmount(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
                                     Dictionary<ProtoId<SWCurrencyPrototype>, float> balance)
    {
        foreach (var (currency, amount) in requests)
        {
            balance[currency] -= amount;
        }
    }

    public bool TryPay(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
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

    private SWCurrencyPrototype? GetCurrencyProto(EntityUid cashEntity)
    {
        var proto = Prototype(cashEntity);
        if (proto == null)
        {
            return null;
        }
        var currencies = _prototypeManager.EnumeratePrototypes<SWCurrencyPrototype>();
        foreach (var currency in currencies)
        {
            if (currency.CashEntity == proto.ID)
            {
                return currency;
            }
        }
        return null;
    }
}
