using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._SW;

[Serializable, NetSerializable]
public sealed partial class SWForceInfluenceDoAfterEvent : SimpleDoAfterEvent;
