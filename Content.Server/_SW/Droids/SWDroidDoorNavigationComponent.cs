namespace Content.Server._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidDoorNavigationComponent : Component
{
    [DataField]
    public float DoorOpenRange = 1.35f;

    [DataField]
    public TimeSpan DoorOpenAttemptDelay = TimeSpan.FromSeconds(0.5);

    public TimeSpan NextDoorOpenAttempt = TimeSpan.Zero;
}
