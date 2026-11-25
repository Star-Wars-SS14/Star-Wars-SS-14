using Robust.Shared.GameStates;

namespace Content.Shared._SW.Item;

/// <summary>
/// Applies activation restrictions to direct item slots on footwear.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SWItemToggleNoFootwearSlotComponent : Component;
