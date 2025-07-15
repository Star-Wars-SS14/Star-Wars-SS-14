namespace Content.Shared.Echo.GameTicking.Components;

[RegisterComponent]
public sealed partial class CapturableStationComponent : Component
{
    [DataField]
    public string? Fraction = null;

    [DataField]
    public int CaptureReward = 45000;

    [DataField]
    public int CaprureRewardPerMinuteControlling = 1000;

    [DataField]
    public TimeSpan TimeOfNextPayment;
}
