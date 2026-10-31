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
}
