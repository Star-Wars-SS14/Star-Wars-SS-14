namespace Content.Server._SW.Autobalance;

/// <summary>
/// Marks a player mob that spawned with a job counted by autobalance.
/// </summary>
[RegisterComponent, Access(typeof(SWAutobalanceGameruleSystem))]
public sealed partial class SWAutoBalanceMemberComponent : Component
{
    /// <summary>
    /// True for the first faction (limited by the ratio), false for the second faction.
    /// </summary>
    [DataField]
    public bool FirstFaction;
}
