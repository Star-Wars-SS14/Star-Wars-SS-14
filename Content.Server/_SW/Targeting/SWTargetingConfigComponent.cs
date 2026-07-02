namespace Content.Server._SW.Targeting;

[RegisterComponent]
public sealed partial class SWTargetingConfigComponent : Component
{
    [DataField]
    public Dictionary<string, bool> AllowedJobs = new();

    [DataField]
    public bool TargetUnconfiguredJobs = true;

    [DataField]
    public bool TargetHumanoidsWithoutId;

    [DataField]
    public bool TargetAnimals;
}
