using Content.Shared._SW._Senate;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateSessionStartedEvent : EntityEventArgs
{
    public string BillName;
    public double StartHour;
    public double EndHour;
    public bool Emergency;

    public SWSenateSessionStartedEvent(string billName, double startHour, double endHour, bool emergency = false)
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
    public double StartHour;
    public string? DisplacedBill;

    public SWSenateEmergencySessionScheduledEvent(string billName, double startHour, string? displacedBill)
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

public sealed class SWSenateBillVotesResolvedEvent : EntityEventArgs
{
    public int BillId;
    public string BillName;
    public bool Passed;
    public Dictionary<string, SWVoteChoice> Votes;

    public SWSenateBillVotesResolvedEvent(int billId, string billName, bool passed, Dictionary<string, SWVoteChoice> votes)
    {
        BillId = billId;
        BillName = billName;
        Passed = passed;
        Votes = votes;
    }
}

public sealed class SWSenateEmergencyMotionPassedEvent : EntityEventArgs
{
    public SWMotionType Type;
    public int Level;
    public SWEmergencyPower Power;

    public SWSenateEmergencyMotionPassedEvent(SWMotionType type, int level, SWEmergencyPower power)
    {
        Type = type;
        Level = level;
        Power = power;
    }
}

public sealed class SWSenatePartyWhipAnnouncedEvent : EntityEventArgs
{
    public string PartyName;
    public string BillName;
    public string ChoiceLabel;

    public SWSenatePartyWhipAnnouncedEvent(string partyName, string billName, string choiceLabel)
    {
        PartyName = partyName;
        BillName = billName;
        ChoiceLabel = choiceLabel;
    }
}

public enum SWSenateAnnouncementKind : byte
{
    Notice,
    Defense,
    Security,
    Health,
    Economy,
    Finance,
    Justice,
    Chancellor,
    Scandal,
}

public sealed class SWSenateCustomAnnouncementEvent : EntityEventArgs
{
    public string Sender;
    public string Text;
    public SWSenateAnnouncementKind Kind;

    public SWSenateCustomAnnouncementEvent(string sender, string text, SWSenateAnnouncementKind kind = SWSenateAnnouncementKind.Notice)
    {
        Sender = sender;
        Text = text;
        Kind = kind;
    }
}

public sealed class SWSenateDayStartedEvent : EntityEventArgs { }

public sealed class SWSenateDayClosedEvent(bool finalDay) : EntityEventArgs
{
    public bool FinalDay { get; } = finalDay;
}
