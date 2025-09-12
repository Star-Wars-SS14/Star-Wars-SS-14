using Content.Shared._SW.Economy;
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
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        _s = Logger.GetSawmill("SWBank");
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
    public SWCurrencyPrototype? GetCurrencyProto(EntityUid cashEntity)
    {
        var proto = Prototype(cashEntity);
        if (proto == null)
        {
            return null;
        }
        var currencies = _prototypeManager.EnumeratePrototypes<SWCurrencyPrototype>();
        foreach (var currency in currencies)
        {
            //we use contains because there can be entities like
            //credits, credits10, credits100 e.t.c.
            if (proto.ID.Contains(currency.CashEntity)) 
            {
                return currency;
            }
        }
        return null;
    }
}
