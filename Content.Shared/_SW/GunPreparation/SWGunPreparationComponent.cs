using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.GunPreparation;
[RegisterComponent]
public sealed partial class SWGunPreparationComponent : Component
{
    [DataField]
    public string Action = "SWPrepareGun";

    [DataField]
    public TimeSpan PreparationTime = TimeSpan.FromSeconds(2.5);

    public bool Prepared = false;

    public EntityUid? ActionEntity = null;
    public EntityUid? SelfActionEntity = null;
}
