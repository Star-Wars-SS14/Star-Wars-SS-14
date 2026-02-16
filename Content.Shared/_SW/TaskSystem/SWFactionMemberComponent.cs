using Robust.Shared.GameStates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TaskSystem;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWFactionMemberComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public string Faction;

    [DataField, AutoNetworkedField]
    public bool IsLeader = false;

    [DataField, AutoNetworkedField]
    public string? Job = null;
}
