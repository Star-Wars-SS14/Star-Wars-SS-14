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
        if (!TryComp<ToggleClothingComponent>(ent, out var comp) || comp.ActionEntity == null)
        {
            return;
        }

        if (args.Activated == false)
        {
            var cooldown = TimeSpan.FromSeconds(ent.Comp.UseDelay);
            _actionsSystem.SetUseDelay(comp.ActionEntity, cooldown);
            _actionsSystem.StartUseDelay(comp.ActionEntity);
        }
        else
        {
            var cooldown = TimeSpan.FromSeconds(1);
            _actionsSystem.SetUseDelay(comp.ActionEntity, cooldown);
            _actionsSystem.StartUseDelay(comp.ActionEntity);
        }
    }
}
