using Content.Shared._SW.TaskSystem;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.KillRandomOfFaction;

[RegisterComponent]
public sealed partial class SWKillRandomByNameComponent : Component
{
    [DataField]
    public List<string> Factions = new();

    [DataField]
    public Dictionary<string, int> JobsToScores = new();

    public EntityUid? PersonToKill = null;

    public string? JobOfTarget = null;   
}
