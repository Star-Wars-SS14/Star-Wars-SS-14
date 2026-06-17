namespace Content.Server._SW.Targeting;

[RegisterComponent]
public sealed partial class SWTargetingConfigTargetsComponent : Component
{
    [DataField]
    public bool DefaultAllowedJobs;

    [DataField]
    public bool TargetUnconfiguredJobs = true;

    public HashSet<EntityUid> Targets = new();
}
