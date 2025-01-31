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
    public float AttractByForceSpeed = 12f;
    public EntityUid? CurrentAttractingItem;
    public bool IsAttracting = false;

    [DataField]
    public float MoveByForceImpulseMultiplayer = 10f;
    public EntityUid? CurrentMovingItem;
    public bool IsMoving = false;
}
