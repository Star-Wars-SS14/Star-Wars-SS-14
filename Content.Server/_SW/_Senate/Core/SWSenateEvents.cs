namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateSessionStartedEvent : EntityEventArgs
{
    public string BillName;
    public int StartHour;
    public int EndHour;

    public SWSenateSessionStartedEvent(string billName, int startHour, int endHour)
    {
        BillName = billName;
        StartHour = startHour;
        EndHour = endHour;
    }
}

public sealed class SWSenateBillResolvedEvent : EntityEventArgs
{
    public string BillName;
    public bool Passed;

    public SWSenateBillResolvedEvent(string billName, bool passed)
    {
        BillName = billName;
        Passed = passed;
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
