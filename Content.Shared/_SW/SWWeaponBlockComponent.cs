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
    public EntityUid? CurrentUser = null;
}
