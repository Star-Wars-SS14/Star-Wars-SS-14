using Robust.Shared.Audio;

namespace Content.Server._SW.EndRound;

[RegisterComponent]
public sealed partial class EndRoundByTimerComponent : Component
{
    [DataField("DelayBeforeEndInSeconds", required: true)]
    public float DelayBeforeEndInSeconds = 2 * 60 * 60; //2 часа

    [DataField]
    public LocId AnnouncementTextEndByTimer = "round-end-by-timer-msg";
    [DataField]
    public LocId AnnouncementTextEndByCommand = "round-end-by-command-msg";

    //[DataField]
    //public LocId Title = "round-end-title";

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public SoundSpecifier Sound = default!;

    [ViewVariables]
    [DataField]
    public Color Color = Color.Gray;
}
