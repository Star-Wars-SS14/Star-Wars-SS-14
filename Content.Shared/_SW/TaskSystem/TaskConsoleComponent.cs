using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TaskSystem;

[RegisterComponent]
public sealed partial class TaskConsoleComponent : Component
{
    [DataField(required: true)]
    public string Faction;

    /// <summary>
    /// The lowest rank from the faction's commandRanks that may open this console.
    /// </summary>
    [DataField]
    public int MinOfficerRank = 60;

    /// <summary>
    /// Jobs that may open this console whatever the rank. Minor factions have no ranks, so their leader goes here.
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> LeaderJobs = new();
}
