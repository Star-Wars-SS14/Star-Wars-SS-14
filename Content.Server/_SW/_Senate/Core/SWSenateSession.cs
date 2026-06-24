namespace Content.Server._SW._Senate.Core;

public enum SWSessionStatus
{
    Pending,
    Confirmed,
    Rejected,
}

public sealed class SWSenateSession
{
    public int Id;
    public int BillId;
    public int StartHour;
    public int Duration;
    public SWSessionStatus Status = SWSessionStatus.Pending;
    public bool Emergency;

    public int EndHour => StartHour + Duration;
}

public sealed class SWEmergencyScheduleResult
{
    public int StartHour;
    public string? DisplacedBill;
}
