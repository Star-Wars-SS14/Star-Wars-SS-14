using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent]
public sealed partial class SWInVehicleComponent : Component
{
    public Entity<SWVehicleComponent> Vehicle = default!;

    public string OccupiedRole = default!;

    public EntityUid? ActionEntity = default!;
}
