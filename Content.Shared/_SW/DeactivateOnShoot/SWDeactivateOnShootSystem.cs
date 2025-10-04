using Content.Shared.Clothing.Components;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle;
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
        SubscribeLocalEvent<SWDeactivateClothingOnShootComponent, GotEquippedEvent>(OnEquip);
        SubscribeLocalEvent<SWDeactivateClothingOnShootComponent, GotUnequippedEvent>(OnUnequip);
        SubscribeLocalEvent<SWDeactivateOnShootUserComponent, ShooterImpulseEvent>(OnShoot);
    }

    private void OnEquip(Entity<SWDeactivateClothingOnShootComponent> ent, ref GotEquippedEvent args)
    {
        EnsureComp<SWDeactivateOnShootUserComponent>(args.Equipee).Item = ent.Owner;
    }

    private void OnUnequip(Entity<SWDeactivateClothingOnShootComponent> ent, ref GotUnequippedEvent args)
    {
        RemComp<SWDeactivateOnShootUserComponent>(args.Equipee);
    }

    private void OnShoot(Entity<SWDeactivateOnShootUserComponent> ent, ref ShooterImpulseEvent args)
    {
        if (ent.Comp.Item == null
            || !TryComp <ToggleClothingComponent>(ent.Comp.Item, out var comp))
        {
            return;
        }
        _toggle.TryDeactivate(ent.Comp.Item.Value);
    }
}
