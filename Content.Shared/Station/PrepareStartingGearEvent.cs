using Content.Shared.Roles;

namespace Content.Shared.Station;

/// <summary>Allows server systems to prepare a private snapshot for this equip operation.</summary>
[ByRefEvent]
public record struct PrepareStartingGearEvent(EntityUid Entity, IEquipmentLoadout Gear)
{
    public EntityUid? Saber;
}

/// <summary>Places a prepared starting saber after the other equipment has been equipped.</summary>
[ByRefEvent]
public record struct CompleteStartingSaberEvent(EntityUid Saber);
