using Robust.Shared.GameStates;

namespace Content.Shared._SW.UserInterface;

/// <summary>
/// The faction a console belongs to. Its windows take this faction's accent, whoever opens them.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SWConsoleFactionComponent : Component
{
    /// <summary>
    /// The faction prototype ID, or null for a neutral console.
    /// </summary>
    [DataField]
    public string? Faction;
}
