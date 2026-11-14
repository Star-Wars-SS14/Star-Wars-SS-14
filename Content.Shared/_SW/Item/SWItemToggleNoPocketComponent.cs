using Robust.Shared.GameStates;

namespace Content.Shared._SW.Item;

/// <summary>
/// Prevents an item from being activated while directly inside a pocket slot.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SWItemToggleNoPocketComponent : Component;
