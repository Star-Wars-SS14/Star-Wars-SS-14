using System;
using Robust.Shared.GameStates;

namespace Content.Shared._SW.ForceAbilities;

[RegisterComponent, NetworkedComponent]
public sealed partial class SWWeaponBlockComponent : Component
{
    [DataField]
    public bool RequiresForceUser = true;

    [DataField]
    public bool BlocksMelee = true;

    [DataField]
    public bool BlocksProjectiles = false;

    [DataField]
    public float ProjectileReflectProb = 1f;

    public EntityUid? CurrentUser = null;

    public float? StoredReflectProb = null;

    [DataField]
    public float BlockDuration = 5f;

    public TimeSpan BlockEndTime;

    [DataField]
    public float DelayBetweenBlocks = 1f;

    public TimeSpan NextReflectionAvailable;

    [ViewVariables]
    public bool IsBlocking = false;
}
