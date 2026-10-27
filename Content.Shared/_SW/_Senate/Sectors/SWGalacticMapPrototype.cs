using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._SW._Senate.Sectors;

/// <summary>Static Holonet geography. Live statistics remain in the Senate state.</summary>
[Prototype("swGalacticMap")]
public sealed partial class SWGalacticMapPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField] public Vector2 Size = new(116, 112);
    [DataField(required: true)] public List<SWGalacticMapWorld> Worlds = new();
    [DataField(required: true)] public List<SWGalacticMapRoute> Routes = new();
    [DataField] public List<SWGalacticMapRegion> Regions = new();
    [DataField] public List<SWGalacticMapCorridor> Corridors = new();
    // Scenario control zones are separate from Senate constituencies and trade routes.
    [DataField] public List<SWGalacticMapPoliticalArea> PoliticalAreas = new();
}

[DataDefinition]
public sealed partial class SWGalacticMapPoliticalArea
{
    [DataField(required: true)] public string Name = string.Empty;
    [DataField(required: true)] public string Faction = string.Empty;
    [DataField(required: true)] public List<Vector2> Boundary = new();
    [DataField] public string? Label;
    [DataField] public Vector2 LabelPosition;
    [DataField] public bool Contested;

    public bool Contains(Vector2 point)
    {
        var inside = false;
        for (var i = 0; i < Boundary.Count; i++)
        {
            var a = Boundary[i];
            var b = Boundary[(i + 1) % Boundary.Count];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}

[DataDefinition]
public sealed partial class SWGalacticMapWorld
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string Name = string.Empty;
    [DataField(required: true)] public Vector2 Position;
    // A gameplay constituency, not a claim about geographic or political ownership.
    [DataField] public ProtoId<SWSectorPrototype>? Sector;
    [DataField] public bool Anchor;
    // Small worlds share this catalogue but do not crowd the galaxy overview.
    [DataField] public bool Overview = true;
    [DataField] public Vector2? LocalPosition;
    [DataField] public string Kind = "Планета";
    [DataField] public string Description = string.Empty;
    // Explicit star-system membership, independent of the Senate constituency.
    [DataField] public string SystemId = string.Empty;
    [DataField] public string SystemName = string.Empty;
}

[DataDefinition]
public sealed partial class SWGalacticMapRoute
{
    [DataField(required: true)] public string From = string.Empty;
    [DataField(required: true)] public string To = string.Empty;
    [DataField] public string? Corridor;
    [DataField] public bool Proposed;
    [DataField] public Vector2? Control;
    [DataField] public bool Local;
}

[DataDefinition]
public sealed partial class SWGalacticMapRegion
{
    [DataField(required: true)] public string Name = string.Empty;
    [DataField] public List<ProtoId<SWSectorPrototype>> Sectors = new();
    [DataField] public List<Vector2> Boundary = new();
}

[DataDefinition]
public sealed partial class SWGalacticMapCorridor
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string Name = string.Empty;
    [DataField] public Color Color = Color.White;
}
