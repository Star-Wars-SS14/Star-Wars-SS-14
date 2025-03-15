using Robust.Shared.Serialization;

namespace Content.Shared._SW;

[Serializable, NetSerializable]
public sealed class ReinforcementsBroadcastMessage : BoundUserInterfaceMessage
{
}

public enum StarWarsFractionType : byte
{
    Empire = 0,
    Republic = 1,
    Mandalorian = 2
}
