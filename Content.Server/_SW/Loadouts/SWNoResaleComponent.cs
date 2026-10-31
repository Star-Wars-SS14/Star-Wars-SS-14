namespace Content.Server._SW.Loadouts;

/// <summary>
/// This very item was given to a body at spawn, so it is worth nothing at a sell console. It sits on the entity and not
/// on the prototype, because the same weapon from a shop must sell as it always did.
/// </summary>
[RegisterComponent]
public sealed partial class SWNoResaleComponent : Component;
