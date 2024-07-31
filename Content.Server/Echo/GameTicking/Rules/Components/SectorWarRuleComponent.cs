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
}
