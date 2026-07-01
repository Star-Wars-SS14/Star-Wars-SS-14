using Content.Shared._SW._Senate;

namespace Content.Server._SW._Senate.Core;

public enum SWBillStage
{
    Voting,
    AwaitingSignature,
    Passed,
    Rejected,
}

public sealed class SWSenateBill
{
    public int Id;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string Author = string.Empty;
    public Dictionary<string, float> Effects = new();
    public Dictionary<string, SWVoteChoice> Votes = new();
    public SWBillStage Stage = SWBillStage.Voting;
    public bool AwaitingTiebreak;
    public bool Emergency;
    public int EmergencyCrisisId;
    public float EmergencyRelief;
    public SWMotionType? SpecialMotionType;
    public int SpecialMotionLevel;
    public SWEmergencyPower SpecialMotionPower;
    public int RequiredPercent;

    // Chancellor election: multi-candidate vote, resolved separately from the For/Against pipeline.
    public bool IsElection;
    public List<string> Candidates = new();
    public Dictionary<string, string> ElectionVotes = new();
}
