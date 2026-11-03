using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Read-only political check for an authenticated proposed appointment; defaults to denial.</summary>
[ByRefEvent]
public record struct SWGlobalWarEvaluateAppointmentEvent(EntityUid Body, EntityUid Mind,
    SWGlobalWarFaction Faction, SWGlobalWarRole Role, Guid Generation)
{
    public bool Allowed;
}
