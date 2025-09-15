using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy;

[NetSerializable, Serializable]
public enum SWBasePayableConsoleUIKey
{
    Key,
}

[Serializable, NetSerializable]
public class SWBasePayableBounderUserInterfaceState : BoundUserInterfaceState
{
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> CurrentAmount = new();
}

[Serializable, NetSerializable]
public sealed class SWTryPayoutMessage : BoundUserInterfaceMessage
{
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> Requests = new();
    public SWTryPayoutMessage(Dictionary<ProtoId<SWCurrencyPrototype>, float> dict)
    {
        Requests = dict;
    }
}


[Serializable, NetSerializable]
public sealed class SWTryAddMoneyMessage : BoundUserInterfaceMessage
{

}
