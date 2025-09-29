using Robust.Shared.GameStates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.ClothingAddCooldownOnDeactivate;
[RegisterComponent, NetworkedComponent]
public sealed partial class SWToggleClothingAddCooldownOnDeactivateComponent : Component
{
    [DataField]
    public float UseDelay = 3f;
}
