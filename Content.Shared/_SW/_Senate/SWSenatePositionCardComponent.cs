using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

[Serializable, NetSerializable]
public enum SWSenatePosition : byte
{
    Senator,
    Chancellor,
    Speaker,
    Minister,
    ChiefJustice,
    Judge,
    Prosecutor,
    Defender,
}

[RegisterComponent]
public sealed partial class SWSenatePositionCardComponent : Component
{
    [DataField]
    public SWSenatePosition Position = SWSenatePosition.Senator;
}
