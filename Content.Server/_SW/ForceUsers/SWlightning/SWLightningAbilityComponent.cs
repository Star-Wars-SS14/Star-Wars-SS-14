using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW;

[RegisterComponent]
public sealed partial class SWLightningAbilityComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string LigtningActionPrototype = "ActionSithForceLightning";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int BoltCount = 1;

    [DataField(required: true), ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier Damage;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float StunTimeInSeconds = 3f;
}
