using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.SWShuttleSystem;

[RegisterComponent]
public sealed partial class SWTradeShuttleMarkerComponent : Component
{
    [DataField("shuttle")]
    public EntityUid Shuttle;

    [DataField("shuttlePath")] public ResPath ShuttlePath = new("/Maps/Shuttles/arrivals.yml");
}
