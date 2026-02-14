using Content.Shared.Whitelist;
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

    /// <summary>
    /// We use this blacklist to check if we can spawn new entity. So, entities that have been already spawned should fail it
    /// </summary>
    [DataField]
    public EntityWhitelist Blacklist = default!;
}
