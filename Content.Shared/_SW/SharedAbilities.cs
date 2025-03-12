using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._SW;

public sealed partial class DashAbilityEvent : InstantActionEvent
{
}

public sealed partial class ForceLightningAbilityEvent : EntityTargetActionEvent
{
}

public sealed partial class BlockEvent : InstantActionEvent
{
}

public sealed partial class SubdueAbilityEvent : InstantActionEvent
{
}

public sealed partial class SaberPrivateAbilityEvent : InstantActionEvent
{
}

public sealed partial class CallSaberAbilityEvent : InstantActionEvent
{
}

public sealed partial class JediForceSaberAttackEvent : EntityTargetActionEvent
{
}

public sealed partial class SithGaspAbilityEvent : EntityTargetActionEvent
{
}

[Serializable, NetSerializable]
public sealed partial class MasteryUpEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed partial class MasteryLightingUpgradeEvent : EntityEventArgs
{
}
