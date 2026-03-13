using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.ForceUsers.SendMessageAbility;

[RegisterComponent]
public sealed partial class SWSendMessageAbilityComponent : Component
{
    [DataField]
    public LocId MessageId = "sw-force-influence-message";

    [DataField]
    public string Action = "SWForceInfluenceAction";
}
