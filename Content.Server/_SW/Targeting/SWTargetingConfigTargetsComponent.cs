namespace Content.Server._SW.Targeting;

[RegisterComponent]
public sealed partial class SWTargetingConfigTargetsComponent : Component
{
    [DataField]
    public bool DefaultAllowedJobs;

    [DataField]
    public bool TargetUnconfiguredJobs = true;

    [DataField]
    public bool TargetHumanoidsWithoutId;

    [DataField]
    public bool TargetAnimals;

    public HashSet<EntityUid> Targets = new();
}
