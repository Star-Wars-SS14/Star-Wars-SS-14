using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Droids;

public sealed partial class SWDroidTakeCommandActionEvent : InstantActionEvent
{
}

public sealed partial class SWDroidOrderActionEvent : InstantActionEvent
{
    [DataField(required: true)]
    public SWDroidOrderType Type;
}

public sealed partial class SWDroidGoToActionEvent : WorldTargetActionEvent
{
}

public sealed partial class SWDroidAttackActionEvent : EntityTargetActionEvent
{
}

[Serializable, NetSerializable]
public enum SWDroidOrderType : byte
{
    Stay,
    Follow,
    GoTo,
    Attack
}
