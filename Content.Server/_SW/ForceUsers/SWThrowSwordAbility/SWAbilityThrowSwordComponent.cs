using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW;

[RegisterComponent]
public sealed partial class SWAbilityThrowSwordComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ThrowForce = 10f;
}
