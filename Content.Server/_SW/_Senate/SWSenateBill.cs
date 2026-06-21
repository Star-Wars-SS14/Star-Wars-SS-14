namespace Content.Server._SW._Senate;

public enum SWBillStage
{
    Voting,
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
    public SWBillStage Stage = SWBillStage.Voting;
}
