using Content.Shared._SW.GlobalWar.Mapping;
using Robust.Shared.Maths;

namespace Content.Server._SW.GlobalWar.Mapping;

/// <summary>Address attached to the actual device, or to a noninteractive editor marker.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarMapAnchorSystem))]
public sealed partial class SWGlobalWarMapAnchorComponent : Component
{
    /// <summary>Unique identifier within this physical location.</summary>
    [DataField]
    public string AnchorId = string.Empty;

    /// <summary>Purpose used to validate links and choose prepared destinations.</summary>
    [DataField]
    public SWGlobalWarAnchorKind Kind;

    /// <summary>Explicit named links, such as peer, storage, dock and clearance.</summary>
    [DataField]
    public Dictionary<string, SWGlobalWarAnchorRef> Links = new();

    /// <summary>Inclusive minimum cell for an area, relative to this marker's parent.</summary>
    [DataField]
    public Vector2i Min;

    /// <summary>Inclusive maximum cell for an area, relative to this marker's parent.</summary>
    [DataField]
    public Vector2i Max;

    /// <summary>Prepared spawn search radius in grid cells; zero permits only the marker's point, maximum eight.</summary>
    [DataField]
    public int SpawnRadius = 4;

    /// <summary>First registered physical map; moving a device cannot reassign its strategic target.</summary>
    public EntityUid? RegisteredMap;
}
