using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.Serialization;

namespace Content.Server._SW;

[RegisterComponent]
[DataDefinition]
public sealed partial class SWPushAbilityComponent : Component
{
    [DataField("actionPrototype")]
    public string ActionPrototype = "ActionForcePush";

    /// <summary>
    /// How long the victim remains in a dazed state after landing
    /// </summary>
    [DataField("knockedDownDuration")]
    public float KnockedDownDuration = 5f;

    /// <summary>
    /// Knockback distance in tiles
    /// </summary>
    [DataField("pushDistance")]
    public float PushDistance = 3f;

    /// <summary>
    /// Ejection speed (tiles per second)
    /// </summary>
    [DataField("pushSpeed")]
    public float PushSpeed = 8f;

    /// <summary>
    /// Time between smoke effect spawns
    /// </summary>
    [DataField("trailInterval")]
    public float TrailInterval = 0.15f;
}