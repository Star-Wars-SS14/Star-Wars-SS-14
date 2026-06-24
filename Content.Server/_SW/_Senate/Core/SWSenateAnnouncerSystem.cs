using Content.Server.Chat.Systems;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateAnnouncerSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;

    private static readonly Color AnnouncementColor = Color.FromHex("#9B59B6");
    private static readonly Color EmergencyColor = Color.FromHex("#C0392B");

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateSessionStartedEvent>(OnSessionStarted);
        SubscribeLocalEvent<SWSenateEmergencySessionScheduledEvent>(OnEmergencyScheduled);
        SubscribeLocalEvent<SWSenateBillResolvedEvent>(OnBillResolved);
        SubscribeLocalEvent<SWSenateCustomAnnouncementEvent>(OnCustom);
    }

    private void OnEmergencyScheduled(SWSenateEmergencySessionScheduledEvent ev)
    {
        var text = Loc.GetString("sw-senate-announce-emergency-scheduled",
            ("time", $"{ev.StartHour:00}:00"), ("bill", ev.BillName));
        if (ev.DisplacedBill != null)
            text += " " + Loc.GetString("sw-senate-announce-emergency-replaced", ("bill", ev.DisplacedBill));

        _chat.DispatchGlobalAnnouncement(text, Loc.GetString("sw-senate-announce-sender"), colorOverride: EmergencyColor);
    }

    private void OnCustom(SWSenateCustomAnnouncementEvent ev)
    {
        _chat.DispatchGlobalAnnouncement(ev.Text, ev.Sender, colorOverride: AnnouncementColor);
    }

    private void OnSessionStarted(SWSenateSessionStartedEvent ev)
    {
        var key = ev.Emergency ? "sw-senate-announce-emergency-started" : "sw-senate-announce-session";
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(key, ("bill", ev.BillName)),
            Loc.GetString("sw-senate-announce-sender"),
            colorOverride: ev.Emergency ? EmergencyColor : AnnouncementColor);
    }

    private void OnBillResolved(SWSenateBillResolvedEvent ev)
    {
        var key = ev.Passed ? "sw-senate-announce-passed" : "sw-senate-announce-rejected";
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(key, ("bill", ev.BillName)),
            Loc.GetString("sw-senate-announce-sender"),
            colorOverride: AnnouncementColor);
    }
}
