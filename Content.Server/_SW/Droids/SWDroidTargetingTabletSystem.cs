using Content.Server._SW.Targeting;
using Content.Server._SW.TurretConsole;
using Content.Server.Popups;
using Content.Shared._SW.Droids;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Examine;
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

        SubscribeLocalEvent<SWDroidTargetingTabletComponent, ComponentStartup>(OnTabletStartup);
        SubscribeLocalEvent<SWDroidTargetingTabletComponent, ExaminedEvent>(OnTabletExamined);
        SubscribeLocalEvent<SWDroidTargetingTabletComponent, SWTargetingProviderAvailableEvent>(OnProviderAvailable);
        SubscribeLocalEvent<SWDroidCommandableComponent, InteractUsingEvent>(OnDroidInteractUsing);
    }

    private void OnTabletStartup(Entity<SWDroidTargetingTabletComponent> ent, ref ComponentStartup args)
    {
        EnsureControlId(ent.Owner, ent.Comp);
    }

    private void OnTabletExamined(Entity<SWDroidTargetingTabletComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        EnsureControlId(ent.Owner, ent.Comp);

        args.PushMarkup(Loc.GetString(
            "sw-droid-targeting-tablet-examine-control-id",
            ("id", ent.Comp.ControlId),
            ("status", Loc.GetString(ent.Comp.Enabled
                ? "sw-droid-control-console-status-enabled"
                : "sw-droid-control-console-status-disabled"))));
    }

    private void OnProviderAvailable(Entity<SWDroidTargetingTabletComponent> ent, ref SWTargetingProviderAvailableEvent args)
    {
        if (ent.Comp.Enabled)
            return;

        args.Cancel();

        if (!args.Popup ||
            args.User is not { Valid: true } user)
        {
            return;
        }

        _popup.PopupCursor(Loc.GetString("sw-droid-targeting-tablet-disabled"), user, PopupType.MediumCaution);
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

        if (!tablet.Enabled)
        {
            _popup.PopupEntity(Loc.GetString("sw-droid-targeting-tablet-disabled"), droid, args.User, PopupType.MediumCaution);
            return;
        }

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

    private void EnsureControlId(EntityUid uid, SWDroidTargetingTabletComponent component)
    {
        if (!string.IsNullOrWhiteSpace(component.ControlId))
            return;

        component.ControlId = $"{component.ControlIdPrefix}-{GetNetEntity(uid).Id}";
    }
}
