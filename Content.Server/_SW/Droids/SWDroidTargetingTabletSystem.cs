using Content.Server._SW.Targeting;
using Content.Server._SW.TurretConsole;
using Content.Server.Popups;
using Content.Shared._SW.Droids;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server._SW.Droids;

public sealed class SWDroidTargetingTabletSystem : EntitySystem
{
    [Dependency] private readonly LockSystem _lock = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SWDroidModuleSystem _modules = default!;
    [Dependency] private readonly SWTargetingConfigTargetsSystem _targeting = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDroidCommandableComponent, InteractUsingEvent>(OnDroidInteractUsing);
    }

    private void OnDroidInteractUsing(Entity<SWDroidCommandableComponent> droid, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            !TryComp<SWDroidTargetingTabletComponent>(args.Used, out var tablet) ||
            !HasComp<SWTurretConsoleComponent>(args.Used))
        {
            return;
        }

        args.Handled = true;

        if (_lock.IsLocked(droid.Owner))
        {
            _popup.PopupEntity(Loc.GetString("sw-droid-targeting-tablet-locked"), droid, args.User, PopupType.MediumCaution);
            return;
        }

        if (TryComp<BorgChassisComponent>(droid, out var chassis))
            _modules.RefreshFactionFromModules((droid.Owner, chassis));

        if (!CanBindTablet(args.User, tablet, droid.Comp))
        {
            _popup.PopupEntity(Loc.GetString("sw-droid-targeting-tablet-wrong-faction"), droid, args.User, PopupType.MediumCaution);
            return;
        }

        _targeting.UnregisterTargetFromAll(droid);
        _targeting.RegisterTarget(args.Used, droid);
        _popup.PopupEntity(Loc.GetString("sw-droid-targeting-tablet-linked"), droid, args.User);
    }

    private bool CanBindTablet(EntityUid user, SWDroidTargetingTabletComponent tablet, SWDroidCommandableComponent droid)
    {
        if (string.IsNullOrWhiteSpace(droid.Faction) ||
            tablet.Faction != droid.Faction)
        {
            return false;
        }

        return true;
    }

}
