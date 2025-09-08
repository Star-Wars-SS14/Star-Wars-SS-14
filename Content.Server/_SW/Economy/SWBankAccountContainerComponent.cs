using Content.Shared._SW.Economy;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Economy;

[RegisterComponent]
public sealed partial class SWBankAccountContainerComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> CurrentAmount = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();
}
