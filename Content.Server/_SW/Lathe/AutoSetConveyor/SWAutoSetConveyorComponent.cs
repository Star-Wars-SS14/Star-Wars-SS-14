using Content.Shared.Conveyor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Lathe.AutoSetConveyor;

[RegisterComponent]
public sealed partial class SWAutoSetConveyorComponent : Component
{
    [DataField]
    public ConveyorState State = ConveyorState.Forward;
}
