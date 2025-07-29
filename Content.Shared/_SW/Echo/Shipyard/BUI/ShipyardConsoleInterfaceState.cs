using Robust.Shared.Serialization;

namespace Content.Shared.Echo.Shipyard.BUI;

[NetSerializable, Serializable]
public sealed class ShipyardConsoleInterfaceState(
    int balance,
    byte uiKey,
    List<string> shipyardPrototypes,
    string shipyardName)
    : BoundUserInterfaceState
{
    public int Balance = balance;
    public readonly byte UiKey = uiKey;

    public readonly List<string> ShipyardPrototypes = shipyardPrototypes;
    public readonly string ShipyardName = shipyardName;
}
