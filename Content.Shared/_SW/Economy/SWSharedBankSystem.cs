using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy;

public sealed class SWSharedBankSystem : EntitySystem
{
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
}
