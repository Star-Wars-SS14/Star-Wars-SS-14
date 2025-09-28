using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Economy.ExchangeConsole;

[RegisterComponent]
public sealed partial class SWExchangeConsoleComponent : Component
{
    [DataField]
    public List<ProtoId<SWCurrencyPrototype>> AllowedCurrencies = new();
}
