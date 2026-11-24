using Robust.Shared.Prototypes;

namespace Content.Shared._SW.GlobalWar.Mapping;

/// <summary>Stable cross-map address, independent of runtime entity identifiers.</summary>
[DataDefinition]
public sealed partial class SWGlobalWarAnchorRef
{
    /// <summary>Sector containing the addressed object.</summary>
    [DataField(required: true)]
    public ProtoId<SWGlobalWarPlanetPrototype> Sector;

    /// <summary>Independent surface or orbit location.</summary>
    [DataField(required: true)]
    public SWGlobalWarPlanetLocation Location;

    /// <summary>Case-sensitive identifier unique within the location.</summary>
    [DataField(required: true)]
    public string AnchorId = string.Empty;
}

/// <summary>Functional purpose; markers never grant authority or create resources.</summary>
public enum SWGlobalWarAnchorKind : byte
{
    Landing,
    Dock,
    Clearance,
    Arrival,
    Capture,
    Defence,
    Storage,
    Logistics,
    FactoryArea,
    Spawn,
    Recruitment,
    Shipyard,
}
