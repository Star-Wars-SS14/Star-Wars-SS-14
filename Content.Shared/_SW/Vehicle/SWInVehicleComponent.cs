using Robust.Shared.GameStates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWInVehicleComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Vehicle = default!;

    [AutoNetworkedField]
    public string OccupiedRole = default!;

    [AutoNetworkedField]
    public EntityUid? ActionEntity = default!;
}
