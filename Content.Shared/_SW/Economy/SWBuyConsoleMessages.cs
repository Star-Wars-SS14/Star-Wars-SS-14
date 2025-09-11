using Content.Shared.Cargo.Prototypes;
using Content.Shared.Cargo;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy;


[NetSerializable, Serializable]
public enum SWBuyConsoleUIKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWBuyConsoleBounderUserInterfaceState : BoundUserInterfaceState
{
    public string Name = "";
    public int Count;
    public List<CargoOrderData> Orders = new();
    public List<ProtoId<SWCargoProductPrototype>> Products = new();

    public SWBuyConsoleBounderUserInterfaceState(string name, int count, List<CargoOrderData> orders, List<ProtoId<SWCargoProductPrototype>> products)
    {
        Name = name;
        Count = count;
        Orders = orders;
        Products = products;
    }
}

[Serializable, NetSerializable]
public sealed class SWTryAddOrderMessage : BoundUserInterfaceMessage
{
    public string Requester;
    public string Reason;
    public string CargoProductId;
    public int Amount;

    public SWTryAddOrderMessage(string requester, string reason, string cargoProductId, int amount)
    {
        Requester = requester;
        Reason = reason;
        CargoProductId = cargoProductId;
        Amount = amount;
    }
}
