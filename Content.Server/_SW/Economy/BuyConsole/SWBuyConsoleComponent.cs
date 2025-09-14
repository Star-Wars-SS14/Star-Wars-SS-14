using Content.Shared._SW.Economy;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Economy.BuyConsole;
[RegisterComponent]
public sealed partial class SWBuyConsoleComponent : Component
{
    [DataField("allowed")]
    public List<ProtoId<SWCurrencyPrototype>> AllowedCurrencies = new();

    [DataField]
    public List<ProtoId<SWCargoProductPrototype>> Products = new();
}
