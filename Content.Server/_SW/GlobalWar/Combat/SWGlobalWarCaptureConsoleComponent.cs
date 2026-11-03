namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>A replaceable physical terminal for the persistent sector/location capture objective.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarCaptureSystem))]
public sealed partial class SWGlobalWarCaptureConsoleComponent : Component
{
    /// <summary>Half width of the square holding zone around the console, in metres.</summary>
    [DataField]
    public float ZoneHalfExtent = 6f;

    /// <summary>Time held by capable attackers without a defending combatant.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(300);

    /// <summary>Maximum absence before an unfinished operation is discarded.</summary>
    [DataField]
    public TimeSpan AbandonmentGrace = TimeSpan.FromSeconds(30);
}
