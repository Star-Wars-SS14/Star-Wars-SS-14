using Content.Shared.Radio;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Turrets.RepotOnDamaged;

[RegisterComponent]
public sealed partial class SWReportOnDamagedComponent : Component
{
    [DataField(required: true)]
    public ProtoId<RadioChannelPrototype> ChannelId;

    [DataField]
    public LocId MessageId = "sw-turret-under-attack";

    [DataField("delay")]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    public TimeSpan NextMessageAvailable;
}
