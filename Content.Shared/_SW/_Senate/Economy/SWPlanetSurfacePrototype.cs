using System.Collections.Generic;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>Schematic industrial region; neither a physical game map nor canonical continental geography.</summary>
[Prototype("swPlanetSurface")]
public sealed partial class SWPlanetSurfacePrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField] public int Width = 16;
    [DataField] public int Height = 12;
    [DataField(required: true)] public List<string> Terrain = new();
    [DataField(required: true)] public string Biome = string.Empty;
    [DataField(required: true)] public List<SWPlanetSurfaceSite> Sites = new();
    [DataField(required: true)] public List<SWPlanetSurfaceRoad> Roads = new();
}

[DataDefinition]
public sealed partial class SWPlanetSurfaceSite
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string Name = string.Empty;
    [DataField(required: true)] public string Kind = string.Empty;
    [DataField(required: true)] public Vector2i Cell;
    [DataField] public List<ProtoId<SWProductionRecipePrototype>> Recipes = new();
    [DataField] public ProtoId<SWProductionConditionPrototype>? Resource;
}

/// <summary>Schematic route overlay, reserved for future flow display; does not add economic capacity.</summary>
[DataDefinition]
public sealed partial class SWPlanetSurfaceRoad
{
    [DataField(required: true)] public Vector2i Cell;
    [DataField(required: true)] public string Kind = "road";
}
