using Content.Shared.Inventory.Events;
using Robust.Shared.GameObjects;

namespace Content.Shared._SW.Items;

/// <summary>
///     Prevents the wearer from removing the collar themselves; anyone else still can.
///     Lives in Shared so the restriction is also respected during client prediction.
/// </summary>
public sealed class SWElectricCollarSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWElectricCollarComponent, BeingUnequippedAttemptEvent>(OnUnequipAttempt);
    }

    private void OnUnequipAttempt(Entity<SWElectricCollarComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        if (args.Unequipee == args.UnEquipTarget)
        {
            args.Reason = "sw-electric-collar-cannot-remove";
            args.Cancel();
        }
    }
}