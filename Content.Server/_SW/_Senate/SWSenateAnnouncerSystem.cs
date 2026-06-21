using Content.Server.Chat.Systems;

namespace Content.Server._SW._Senate;

public sealed class SWSenateAnnouncerSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;

    private static readonly Color AnnouncementColor = Color.FromHex("#9B59B6");

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateSessionStartedEvent>(OnSessionStarted);
        SubscribeLocalEvent<SWSenateBillResolvedEvent>(OnBillResolved);
        SubscribeLocalEvent<SWSenateCustomAnnouncementEvent>(OnCustom);
    }

    private void OnCustom(SWSenateCustomAnnouncementEvent ev)
    {
        _chat.DispatchGlobalAnnouncement(ev.Text, ev.Sender, colorOverride: AnnouncementColor);
    }

    private void OnSessionStarted(SWSenateSessionStartedEvent ev)
    {
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("sw-senate-announce-session", ("bill", ev.BillName)),
            Loc.GetString("sw-senate-announce-sender"),
            colorOverride: AnnouncementColor);
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
