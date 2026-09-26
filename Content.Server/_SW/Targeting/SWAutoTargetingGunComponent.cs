using Content.Server.NPC;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Content.Server.NPC.Queries;

namespace Content.Server._SW.Targeting;

[RegisterComponent, Access(typeof(SWAutoTargetingGunSystem))]
public sealed partial class SWAutoTargetingGunComponent : Component
{
    /// <summary>
    /// Utility query used to select an automatic target when no forced target is set.
    /// </summary>
    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<UtilityQueryPrototype>))]
    public string UtilityQuery = "SWNearbyGunTargetsWithID";

    /// <summary>
    /// Maximum distance for automatic target search and firing.
    /// </summary>
    [DataField]
    public float Range = 10f;

    /// <summary>
    /// Delay between automatic target selection updates.
    /// </summary>
    [DataField]
    public float TargetUpdateDelay = 0.5f;

    /// <summary>
    /// Delay between line-of-sight checks for the current target.
    /// </summary>
    [DataField]
    public float LosUpdateDelay = 0.2f;

    /// <summary>
    /// Delay between shooting attempts.
    /// </summary>
    [DataField]
    public float ShootDelay = 0.2f;

    /// <summary>
    /// Whether the gun may only fire when the current target is visible.
    /// </summary>
    [DataField]
    public bool RequireLineOfSight = true;

    /// <summary>
    /// Whether line-of-sight checks should treat opaque fixtures as blockers.
    /// </summary>
    [DataField]
    public bool UseOpaqueForLineOfSight;

    /// <summary>
    /// Current target selected by the utility query or forced command target.
    /// </summary>
    public EntityUid? Target;

    /// <summary>
    /// Command target that takes priority over automatic target selection.
    /// </summary>
    public EntityUid? ForcedTarget;

    /// <summary>
    /// Accumulates time until the next automatic target selection update.
    /// </summary>
    public float TargetUpdateAccumulator;

    /// <summary>
    /// Accumulates time until the next line-of-sight check.
    /// </summary>
    public float LosUpdateAccumulator;

    /// <summary>
    /// Accumulates time until the next shooting attempt.
    /// </summary>
    public float ShootAccumulator;

    /// <summary>
    /// Cached result of the latest line-of-sight check for the current target.
    /// </summary>
    public bool TargetInLineOfSight;

    /// <summary>
    /// Temporary blackboard passed to utility queries during automatic target selection.
    /// </summary>
    public readonly NPCBlackboard Blackboard = new();
}

public sealed class SWAutoTargetingGunAttemptEvent(EntityUid target, bool forcedTarget) : CancellableEntityEventArgs
{
    public EntityUid Target = target;
    public bool ForcedTarget = forcedTarget;
}
