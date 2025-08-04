using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TaskSystem;

[RegisterComponent]
public sealed partial class SWTaskComponent : Component
{
    public FixedPoint2 Completeness = 0.0;

    [DataField]
    public string Name = "task";

    [DataField]
    public string Description = "desc";

    [DataField]
    public int Score = 1;

    [DataField]
    public string RoundEndText = string.Empty;

    [DataField]
    public TaskSpawnRestrictions Restrictions = TaskSpawnRestrictions.OnePerFaction;

    public string OwningFaction = string.Empty;
}

public enum TaskSpawnRestrictions
{
    NoRestrictions,
    OnePerFaction,
    OnePerAllFactions,
}
