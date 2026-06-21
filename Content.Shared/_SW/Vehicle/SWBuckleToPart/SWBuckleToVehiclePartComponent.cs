using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle.SWBuckleToPart;

[RegisterComponent]
public sealed partial class SWBuckleToVehiclePartComponent : Component
{
    [DataField]
    public string Part = "main";
}
