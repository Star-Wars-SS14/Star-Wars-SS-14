using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Gamerules.Respawn;

[RegisterComponent]
public sealed partial class SWWaitingRespawnComponent : Component
{
    public EntityUid? RespawnActionId;
}
