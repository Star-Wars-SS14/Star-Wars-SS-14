using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TransferToVehiclePart;

[RegisterComponent]
public sealed partial class SWTransferMindToVehiclePartComponent : Component
{
    [DataField]
    public string? PartId = null;
}
