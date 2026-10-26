using Content.Server.Chat.Systems;
using Content.Shared._SW._Senate;
using Robust.Shared.Audio;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateAnnouncerSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private static readonly Color AnnouncementColor = Color.FromHex("#9B59B6");
    private static readonly Color EmergencyColor = Color.FromHex("#C0392B");
    private static readonly SoundSpecifier NoticeSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_notice.wav");
    private static readonly SoundSpecifier SessionStartedSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_session_started.wav");
    private static readonly SoundSpecifier EmergencyScheduledSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_emergency_scheduled.wav");
    private static readonly SoundSpecifier EmergencyStartedSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_emergency_started.wav");
    private static readonly SoundSpecifier BillPassedSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_bill_passed.wav");
    private static readonly SoundSpecifier BillRejectedSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_bill_rejected.wav");
    private static readonly SoundSpecifier PartyWhipSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_party_whip.wav");
    private static readonly SoundSpecifier DefenseSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_ministry_defense.wav");
    private static readonly SoundSpecifier SecuritySound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_ministry_security.wav");
    private static readonly SoundSpecifier HealthSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_ministry_health.wav");
    private static readonly SoundSpecifier EconomySound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_ministry_economy.wav");
    private static readonly SoundSpecifier FinanceSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_ministry_finance.wav");
    private static readonly SoundSpecifier JusticeSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_ministry_justice.wav");
    private static readonly SoundSpecifier ChancellorSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_chancellor_action.wav");
    private static readonly SoundSpecifier ScandalSound = new SoundPathSpecifier("/Audio/_SW/_Senate/Announcements/senate_voice_scandal.wav");

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateSessionStartedEvent>(OnSessionStarted);
        SubscribeLocalEvent<SWSenateEmergencySessionScheduledEvent>(OnEmergencyScheduled);
        SubscribeLocalEvent<SWSenateBillResolvedEvent>(OnBillResolved);
        SubscribeLocalEvent<SWSenatePartyWhipAnnouncedEvent>(OnPartyWhip);
        SubscribeLocalEvent<SWSenateCustomAnnouncementEvent>(OnCustom);
    }

    private void OnPartyWhip(SWSenatePartyWhipAnnouncedEvent ev)
    {
        if (!_time.ModeActive)
            return;

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("sw-senate-announce-party-whip",
                ("party", ev.PartyName), ("bill", ev.BillName), ("choice", ev.ChoiceLabel)),
            Loc.GetString("sw-senate-announce-sender"),
            announcementSound: PartyWhipSound,
            colorOverride: AnnouncementColor);
    }

    private void OnEmergencyScheduled(SWSenateEmergencySessionScheduledEvent ev)
    {
        if (!_time.ModeActive)
            return;

        var text = Loc.GetString("sw-senate-announce-emergency-scheduled",
            ("time", SWSenateDuration.FormatClock(ev.StartHour)), ("bill", ev.BillName));
        if (ev.DisplacedBill != null)
            text += " " + Loc.GetString("sw-senate-announce-emergency-replaced", ("bill", ev.DisplacedBill));

        _chat.DispatchGlobalAnnouncement(
            text,
            Loc.GetString("sw-senate-announce-sender"),
            announcementSound: EmergencyScheduledSound,
            colorOverride: EmergencyColor);
    }

    private void OnCustom(SWSenateCustomAnnouncementEvent ev)
    {
        if (!_time.ModeActive)
            return;

        _chat.DispatchGlobalAnnouncement(
            ev.Text,
            ev.Sender,
            announcementSound: SoundFor(ev.Kind),
            colorOverride: AnnouncementColor);
    }

    private void OnSessionStarted(SWSenateSessionStartedEvent ev)
    {
        if (!_time.ModeActive)
            return;

        var key = ev.Emergency ? "sw-senate-announce-emergency-started" : "sw-senate-announce-session";
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(key, ("bill", ev.BillName)),
            Loc.GetString("sw-senate-announce-sender"),
            announcementSound: ev.Emergency ? EmergencyStartedSound : SessionStartedSound,
            colorOverride: ev.Emergency ? EmergencyColor : AnnouncementColor);
    }

    private void OnBillResolved(SWSenateBillResolvedEvent ev)
    {
        if (!_time.ModeActive)
            return;

        var key = ev.Passed ? "sw-senate-announce-passed" : "sw-senate-announce-rejected";
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(key, ("bill", ev.BillName)),
            Loc.GetString("sw-senate-announce-sender"),
            announcementSound: ev.Passed ? BillPassedSound : BillRejectedSound,
            colorOverride: AnnouncementColor);
    }

    private static SoundSpecifier SoundFor(SWSenateAnnouncementKind kind) => kind switch
    {
        SWSenateAnnouncementKind.Defense => DefenseSound,
        SWSenateAnnouncementKind.Security => SecuritySound,
        SWSenateAnnouncementKind.Health => HealthSound,
        SWSenateAnnouncementKind.Economy => EconomySound,
        SWSenateAnnouncementKind.Finance => FinanceSound,
        SWSenateAnnouncementKind.Justice => JusticeSound,
        SWSenateAnnouncementKind.Chancellor => ChancellorSound,
        SWSenateAnnouncementKind.Scandal => ScandalSound,
        _ => NoticeSound,
    };
}
