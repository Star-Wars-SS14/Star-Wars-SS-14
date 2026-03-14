using Content.Shared.Alert;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW;

[Serializable, NetSerializable]
public sealed class ReinforcementsBroadcastMessage : BoundUserInterfaceMessage
{
}

public sealed partial class OpenMasteryShopAlertEvent : BaseAlertEvent;

public enum StarWarsFractionType : byte
{
    Empire = 0,
    SWRepublic = 1,
    SWMandalorian = 2
}

[ByRefEvent]
public record struct SWModifyAssignedJobsEvent
{
    public Dictionary<NetUserId, (ProtoId<JobPrototype>?, EntityUid)> AssignedJobs;
    public SWModifyAssignedJobsEvent(Dictionary<NetUserId, (ProtoId<JobPrototype>?, EntityUid)> aj)
    {
        AssignedJobs = aj;
    }
}
