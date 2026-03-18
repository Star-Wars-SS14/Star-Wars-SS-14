using Robust.Shared.Audio;

namespace Content.Server._SW.EndRound;

[RegisterComponent]
public sealed partial class SWEndRoundGameruleComponent : Component
{
    [DataField("delay")]
    public TimeSpan DelayBeforeFirstCheck = TimeSpan.FromMinutes(30); //0.5 часа

    [DataField("delayVote")]
    public TimeSpan DelayBeforeVote = TimeSpan.FromMinutes(30); //0.5 часа

    [DataField("delaybetweenVotes")]
    public TimeSpan DelayBetweenVotes = TimeSpan.FromMinutes(30); //0.5 часа

    public TimeSpan NextTimeStartVote = TimeSpan.FromMinutes(30);

    /// <summary>
    /// If number of alive factions members of any of these factions is 0, then end round
    /// </summary>
    [DataField]
    public List<string> Factions = new List<string>() { "RepublicFaction", "EmpireFaction" };

    [DataField]
    public LocId AnnouncementTextEndByTimer = "round-end-by-timer-msg";
    [DataField]
    public LocId AnnouncementTextEndByCommand = "round-end-by-command-msg";

    [ViewVariables]
    [DataField]
    public Color Color = Color.Gray;
}
