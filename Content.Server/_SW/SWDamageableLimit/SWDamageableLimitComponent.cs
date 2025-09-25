using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.SWDamageableLimit;
[RegisterComponent]
public sealed partial class SWDamageableLimitComponent : Component
{
    /// <summary>
    /// Maximum damage is actually
    /// damage on dead threshold + TotalDamageToCrit
    /// </summary>
    [DataField]
    public float TotalDamageToDeadAdd = 100f;
}
