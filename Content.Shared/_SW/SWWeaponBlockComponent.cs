using Content.Shared.Weapons.Melee;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW;

[RegisterComponent]
public sealed partial class SWWeaponBlockComponent : Component
{
    [DataField]
    public float ReflectionDuration = 2f;
    public TimeSpan ReflectionEndTime;
    [DataField]
    public float DelayBetweenReflections = 1f;
    public TimeSpan NextReflectionAvailable;

    [ViewVariables]
    public bool IsBlocking = false;
}
