using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.ForceAbilities;


[RegisterComponent]
public sealed partial class ForceAbilityDistanceInteractComponent : Component
{
    //Attracting objects
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float AttractByForceSpeed = 12f;
    public EntityUid? CurrentAttractingItem;
    public bool IsAttracting = false;
    [DataField]
    public string ActionAttractPrototype = "ActionForceAttract";

    //Moving objects
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MoveByForceImpulseMultiplayer = 60f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MoveByForceMinSpeed = 2.5f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Range = 11f;
    public EntityUid? CurrentMovingItem;
    public bool IsUsingForceToMoveObject = false;
}
