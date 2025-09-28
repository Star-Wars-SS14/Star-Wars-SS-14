using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
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
public sealed partial class SWBill : IEquatable<SWBill>
{
    private static int _lastNum = 0;

    public int Id;

    [DataField]
    public string Name = "";

    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> CurrencyRequests = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();
    public SWBill()
    {
        Id = _lastNum++;
    }

    public bool Equals(SWBill? other)
    {
        if (other == null)
        {
            return false;
        }
        return Id == other.Id;
    }
}
