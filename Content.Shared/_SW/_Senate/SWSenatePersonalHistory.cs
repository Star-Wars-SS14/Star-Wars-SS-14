using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

[Serializable, NetSerializable]
public sealed class SWSenatePersonalHistoryRequest : EntityEventArgs { }

[Serializable, NetSerializable]
public sealed class SWSenatePersonalHistoryResponse : EntityEventArgs
{
    public string[] Lines = Array.Empty<string>();
    public int RoundId;
    public string Player = string.Empty;
    public SWSenatePersonalCharacter[] Characters = Array.Empty<SWSenatePersonalCharacter>();
}

[Serializable, NetSerializable]
public sealed class SWSenatePersonalCharacter
{
    public string Name = string.Empty;
    public string Role = string.Empty;
    public bool Observer;
}
