using Robust.Shared.GameStates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.DeactivateOnShoot;
[RegisterComponent,  NetworkedComponent]
public sealed partial class SWDeactivateOnShootUserComponent : Component
{
    public EntityUid? Item = null;
}
