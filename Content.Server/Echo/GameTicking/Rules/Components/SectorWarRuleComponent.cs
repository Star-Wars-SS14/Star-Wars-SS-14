using Content.Server.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server.Echo.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class SectorWarRuleComponent : Component
{
    [DataField]
    public int AllianceCredits;

    [DataField]
    public int AllianceCapturedSectors;

    [DataField]
    public int ResistanceCredits;

    [DataField]
    public int ResistanceCapturedSectors;

    [DataField]
    public ProtoId<GameMapPrototype>[] Maps;

    [DataField]
    public List<int> AlertDelays = [5,30,15,5,1]; //In minutes. Left -> Right order.

    [DataField]
    public TimeSpan LastAlertTime = TimeSpan.Zero;

    [DataField]
    public string AlertSound = "/Audio/Announcements/RoundEnd/apc_destroyed.ogg";

    [DataField]
    public string AllianceWinSound = "/Audio/Announcements/RoundEnd/apc_destroyed.ogg";

    [DataField]
    public string ResistanceWinSound = "/Audio/Announcements/RoundEnd/apc_destroyed.ogg";
}
