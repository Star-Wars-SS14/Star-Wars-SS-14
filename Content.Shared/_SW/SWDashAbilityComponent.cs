using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW;

[RegisterComponent]
public sealed partial class SWDashAbilityComponent : Component
{
    [DataField("sprintModifier"), ViewVariables(VVAccess.ReadWrite)]
    public float SpeedSprintModifier = 2f;
    [DataField("walkModifier")]
    public float SpeedWalkModifier = 2f;

    [DataField("action"), ViewVariables(VVAccess.ReadWrite)]
    public string? Action = "SWDashAction";

    [DataField("duration"), ViewVariables(VVAccess.ReadWrite)]
    public float DashDuration = 5f;
    public TimeSpan DashEndTime;

    [DataField("delay"), ViewVariables(VVAccess.ReadWrite)]
    public float DelayBetweenDashes = 7f;
    public TimeSpan NextDashAvailable;

    public bool IsDashing = false;
}
