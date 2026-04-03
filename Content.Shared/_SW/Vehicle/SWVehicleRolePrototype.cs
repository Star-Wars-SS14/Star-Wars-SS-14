using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;

[Prototype("swVehicleRole")]
public sealed class SWVehicleRolePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public LocId LocalizedName = "sw-base-role-name";

    [DataField("components")]
    public ComponentRegistry ComponentsAddToUser { get; } = new();
}
