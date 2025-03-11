using Content.Shared._SW.TaskSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.KillRandomOfFaction;

public sealed partial class SWKillRandomOfFactionComponent : Component
{
    [DataField(required: true)]
    public string Faction;

    public EntityUid PersonToKill;
}
