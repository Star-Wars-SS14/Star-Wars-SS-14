using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Politics;

[Serializable, NetSerializable]
public enum SWGlobalWarCampaignPhase : byte
{
    Running,
    Capitulating,
    Ending,
    Stopped,
}
