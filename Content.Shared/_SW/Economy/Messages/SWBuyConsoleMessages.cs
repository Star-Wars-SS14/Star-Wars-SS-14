using Content.Shared.Cargo.Prototypes;
using Content.Shared.Cargo;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Shared._SW.Economy.Prototypes;

namespace Content.Shared._SW.Economy.Messages;


[NetSerializable, Serializable]
public enum SWBuyConsoleUIKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWBuyConsoleBounderUserInterfaceState : SWBasePayableBounderUserInterfaceState
{
    public List<ProtoId<SWCargoProductPrototype>> Products = new();
    public List<ProtoId<SWCurrencyPrototype>> Currencies = new();
    public int MaxOrderAmount;
    public SWBuyConsoleBounderUserInterfaceState()
    {

    }
    public SWBuyConsoleBounderUserInterfaceState
        (List<ProtoId<SWCurrencyPrototype>> curs,
        List<ProtoId<SWCargoProductPrototype>> products)
    {
        Products = products;
        Currencies = curs;
    }
}

[Serializable, NetSerializable]
public sealed class SWTryPurchaseMessage : BoundUserInterfaceMessage
{
    public ProtoId<SWCargoProductPrototype> CargoProductId;
    public int Amount;

    public SWTryPurchaseMessage(ProtoId<SWCargoProductPrototype> cargoProduct, int amount)
    {
        CargoProductId = cargoProduct;
        Amount = amount;
    }
}
