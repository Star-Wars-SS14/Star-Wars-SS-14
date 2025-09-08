using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy;
[Prototype("swcurrency")]
public sealed partial class SWCurrencyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    /// <summary>
    /// How much 1 unit of this currency is in other currencies
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> ExchangeRates = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();

    [DataField]
    public LocId LocalizedName = "money";

    [DataField]
    public LocId ShortName = "$";
}
