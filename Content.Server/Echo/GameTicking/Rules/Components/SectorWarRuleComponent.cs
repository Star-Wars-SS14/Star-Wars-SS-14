using Content.Server.Maps;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;
using System.Numerics;
using static Content.Shared.Administration.AdminAnnounceEuiMsg;

namespace Content.Server.Echo.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class SectorWarRuleComponent : Component
{
    public Dictionary<string, uint> FactionsScore = new();

    public Dictionary<string, int> FactionMoney = new();

    [DataField]
    public List<int> AlertDelays = [90, 30, 15, 5]; //In minutes. Left -> Right order.

    [DataField]
    public TimeSpan LastAlertTime = TimeSpan.Zero;

    [DataField]
    public string AlertSound = "/Audio/Echo/time_alert.ogg";

    [DataField]
    public string? CaptureSound = null; //"/Audio/Echo/capture.ogg";

    [DataField]
    public int StartBalance = 100000;

    [DataField]
    public (EntityUid Entity, AudioComponent Component)? CurrentAudio;

    [DataField]
    public Dictionary<SWSpaceWarGameState, LocId> RepublicAnnouncementTexts = new Dictionary<SWSpaceWarGameState, LocId>()
    {
        { SWSpaceWarGameState.Holding, "announce-hold-republic" },
        { SWSpaceWarGameState.AttackingPartially, "announce-partially-republic"},
        { SWSpaceWarGameState.AttackingFull, "announce-full-republic"}
    };

    [DataField]
    public Dictionary<SWSpaceWarGameState, LocId> EmpireAnnouncementTexts = new Dictionary<SWSpaceWarGameState, LocId>()
    {
        {SWSpaceWarGameState.Holding, "announce-hold-empire"},
        {SWSpaceWarGameState.AttackingPartially, "announce-partially-empire" },
        {SWSpaceWarGameState.AttackingFull,  "announce-full-empire"}
    };

    [DataField]
    public Dictionary<SWSpaceWarGameState, SoundSpecifier?> AnnouncementSounds = new Dictionary<SWSpaceWarGameState, SoundSpecifier?>()
    {
        {SWSpaceWarGameState.Holding, new SoundPathSpecifier("/Audio/_SW/_spaceWar/radar.ogg") },
        {SWSpaceWarGameState.AttackingPartially, new SoundPathSpecifier("/Audio/_SW/_spaceWar/start_mission.ogg")},
        {SWSpaceWarGameState.AttackingFull, new SoundPathSpecifier("/Audio/_SW/_spaceWar/start_engine.ogg")}
    };

    public SWSpaceWarGameState CurState = SWSpaceWarGameState.Preholding;

    [DataField]
    //Shows when the staet ENDS,
    //so if StatesEndTimes[<state>] = <number>, it means that when <number> of seconds passed from roundstart
    //state <state> should end and we go to the next state
    public Dictionary<SWSpaceWarGameState, float> StatesEndTimes = new Dictionary<SWSpaceWarGameState, float>(){
        {SWSpaceWarGameState.Preholding, 120},
        {SWSpaceWarGameState.Holding, 18*60 + 120}, //120
        {SWSpaceWarGameState.AttackingPartially,  18*60 + 120 + 25*60}, //18 * 60 + 120
        {SWSpaceWarGameState.AttackingFull,  1}
        //18 * 60 + 120 + 25 * 60
    };

    [DataField]
    public LocId RepublicSender = "republic-spacewar-sender";

    [DataField]
    public LocId EmpireSender = "empire-spacewar-sender";

    [DataField]
    public Color EmpireAnnouncementColor = Color.DarkRed;

    [DataField]
    public Color RepublicAnnouncementColor = Color.Aqua;
}

public enum SWSpaceWarGameState : byte
{
    Preholding,
    Holding,
    AttackingPartially,
    AttackingFull
}
