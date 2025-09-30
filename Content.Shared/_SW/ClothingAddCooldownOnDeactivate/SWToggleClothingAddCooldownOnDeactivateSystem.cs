using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Item.ItemToggle.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.ClothingAddCooldownOnDeactivate;
public sealed class SWToggleClothingAddCooldownOnDeactivateSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<SWToggleClothingAddCooldownOnDeactivateComponent, ItemToggledEvent>(OnToggled);
    }

    private void OnToggled(Entity<SWToggleClothingAddCooldownOnDeactivateComponent> ent, ref ItemToggledEvent args)
    {
        ISawmill _s = Logger.GetSawmill("asdfgh");
        _s.Fatal("s00");
        if (!TryComp<ToggleClothingComponent>(ent, out var comp) || comp.ActionEntity == null)
        {
            return;
        }
        _s.Fatal("s01");
        if (args.Activated == false)
        {
            var cooldown = TimeSpan.FromSeconds(ent.Comp.UseDelay);
            _s.Fatal("sadfghjkl");
            _actionsSystem.SetUseDelay(comp.ActionEntity, cooldown);
            _actionsSystem.SetCooldown(comp.ActionEntity, cooldown);
        } else
        {
            var cooldown = TimeSpan.FromSeconds(1);
            _actionsSystem.SetUseDelay(comp.ActionEntity, cooldown);
            _actionsSystem.SetCooldown(comp.ActionEntity, cooldown);
        }
    }
}
