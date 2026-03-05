using Robust.Shared.Map;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.ForceAbilities;

[RegisterComponent]
public sealed partial class BeingMovedByForceComponent : Component
{
    public EntityUid? ForceUserMover = default!;
    public bool IsMoving = false;
    public MapCoordinates MovingTo = default!;
}
