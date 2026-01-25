using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Gamerules.RefillOre;

[RegisterComponent]
public sealed partial class SWRespawnGameruleSpawnpointComponent : Component
{
    [DataField(required: true)]
    public string RespawnProto;
}
