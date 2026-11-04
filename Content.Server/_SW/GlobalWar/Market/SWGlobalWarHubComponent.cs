using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._SW.GlobalWar.Market;

/// <summary>A prepared orbital work object, bound once to its original stationary planetary grid.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarMarketSystem))]
public sealed partial class SWGlobalWarHubComponent : Component
{
    public SWGlobalWarHubBinding? Binding;
}

public sealed record SWGlobalWarHubBinding(Guid Generation, EntityUid Planet, EntityUid Map,
    EntityCoordinates Coordinates, EntityCoordinates GridCoordinates, Angle GridRotation);
