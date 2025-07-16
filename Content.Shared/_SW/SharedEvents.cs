using Content.Shared.Alert;
using Robust.Shared.Serialization;

namespace Content.Shared._SW;

[Serializable, NetSerializable]
public sealed class ReinforcementsBroadcastMessage : BoundUserInterfaceMessage
{
}

public sealed partial class OpenMasteryShopAlertEvent : BaseAlertEvent;

public enum StarWarsFractionType : byte
{
    Empire = 0,
    SWRepublic = 1,
    SWMandalorian = 2
}
