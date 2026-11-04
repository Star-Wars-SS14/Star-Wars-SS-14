using Robust.Shared.GameStates;

namespace Content.Shared._SW.GlobalWar.Access;

/// <summary>Public marker: cross-map portal movement waits for authoritative policy, without exposing permissions.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SWGlobalWarGuardedMapComponent : Component;
