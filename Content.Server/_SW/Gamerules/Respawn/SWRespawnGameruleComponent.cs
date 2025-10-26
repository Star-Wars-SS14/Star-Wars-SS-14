using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Gamerules.Respawn;

[RegisterComponent]
public sealed partial class SWRespawnGameruleComponent : Component
{
    [DataField("delay")]
    public float DelayBeforeCanRespawn = 300f;

    [DataField]
    public LocId WaitMessage = "sw-wait-respawn";

    [DataField]
    public string RespawnActionId = "SWRespawnAction";
}
