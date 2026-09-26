using Content.Server._SW.Targeting;
using Content.Server.Popups;
using Content.Shared._SW.Droids;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;

namespace Content.Server._SW.Droids;

public sealed class SWDroidControlConsoleSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SWTargetingConfigTargetsSystem _targeting = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private const float UpdateDelay = 1f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDroidControlConsoleComponent, BoundUIOpenedEvent>(OnBoundUIOpened);
        SubscribeLocalEvent<SWDroidControlConsoleComponent, SWDroidControlConsoleToggleDatapadMessage>(OnToggleDatapad);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateDelay)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<SWDroidControlConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out var console, out var ui))
        {
            if (!_ui.IsUiOpen((uid, ui), SWDroidControlConsoleUiKey.Key))
                continue;

            UpdateInterface(uid, console);
        }
    }

    private void OnBoundUIOpened(Entity<SWDroidControlConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, SWDroidControlConsoleUiKey.Key))
            return;

        UpdateInterface(ent.Owner, ent.Comp);
    }

    private void OnToggleDatapad(Entity<SWDroidControlConsoleComponent> ent, ref SWDroidControlConsoleToggleDatapadMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!CanUse(user, ent.Owner))
        {
            _popup.PopupCursor(Loc.GetString("comms-console-permission-denied"), user, PopupType.Medium);
            return;
        }

        if (!TryGetEntity(args.DatapadUid, out var datapadUid) ||
            !TryComp<SWDroidTargetingTabletComponent>(datapadUid, out var tablet) ||
            tablet.Faction != ent.Comp.Faction)
        {
            UpdateInterface(ent.Owner, ent.Comp);
            return;
        }

        tablet.Enabled = !tablet.Enabled;
        _targeting.RefreshProvider(datapadUid.Value);
        UpdateInterface(ent.Owner, ent.Comp);
    }

    private void UpdateInterface(EntityUid uid, SWDroidControlConsoleComponent component)
    {
        var entries = new List<SWDroidControlConsoleDatapadEntry>();

        var query = EntityQueryEnumerator<SWDroidTargetingTabletComponent>();
        while (query.MoveNext(out var tabletUid, out var tablet))
        {
            if (tablet.Faction != component.Faction)
                continue;

            EnsureControlId(tabletUid, tablet);

            TryComp<SWTargetingConfigTargetsComponent>(tabletUid, out var targets);
            var status = Loc.GetString(tablet.Enabled
                ? "sw-droid-control-console-status-enabled"
                : "sw-droid-control-console-status-disabled");

            entries.Add(new SWDroidControlConsoleDatapadEntry(
                GetNetEntity(tabletUid),
                tablet.ControlId,
                Name(tabletUid),
                status,
                targets?.Targets.Count ?? 0,
                tablet.Enabled));
        }

        entries.Sort((a, b) => string.Compare(a.ControlId, b.ControlId, StringComparison.Ordinal));
        _ui.SetUiState(uid, SWDroidControlConsoleUiKey.Key, new SWDroidControlConsoleBoundUserInterfaceState(entries));
    }

    private bool CanUse(EntityUid user, EntityUid console)
    {
        return !TryComp<AccessReaderComponent>(console, out var accessReader) ||
               _access.IsAllowed(user, console, accessReader);
    }

    private void EnsureControlId(EntityUid uid, SWDroidTargetingTabletComponent component)
    {
        if (!string.IsNullOrWhiteSpace(component.ControlId))
            return;

        component.ControlId = $"{component.ControlIdPrefix}-{GetNetEntity(uid).Id}";
    }
}
