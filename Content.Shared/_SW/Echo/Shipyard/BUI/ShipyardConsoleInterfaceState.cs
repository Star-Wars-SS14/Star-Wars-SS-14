using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Echo.Shipyard.BUI;

[NetSerializable, Serializable]
public sealed class ShipyardConsoleInterfaceState(
    Dictionary<ProtoId<SWCurrencyPrototype>, float> balance,
    byte uiKey,
    List<string> shipyardPrototypes,
    string shipyardName)
    : BoundUserInterfaceState
{
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> Balance = balance;
    public readonly byte UiKey = uiKey;

    public readonly List<string> ShipyardPrototypes = shipyardPrototypes;
    public readonly string ShipyardName = shipyardName;
}
