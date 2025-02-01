using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Force.Components;


[RegisterComponent]
public sealed partial class ForceAbilityDistanceInteractComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float AttractByForceSpeed = 12f;
    public EntityUid? CurrentAttractingItem;
    public bool IsAttracting = false;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MoveByForceImpulseMultiplayer = 60f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Range = 11f;
    public EntityUid? CurrentMovingItem;
    public bool IsMoving = false;
}
