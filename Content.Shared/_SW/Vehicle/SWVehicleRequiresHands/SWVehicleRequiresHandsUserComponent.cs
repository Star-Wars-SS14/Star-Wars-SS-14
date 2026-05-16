using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle.SWVehicleRequiresHands;

[RegisterComponent]
public sealed partial class SWVehicleRequiresHandsUserComponent : Component
{
    [DataField("hands")]
    public int EmptyHandsRequiredAmount = 2;
}
