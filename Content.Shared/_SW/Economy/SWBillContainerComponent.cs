using Content.Shared._SW.Economy;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy;
[RegisterComponent]
public sealed partial class SWBillContainerComponent : Component
{
    [DataField]
    public List<SWBill> Bills = new List<SWBill>();
}

[DataDefinition, Serializable]
public sealed partial class SWBill
{
    [DataField]
    public string Name = "";

    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> CurrencyRequests = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();

}
