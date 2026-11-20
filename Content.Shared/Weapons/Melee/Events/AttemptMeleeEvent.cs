namespace Content.Shared.Weapons.Melee.Events;

/// <summary>
/// Raised directed on a weapon when attempt a melee attack.
/// </summary>
[ByRefEvent]
public record struct AttemptMeleeEvent(EntityUid User, bool Cancelled = false, string? Message = null);

/// <summary>
/// Raised on the weapon before the attack begins. A new weapon cooldown
/// defers the swing without starting its normal recovery.
/// </summary>
[ByRefEvent]
public record struct MeleeAttackPreCooldownEvent;
