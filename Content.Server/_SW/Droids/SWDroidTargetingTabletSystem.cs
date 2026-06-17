using System.Linq;
using Content.Server._SW.Targeting;
using Content.Server._SW.TurretConsole;
using Content.Shared._SW.Droids;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;

namespace Content.Server._SW.Droids;

public sealed class SWDroidTargetingTabletSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SWTargetingConfigTargetsSystem _targeting = default!;

    public bool TryRegisterHeldTabletTarget(EntityUid user, EntityUid droid)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities((user, null, null)))
        {
            if (!TryComp<SWDroidTargetingTabletComponent>(item, out _) ||
                !HasComp<SWTurretConsoleComponent>(item))
            {
                continue;
            }

            _targeting.RegisterTarget(item, droid);
            return true;
        }

        return false;
    }

    public void UnregisterTargetFromUserTablets(EntityUid user, EntityUid droid)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities((user, null, null)))
        {
            if (!TryComp<SWDroidTargetingTabletComponent>(item, out _) ||
                !HasComp<SWTurretConsoleComponent>(item))
            {
                continue;
            }

            _targeting.UnregisterTarget(item, droid);
        }
    }
}
