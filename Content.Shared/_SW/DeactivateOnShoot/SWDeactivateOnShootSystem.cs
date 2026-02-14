using Content.Shared.Clothing.Components;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.DeactivateOnShoot;
public sealed class SWDeactivateOnShootSystem : EntitySystem
{
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    public override void Initialize()
    {
        base.Initialize();
        ///we use <see cref="ShooterImpulseEvent"> here
        ///as event that raised on user after shooting
        SubscribeLocalEvent<SWDeactivateClothingOnAttackComponent, GotEquippedEvent>(OnEquip);
        SubscribeLocalEvent<SWDeactivateClothingOnAttackComponent, GotUnequippedEvent>(OnUnequip);
        SubscribeLocalEvent<SWDeactivateOnAttackUserComponent, ShooterImpulseEvent>(OnShoot);
        SubscribeLocalEvent<SWDeactivateOnAttackUserComponent, MeleeAttackEvent>(OnMelee);
    }

    private void OnEquip(Entity<SWDeactivateClothingOnAttackComponent> ent, ref GotEquippedEvent args)
    {
        EnsureComp<SWDeactivateOnAttackUserComponent>(args.Equipee).Item = ent.Owner;
    }

    private void OnUnequip(Entity<SWDeactivateClothingOnAttackComponent> ent, ref GotUnequippedEvent args)
    {
        RemComp<SWDeactivateOnAttackUserComponent>(args.Equipee);
    }

    private void OnShoot(Entity<SWDeactivateOnAttackUserComponent> ent, ref ShooterImpulseEvent args)
    {
        if (ent.Comp.Item == null
            || !TryComp <ToggleClothingComponent>(ent.Comp.Item, out var comp))
        {
            return;
        }
        _toggle.TryDeactivate(ent.Comp.Item.Value);
    }
    private void OnMelee(Entity<SWDeactivateOnAttackUserComponent> ent, ref MeleeAttackEvent args)
    {
        if (ent.Comp.Item == null
            || !TryComp<ToggleClothingComponent>(ent.Comp.Item, out var comp))
        {
            return;
        }
        _toggle.TryDeactivate(ent.Comp.Item.Value);
    }
}
