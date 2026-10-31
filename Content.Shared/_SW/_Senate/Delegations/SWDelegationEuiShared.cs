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
}

[Serializable, NetSerializable]
public enum SWDelegationAction : byte { Accept, Decline, Evacuate }

[Serializable, NetSerializable]
public sealed class SWDelegationActionMessage(SWDelegationAction action, int version) : EuiMessageBase
{
    public SWDelegationAction Action = action;
    public int Version = version;
}
