using Content.Shared.Damage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW;
public sealed class SWAfterAllModifiersAppliedDamageModifyEvent : EntityEventArgs
{
    public DamageSpecifier Damage;

    public SWAfterAllModifiersAppliedDamageModifyEvent(DamageSpecifier damage, EntityUid? origin = null)
    {
        Damage = damage;
    }
}
