using Robust.Shared.Network;

namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Marks a body as a player's current campaign life, so losing the body counts as a confirmed death.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarSpawnSystem))]
public sealed partial class SWGlobalWarLifeComponent : Component
{
    public NetUserId User;
}

/// <summary>
/// The body of a campaign life that ended by an accepted return. It stays in the world and can be healed,
/// but no mind may control it again and it holds no Force abilities.
/// </summary>
[RegisterComponent, Access(typeof(SWGlobalWarSpawnSystem))]
public sealed partial class SWGlobalWarEndedLifeComponent : Component;
