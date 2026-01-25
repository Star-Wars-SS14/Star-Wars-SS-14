using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Lathe.Distributor;

[RegisterComponent]
public sealed partial class SWDistributorComponent : Component
{
    /// <summary>
    /// Maps input (fixture name) to output (Vector2 point)
    /// </summary>
    [DataField]
    public List<SWODistributorWayInfo> RedirectionInfo = default!;
}

[DataDefinition, Serializable]
public sealed partial class SWODistributorWayInfo
{
    [DataField]
    public List<Vector2> OutputPoints = default!;

    [DataField]
    public string InputFixture = default!;

    public int IndexLastPointSpawned = 0;
}
