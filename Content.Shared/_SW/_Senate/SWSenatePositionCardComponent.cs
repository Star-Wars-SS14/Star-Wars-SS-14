using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

[Serializable, NetSerializable]
public enum SWSenatePosition : byte
{
    Senator,
    Chancellor,
    Speaker,
    Minister,
}

[RegisterComponent]
public sealed partial class SWSenatePositionCardComponent : Component
{
    [DataField]
    public SWSenatePosition Position = SWSenatePosition.Senator;
}
