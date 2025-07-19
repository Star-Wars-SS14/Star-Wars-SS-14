using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW;

[RegisterComponent]
public sealed partial class SWPushAbilityComponent : Component
{
    public string ActionPrototype = "ActionForcePush";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float KnockedDownDuration = 5f;
}
