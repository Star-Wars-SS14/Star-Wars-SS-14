using Content.Server.Maps;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Server.Echo.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class SectorWarRuleComponent : Component
{
    public Dictionary<string, uint> FactionsScore = new();

    [DataField]
    public ProtoId<GameMapPrototype>[] Maps;

    [DataField]
    public List<int> AlertDelays = [90,30,15,5]; //In minutes. Left -> Right order.

    [DataField]
    public TimeSpan LastAlertTime = TimeSpan.Zero;

    [DataField]
    public string AlertSound = "/Audio/Echo/time_alert.ogg";

    [DataField]
    public string AllianceWinSound = "/Audio/Echo/alliance_won.ogg";

    [DataField]
    public string ResistanceWinSound = "/Audio/Echo/resistance_won.ogg";

    [DataField]
    public string CaptureSound = "/Audio/Echo/capture.ogg";

    [DataField]
    public int CaptureReward = 45000;

    [DataField]
    public (EntityUid Entity, AudioComponent Component)? CurrentAudio;
}
