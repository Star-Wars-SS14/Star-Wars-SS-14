using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.SWSpawnRandomRule;
[RegisterComponent]
public sealed partial class SWSpawnRandomGameruleComponent : Component
{
    [DataField]
    public string Spawner = default!;

    [DataField]
    public int Count = 1;
}
