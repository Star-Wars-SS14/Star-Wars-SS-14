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
    public string TaskName = "task";

    [DataField]
    public string TaskDesc = "desc";

    [DataField]
    public int Score = 1;

    [DataField]
    public string RoundEndText = string.Empty;
}
