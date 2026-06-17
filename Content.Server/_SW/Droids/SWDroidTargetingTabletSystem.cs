using System.Linq;
using Content.Server._SW.TurretConsole;
using Content.Shared._SW.Droids;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;

namespace Content.Server._SW.Droids;

public sealed class SWDroidTargetingTabletSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public void ApplyConfigFromTablet(EntityUid tablet, SWTurretConsoleComponent console, EntityUid user)
    {
        if (!HasComp<SWDroidTargetingTabletComponent>(tablet) ||
            !TryComp<SWDroidCommanderComponent>(user, out var commander))
        {
            return;
        }

        foreach (var droid in commander.Droids.ToArray())
        {
            if (!TryComp<SWDroidCommandableComponent>(droid, out var commandable) ||
                commandable.ActiveCommander != user ||
                !_mobState.IsAlive(droid))
            {
                commander.Droids.Remove(droid);
                continue;
            }

            ApplyConfigToDroid(droid, console);
        }
    }

    public bool TryApplyHeldTabletConfig(EntityUid user, EntityUid droid)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities((user, null, null)))
        {
            if (!TryComp<SWDroidTargetingTabletComponent>(item, out _) ||
                !TryComp<SWTurretConsoleComponent>(item, out var console))
            {
                continue;
            }

            ApplyConfigToDroid(droid, console);
            return true;
        }

        return false;
    }

    public void ApplyConfigToDroid(EntityUid droid, SWTurretConsoleComponent console)
    {
        var config = EnsureComp<SWTurretConfigComponent>(droid);
        config.AllowedJobs = new Dictionary<string, bool>(console.JobsAllowed);
        config.TargetUnconfiguredJobs = false;
    }
}
