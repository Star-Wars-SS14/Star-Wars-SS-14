using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Politics;

[Serializable, NetSerializable]
public enum SWGlobalWarAccount : byte
{
    Treasury,
    War,
    Logistics,
    Industry,
}
