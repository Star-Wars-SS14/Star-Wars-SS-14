using System;
using Robust.Shared.GameStates;

namespace Content.Shared._SW.ForceAbilities;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
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

    [AutoNetworkedField]
    public EntityUid? CurrentUser;

    [AutoNetworkedField]
    public float? StoredReflectProb;

    [DataField]
    public float BlockDuration = 5f;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan BlockEndTime;

    [DataField]
    public float DelayBetweenBlocks = 1f;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan NextReflectionAvailable;

    [AutoNetworkedField]
    public TimeSpan AttackDelayAfterGuard;

    [ViewVariables, AutoNetworkedField]
    public bool IsBlocking;
}
