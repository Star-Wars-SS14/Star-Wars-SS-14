using Content.Shared._SW.Economy;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed.TypeParsers;
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
    /// <summary>
    /// Fills costs in other currencies based on exchange rates
    /// </summary>
    public void FillCurrencies(List<string> allowedCurrencies,
                        ref Dictionary<ProtoId<SWCurrencyPrototype>, float> costs)
    {
        if (costs.Count == 0 || allowedCurrencies.Count == costs.Count)
        {
            return;
        }

        SWCurrencyPrototype? currencyWithKnownAmount = null;
        float currencyKnownAmount = 0;
        List<ProtoId<SWCurrencyPrototype>> unspecifiedCurrencies
                    = new List<ProtoId<SWCurrencyPrototype>>();
        foreach (var curPrototypeId in allowedCurrencies)
        {
            if (costs.TryGetValue(curPrototypeId, out var value)
                && currencyWithKnownAmount == null)
            {
                currencyWithKnownAmount = _prototypeManager.Index<SWCurrencyPrototype>(curPrototypeId);
                currencyKnownAmount = value;
            }
            else
            {
                unspecifiedCurrencies.Add(curPrototypeId);
            }
        }

        if (currencyWithKnownAmount == null)
        {
            return;
        }

        foreach (var curPrototypeId in unspecifiedCurrencies)
        {
            var proto = _prototypeManager.Index(curPrototypeId);
            float cost = TransferCurrency(currencyKnownAmount,
                                          currencyWithKnownAmount,
                                          proto);
            costs[curPrototypeId] = cost;
        }
    }

    public void FillCurrencies(List<string> allowedCurrencies,
                       ref Dictionary<ProtoId<SWCurrencyPrototype>, int> costs)
    {
        var dict = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();
        foreach (var item in costs)
        {
            dict[item.Key] = item.Value;
        }
        FillCurrencies(allowedCurrencies, ref dict);
        foreach (var item in dict)
        {
            costs[item.Key] = (int)item.Value;
        }
    }

    public void FillCurrencies(List<ProtoId<SWCurrencyPrototype>> allowedCurrencies,
                    ref Dictionary<ProtoId<SWCurrencyPrototype>, float> costs)
    {
        var newList = allowedCurrencies.Select(c => c.Id).ToList();
        FillCurrencies(newList, ref costs);
    }

    public void ReduceBalanceAmount(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
                                     Dictionary<ProtoId<SWCurrencyPrototype>, float> balance)
    {
        foreach (var (currency, amount) in requests)
        {
            balance[currency] -= amount;
        }
    }

    /// <summary>
    /// Tries to pay all requests
    /// </summary>
    /// <param name="requests"></param>
    /// <param name="balance"></param>
    /// <returns></returns>
    public bool TryPayAll(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
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
    /// <summary>
    /// Tries to pay any of requests, considering requests as cost of one item in different currencies
    /// </summary>
    /// <param name="requests"></param>
    /// <param name="balance"></param>
    /// <returns></returns>
    public bool TryPayAny(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
                    Dictionary<ProtoId<SWCurrencyPrototype>, float> balance)
    {
        foreach (var (currency, amount) in requests)
        {
            ISawmill _s = Logger.GetSawmill("asdfghj");
            _s.Fatal($"{amount} <= {balance[currency]}");
            if (amount <= balance[currency])
            {
                var newCost = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();
                newCost[currency] = amount;
                ReduceBalanceAmount(newCost, balance);
                return true;
            }
        }

        return false;
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
