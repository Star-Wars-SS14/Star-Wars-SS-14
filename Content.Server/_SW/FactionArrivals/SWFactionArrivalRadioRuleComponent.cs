using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.FactionArrivals;

[RegisterComponent]
public sealed partial class SWFactionArrivalRadioRuleComponent : Component
{
    [DataField(required: true)]
    public List<SWFactionArrivalRadioAnnouncement> Announcements = new();

    [DataField]
    public bool SkipSilent = true;
}

[DataDefinition]
public sealed partial class SWFactionArrivalRadioAnnouncement
{
    [DataField(required: true)]
    public string Faction = string.Empty;

    [DataField(required: true)]
    public ProtoId<RadioChannelPrototype> Channel;

    [DataField]
    public LocId Phrase = "sw-faction-arrival-radio";
}
