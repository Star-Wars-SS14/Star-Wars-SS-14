using Robust.Shared.Audio;

namespace Content.Server._SW.EndRound;

[RegisterComponent]
public sealed partial class EndRoundByTimerComponent : Component
{
    [DataField("DelayBeforeEndInSeconds")]
    public float DelayBeforeEndInSeconds = 2 * 60 * 60; //2 часа

    [DataField]
    public LocId AnnouncementTextEndByTimer = "round-end-by-timer-msg";
    [DataField]
    public LocId AnnouncementTextEndByCommand = "round-end-by-command-msg";

    [ViewVariables]
    [DataField]
    public Color Color = Color.Gray;
}
