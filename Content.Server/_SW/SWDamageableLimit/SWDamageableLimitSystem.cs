using Content.Shared._SW;
using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.SWDamageableLimit;
public sealed class SWDamageableLimitSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWDamageableLimitComponent, SWAfterAllModifiersAppliedDamageModifyEvent>(OnDamage, after: [typeof(SharedArmorSystem)]);
    }

    private void OnDamage(Entity<SWDamageableLimitComponent> ent, ref SWAfterAllModifiersAppliedDamageModifyEvent args)
    {
        if (!TryComp<MobThresholdsComponent>(ent, out var comp))
        {
            return;
        }

        if (!TryComp<DamageableComponent>(ent, out var damageable))
        {
            return;
        }


        FixedPoint2 damageToDeath = 0;

        foreach (var (amount, type) in comp.Thresholds)
        {
            if (type == MobState.Dead)
            {
                damageToDeath = amount;
            }
        }

        var total = args.Damage.GetTotal();
        if (args.Damage.DamageDict.TryGetValue("Structural", out var value1))
        {
            total -= value1;
        }

        var maxDamage = damageToDeath + ent.Comp.TotalDamageToDeadAdd;
        var curDamage = damageable.Damage.GetTotal();

        if (damageable.Damage.DamageDict.TryGetValue("Structural", out var value2))
        {
            curDamage -= value2;
        }

        var newDamageTotal = total + curDamage;

        if (newDamageTotal > maxDamage)
        {
            FixedPoint2 modifier = (maxDamage - curDamage) / total;
            args.Damage = args.Damage * modifier;
        }
    }
}

