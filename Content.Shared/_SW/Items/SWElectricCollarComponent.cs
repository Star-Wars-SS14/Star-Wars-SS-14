using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using System;
using Content.Shared.Damage;

namespace Content.Shared._SW.Items;

/// <summary>
///     Marks a clothing item as an electric shock collar controlled via device linking.
/// </summary>
[RegisterComponent]
public sealed partial class SWElectricCollarComponent : Component
{
    /// <summary>
    ///     Damage dealt to the wearer on activation. Falls back to 5 Shock if empty.
    /// </summary>
    [DataField]
    public DamageSpecifier Damage = new();

    /// <summary>
    ///     Duration of the stun and knockdown applied on activation.
    /// </summary>
    [DataField]
    public TimeSpan StunTime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Shock sound played on the wearer on activation.
    /// </summary>
    [DataField]
    public SoundPathSpecifier ShockSound = new("/Audio/_SW/discharge.ogg");

    /// <summary>
    ///     Sound played when the collar is equipped onto someone.
    /// </summary>
    [DataField]
    public SoundPathSpecifier EquipSound = new("/Audio/Items/belt_equip.ogg");

    /// <summary>
    ///     Device link sink port that triggers the shock.
    /// </summary>
    [DataField]
    public string Port = "SWCollarShock";
}