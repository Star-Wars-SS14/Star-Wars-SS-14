using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.ForceAbilities;

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

    // Keep only ONE IsDashing listing
    public bool IsDashing = false;

    // Sprite swaying parameters during a dash
    [DataField("swayAmplitude")]
    public float SwayAmplitude = 0.15f;
    
    [DataField("swayFrequency")]
    public float SwayFrequency = 4f;
    
    public float SwayTime = 0f;
}