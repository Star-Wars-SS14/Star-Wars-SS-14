using Content.Shared._SW.Economy;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Economy;

public sealed partial class SWBillContainer : Component
{
    [DataField]
    public List<SWBill> CurrentBills = new List<SWBill>();
}

[DataDefinition]
public sealed partial class SWBill
{
    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> CurrencyRequests = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();

}
