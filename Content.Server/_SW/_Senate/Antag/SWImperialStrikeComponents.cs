using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Server._SW._Senate.Antag;

[RegisterComponent]
public sealed partial class SWImperialStrikeMemberComponent : Component
{
    [DataField]
    public bool Commander;
}

[RegisterComponent]
public sealed partial class SWImperialStrikeSpawnComponent : Component;

[RegisterComponent]
public sealed partial class SWImperialEvacPointComponent : Component
{
    [DataField]
    public float Range = 3f;
}

[RegisterComponent]
public sealed partial class SWSenateCaptureConsoleComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan CaptureDuration = TimeSpan.FromSeconds(180);

    [ViewVariables]
    public bool Capturing;

    [ViewVariables]
    public bool ByEmpire;

    [ViewVariables]
    public TimeSpan StartTime;

    [ViewVariables]
    public bool IsTimeout;

    [ViewVariables]
    public TimeSpan TimeoutStart;
}
