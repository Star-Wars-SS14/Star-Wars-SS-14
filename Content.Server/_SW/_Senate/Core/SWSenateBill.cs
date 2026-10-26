using Content.Shared._SW._Senate;

namespace Content.Server._SW._Senate.Core;

public enum SWBillStage
{
    Voting,
    InCommittee,
    Returned,
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
    // Amendments are independent bills; their effects are deltas to this enacted root law.
    public int? AmendsLawId;
    public Dictionary<string, float> Effects = new();
    public Dictionary<string, SWVoteChoice> Votes = new();
    public SWBillStage Stage = SWBillStage.Voting;
    public string? CommitteeId;
    public string? CommitteeName;
    public int CommitteeDay;
    public int SendBacks;
    public string? ReturnReason;
    public bool Repealed;
    public bool AwaitingTiebreak;
    public bool Emergency;
    public int EmergencyCrisisId;
    public float EmergencyRelief;
    public float EmergencyWaitHours;
    public SWMotionType? SpecialMotionType;
    public int SpecialMotionLevel;
    public SWEmergencyPower SpecialMotionPower;
    public int RequiredPercent;

    public bool IsElection;
    public List<string> Candidates = new();
    public Dictionary<string, string> ElectionVotes = new();
}
