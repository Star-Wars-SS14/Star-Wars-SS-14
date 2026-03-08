using Content.Shared._SW.Economy.Prototypes;
using Content.Shared._SW.TaskSystem;
using Content.Shared.EntityConditions.Conditions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed.TypeParsers;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy.BankSystem;

[Virtual]
public class SWSharedBankSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        _s = Logger.GetSawmill("SWBank");
    }    
    public bool TryTransferCurrency(float amount, SWCurrencyPrototype from, SWCurrencyPrototype to, out float result)
    {
        result = amount;
        if (from == to)
        {
            return true;
        }

        if (from.ExchangeRates.TryGetValue(to.ID, out var rate))
        {
            result *= rate;
            return true;
        }
        //_s.Error($"Unspecified exchange rate: from {from.ID} to {to.ID}");
        return false;
    }

    public Dictionary<ProtoId<SWCurrencyPrototype>, float> Sum(Dictionary<ProtoId<SWCurrencyPrototype>, float> a, Dictionary<ProtoId<SWCurrencyPrototype>, float> b)
    {
        var res = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();
        foreach (var (currency, amount) in a)
        {
            res[currency] = amount;
            if (b.TryGetValue(currency, out var b_amount))
            {
                res[currency] += b_amount;
            }
        }

        foreach (var (currency, amount) in b)
        {
            if (!res.ContainsKey(currency))
            {
                res[currency] = amount;
            }
        }

        return res;
    }

    public bool TransferCurrency(int amount, SWCurrencyPrototype from, SWCurrencyPrototype to, out int result)
    {
        float res;
        var status = TransferCurrency((float)amount, from, to, out res);
        result = (int)res;
        return status;
    }

    public bool TransferCurrency(int amount, ProtoId<SWCurrencyPrototype> from, ProtoId<SWCurrencyPrototype> to, out int result)
    {
        float res;
        var status = TransferCurrency((float)amount, from, to, out res);
        result = (int)res;
        return status;
    }

    public bool TransferCurrency(float amount, ProtoId<SWCurrencyPrototype> from, ProtoId<SWCurrencyPrototype> to, out float result)
    {
        var fromProto = _prototypeManager.Index(from);
        var toProto = _prototypeManager.Index(to);
        return TryTransferCurrency(amount, fromProto, toProto, out result);
    }
    /// <summary>
    /// Fills costs in all allowed currencies.
    /// - Keeps any explicitly specified amounts as-is.
    /// - For any allowed currency without an amount, tries to infer it
    ///   by converting from any other currency with a known amount.
    /// </summary>
    public void FillCurrencies(List<string> allowedCurrencies,
                        ref Dictionary<ProtoId<SWCurrencyPrototype>, float> costs)
    {
        if (allowedCurrencies.Count == 0)
        {
            return;
        }

        // Remember every currency that had an explicitly specified amount,
        // even if it is not in the allowed list, so we can use it as a
        // source for conversions.
        var knownCurrencies = new List<(SWCurrencyPrototype Proto, float Amount)>();
        foreach (var (currencyId, amount) in costs)
        {
            var proto = _prototypeManager.Index(currencyId);
            knownCurrencies.Add((proto, amount));
        }

        if (knownCurrencies.Count == 0)
        {
            return;
        }

        var allowedSet = new HashSet<string>(allowedCurrencies);

        // Remove currencies that are not allowed from the final result.
        var keysToRemove = costs.Keys
            .Where(id => !allowedSet.Contains(id.Id))
            .ToList();
        foreach (var key in keysToRemove)
        {
            costs.Remove(key);
        }

        // Ensure every allowed currency has an amount.
        foreach (var currencyId in allowedCurrencies)
        {
            var protoId = new ProtoId<SWCurrencyPrototype>(currencyId);

            // If an amount is already specified, do not change it.
            if (costs.ContainsKey(protoId))
            {
                continue;
            }

            if (!_prototypeManager.TryIndex(protoId, out SWCurrencyPrototype? targetProto)
                || targetProto == null)
            {
                continue;
            }

            // Try to infer the amount by converting from any known currency.
            foreach (var (sourceProto, sourceAmount) in knownCurrencies)
            {
                if (TryTransferCurrency(sourceAmount, sourceProto, targetProto, out var converted))
                {
                    costs[protoId] = converted;
                    break;
                }
            }
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
        costs.Clear();
        foreach (var item in dict)
        {
            costs[item.Key] = (int)item.Value;
        }
    }

    public void FillCurrencies(List<ProtoId<SWCurrencyPrototype>> allowedCurrencies,
                    ref Dictionary<ProtoId<SWCurrencyPrototype>, int> costs)
    {
        var newList = allowedCurrencies.Select(c => c.Id).ToList();
        FillCurrencies(newList, ref costs);
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

    public bool CheckIfCanPayAll(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests,
                        Dictionary<ProtoId<SWCurrencyPrototype>, float> balance)
    {
        foreach (var (currency, amount) in requests)
        {
            if (!balance.ContainsKey(currency))
            {
                return false;
            }
            if (amount > balance[currency])
            {
                return false;
            }
        }
        return true;
    }

    public void SendBill(Entity<SWBillContainerComponent?> account, ref SWBill bill)
    {
        if (!Resolve(account, ref account.Comp))
        {
            return;
        }

        account.Comp.Bills.Add(bill);
    }
    public bool TryPayBill(Entity<SWBankAccountContainerComponent?> ent, SWBill bill)
    {
        if (!Resolve(ent, ref ent.Comp))
        {
            return false;
        }

        var billComp = EnsureComp<SWBillContainerComponent>(ent);
        if (TryPayAll(bill.CurrencyRequests, ent.Comp.CurrentAmount))
        {
            bill.Paid = true;
            billComp.Bills.Remove(bill);
        }

        return bill.Paid;
    }

    public bool BillIsPaid(Entity<SWBillContainerComponent?> ent, SWBill bill)
    {
        if (!Resolve(ent, ref ent.Comp))
        {
            return false;
        }

        return !ent.Comp.Bills.Contains(bill);
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
        bool can = CheckIfCanPayAll(requests, balance);
        if (!can)
        {
            return false;
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
