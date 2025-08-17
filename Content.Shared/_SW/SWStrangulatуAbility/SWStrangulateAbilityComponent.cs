using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.SWStrangulatуAbility;

[RegisterComponent, NetworkedComponent]
public sealed partial class SWStrangulateAbilityComponent : Component
{
    public EntityUid? CurrentTarget = null;

    [DataField]
    public string Action = "ActionStrangulate";

    [DataField]
    public SoundSpecifier GaspSound = new SoundPathSpecifier("/Audio/Effects/Gasp/male_deathgasp_1.ogg");

    [DataField]
    public LocId StartStrangulateMessage = "sw-start-strangulate";

    [DataField]
    public LocId StopStrangulateMessage = "sw-stop-strangulate";

    [DataField]
    public float Radius = 8f;
}
