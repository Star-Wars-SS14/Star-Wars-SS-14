using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Delegations;

[Serializable, NetSerializable]
public sealed class SWDelegationEuiState : EuiStateBase
{
    public string Text = string.Empty;
    public int Version;
    public bool CanAccept;
    public bool CanDecline;
    public bool CanEvacuate;
    public bool PlannerAvailable;
    public bool PlannerCanCreate;
    public int PlannerToday;
    public int PlannerEarliestSlot;
    public string PlannerNotice = string.Empty;
    public List<SWDelegationMeetingView> Meetings = new();
}

[Serializable, NetSerializable]
public enum SWDelegationAction : byte { Accept, Decline, Evacuate }

[Serializable, NetSerializable]
public sealed class SWDelegationActionMessage(SWDelegationAction action, int version) : EuiMessageBase
{
    public SWDelegationAction Action = action;
    public int Version = version;
}

[Serializable, NetSerializable]
public sealed class SWDelegationMeetingView
{
    public int Id, Day, Slot, Duration, Status, Revision;
    public string Subject = string.Empty;
    public string Location = string.Empty;
    public bool IsOwner, IsTarget, Stale;
}

[Serializable, NetSerializable]
public enum SWDelegationPlannerAction : byte { Create, Respond, Cancel, Reschedule }

[Serializable, NetSerializable]
public sealed class SWDelegationPlannerMessage : EuiMessageBase
{
    public SWDelegationPlannerAction Action;
    public int Version, Id, Day, Slot, Duration;
    public int ExpectedDay, ExpectedSlot, ExpectedDuration, ExpectedStatus, ExpectedRevision;
    public bool Accept;
    public string Subject = string.Empty, Description = string.Empty, Location = string.Empty;
}
