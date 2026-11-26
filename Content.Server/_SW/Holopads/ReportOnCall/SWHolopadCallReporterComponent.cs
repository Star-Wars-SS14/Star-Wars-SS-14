using Content.Shared.Radio;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using System;

namespace Content.Server._SW.Holopads.ReportOnCall;

/// <summary>
/// Sends a radio alert to a faction channel when this holopad receives a call.
/// </summary>
[RegisterComponent]
public sealed partial class SWHolopadCallReporterComponent : Component
{
    [DataField(required: true)]
    public ProtoId<RadioChannelPrototype> ChannelId;

    [DataField]
    public LocId MessageId = "sw-holopad-call-report";

    [DataField("delay")]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    public TimeSpan NextMessageAvailable;
}