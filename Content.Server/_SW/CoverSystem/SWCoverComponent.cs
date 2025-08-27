using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.CoverSystem;
[RegisterComponent]
public sealed partial class SWCoverComponent : Component
{
    /// <summary>
    /// if distaance to cover is <= then this, then projectile will always pass cover
    /// </summary>
    [DataField]
    public float DistanceGuaranteedPass = 0.2f;

    [DataField]
    public float Step = 1f;

    [DataField]
    public float ChanceDecreasePerStep = 0.5f;

    public Dictionary<EntityUid, bool> HandledProjectiles = new Dictionary<EntityUid, bool>();
}
