using Content.Shared._SW._Senate;

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
    public List<int> AmendmentBillIds = new();
    public int CurrentAmendmentIndex;
    public double? BallotEndHour;
    public bool IsAmendmentSession => AmendmentBillIds.Count > 0;
    public int? CurrentBillId => IsAmendmentSession
        ? CurrentAmendmentIndex < AmendmentBillIds.Count ? AmendmentBillIds[CurrentAmendmentIndex] : null
        : BillId;
    public bool ContainsBill(int billId) => IsAmendmentSession ? AmendmentBillIds.Contains(billId) : BillId == billId;
    public double StartHour;
    public double Duration;
    public SWSessionStatus Status = SWSessionStatus.Pending;
    public bool Emergency;

    public double EndHour => StartHour + Duration;
    public double? ActiveEndHour;
    public int StartSlot => Math.Max(0, (int) Math.Floor((StartHour - SWSenateTimeSystem.StartHour) * 4));
    public int SlotDuration => Math.Max(1, (int) Math.Ceiling((Math.Min(EndHour, SWSenateTimeSystem.EndHour) - SWSenateTimeSystem.StartHour) * 4) - StartSlot);
    public string TimeRange => EndHour <= SWSenateTimeSystem.EndHour
        ? $"{SWSenateDuration.FormatClock(StartHour)}–{SWSenateDuration.FormatClock(EndHour)}"
        : $"{SWSenateDuration.FormatClock(StartHour)}–след. день {SWSenateDuration.FormatClock(SWSenateTimeSystem.StartHour + EndHour - SWSenateTimeSystem.EndHour)}";
}

public sealed class SWEmergencyScheduleResult
{
    public double StartHour;
    public string? DisplacedBill;
}
