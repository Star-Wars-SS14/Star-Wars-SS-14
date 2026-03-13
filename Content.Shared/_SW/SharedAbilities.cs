using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Verbs;
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

public sealed class RemoveWizardChargeEvent : EntityEventArgs
{
}

public sealed partial class ForceTryPushAbilityEvent : EntityTargetActionEvent
{

}

[Serializable, NetSerializable]
public sealed class MasteryUpEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class MasteryLightingUpgradeEvent : EntityEventArgs
{
}
public sealed class StopMovingByForceEvent : EntityEventArgs
{
    public EntityUid MoverId;
    public StopMovingByForceEvent(EntityUid moverId)
    {
        MoverId = moverId;
    }
}

public sealed partial class AttractByForceRequestedEvent : EntityTargetActionEvent
{

}

public sealed partial class SWRequestStrangulateEvent : EntityTargetActionEvent
{

}

public sealed partial class SWRequestSendMessageEvent : EntityTargetActionEvent
{

}

public record struct SWUpdateStrangulateEvent
{
    public EntityUid Entity;
    public bool IsStrangulated;
    public SWUpdateStrangulateEvent(EntityUid entity, bool isStrangulated)
    {
        Entity = entity;
        IsStrangulated = isStrangulated;
    }
}

public sealed class HitByLightningEvent : CancellableEntityEventArgs
{

}

public sealed partial class SWRequestRespawnActionEvent : InstantActionEvent { }
