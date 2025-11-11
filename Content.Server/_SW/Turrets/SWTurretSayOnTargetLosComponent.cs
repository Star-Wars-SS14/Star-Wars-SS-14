using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Turrets;

[RegisterComponent]
public sealed partial class SWTurretSayOnTargetLosComponent : Component
{
    [DataField]
    public LocId MessageId = "sw-turret-say-empire";
}
