using Robust.Shared.GameStates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.ForceAbilities;

[RegisterComponent, NetworkedComponent]
public sealed partial class SWForceUserBlockComponent : Component
{
    [ViewVariables]
    public EntityUid? CurrentForceWeapon;

    [DataField]
    public float BlockDuration = 5f;
    public TimeSpan BlockEndTime;
    [DataField]
    public float DelayBetweenBlocks = 1f;
    public TimeSpan NextReflectionAvailable;

    [ViewVariables]
    public bool IsBlocking = false;
}
