namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateSessionStartedEvent : EntityEventArgs
{
    public string BillName;
    public int StartHour;
    public int EndHour;
    public bool Emergency;

    public SWSenateSessionStartedEvent(string billName, int startHour, int endHour, bool emergency = false)
    {
        BillName = billName;
        StartHour = startHour;
        EndHour = endHour;
        Emergency = emergency;
    }
}

public sealed class SWSenateEmergencySessionScheduledEvent : EntityEventArgs
{
    public string BillName;
    public int StartHour;
    public string? DisplacedBill;

    public SWSenateEmergencySessionScheduledEvent(string billName, int startHour, string? displacedBill)
    {
        BillName = billName;
        StartHour = startHour;
        DisplacedBill = displacedBill;
    }
}

public sealed class SWSenateBillResolvedEvent : EntityEventArgs
{
    public string BillName;
    public bool Passed;
    public int EmergencyCrisisId;
    public float EmergencyRelief;

    public SWSenateBillResolvedEvent(string billName, bool passed, int emergencyCrisisId = 0, float emergencyRelief = 0f)
    {
        BillName = billName;
        Passed = passed;
        EmergencyCrisisId = emergencyCrisisId;
        EmergencyRelief = emergencyRelief;
    }
}

public sealed class SWSenateCustomAnnouncementEvent : EntityEventArgs
{
    public string Sender;
    public string Text;

    public SWSenateCustomAnnouncementEvent(string sender, string text)
    {
        Sender = sender;
        Text = text;
    }
}
