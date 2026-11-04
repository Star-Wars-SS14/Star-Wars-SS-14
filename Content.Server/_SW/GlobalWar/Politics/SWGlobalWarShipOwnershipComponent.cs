using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Explicit server ownership of a campaign ship; neither its pilot nor StationMember grants ownership.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarShipOwnershipSystem))]
public sealed partial class SWGlobalWarShipOwnershipComponent : Component
{
    /// <summary>Starting owner supplied by the ship prototype/map or its authoritative construction producer.</summary>
    [DataField]
    public SWGlobalWarFaction Owner;

    public Guid Generation;
    public long Revision;
    public Dictionary<Guid, (SWGlobalWarFaction Owner, long Revision)> Operations = new();
}
