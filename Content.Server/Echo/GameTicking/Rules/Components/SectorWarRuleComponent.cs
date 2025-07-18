using Content.Server.Maps;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;
using System.Numerics;

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
    public string AllianceWinSound = "/Audio/Echo/alliance_won.ogg";

    [DataField]
    public string ResistanceWinSound = "/Audio/Echo/resistance_won.ogg";

    [DataField]
    public string? CaptureSound = null; //"/Audio/Echo/capture.ogg";

    [DataField]
    public int StartBalance = 100000;

    [DataField]
    public (EntityUid Entity, AudioComponent Component)? CurrentAudio;

    [DataField]
    public LocId AnnouncementTextHoldRepublic = "announce-hold-republic";

    [DataField]
    public LocId AnnouncementTextHoldEmpire = "announce-hold-empire";

    [DataField]
    public SoundSpecifier AnnouncementSoundHold = default!;

    [DataField]
    public float AnnouncementTimeHold = 120;

    [DataField]
    public LocId AnnouncementTextAttackPartiallyRepublic = "announce-partially-republic";

    [DataField]
    public LocId AnnouncementTextAttackPartiallyEmpire = "announce-partially-empire";

    [DataField]
    public SoundSpecifier? AnnouncementSoundAttackPartially;

    [DataField]
    public float AnnouncementTimeAttackPartially = 18 * 60 + 120;

    [DataField]
    public LocId AnnouncementTextAttackFullRepublic = "announce-full-republic";

    [DataField]
    public LocId AnnouncementTextAttackFullEmpire = "announce-full-empire";

    [DataField]
    public SoundSpecifier? AnnouncementSoundAttackFull;

    [DataField]
    public float AnnouncementTimeAttackFull = 18 * 60 + 120 + 25 * 60;

}
