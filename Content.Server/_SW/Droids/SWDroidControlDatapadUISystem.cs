using Content.Server._SW.Targeting;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.TurretConsole;
using Content.Shared._SW;
using Content.Shared._SW.Droids;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._SW.Droids;

public sealed class SWDroidControlDatapadUISystem : EntitySystem
{
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly PredictedBatterySystem _battery = default!;
    [Dependency] private readonly SWFactionGameruleSystem _factions = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private const float UpdateDelay = 1f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDroidTargetingTabletComponent, BoundUIOpenedEvent>(OnBoundUIOpened);
        SubscribeLocalEvent<SWDroidTargetingTabletComponent, BoundUIClosedEvent>(OnBoundUIClosed);
        SubscribeLocalEvent<SWDroidTargetingTabletComponent, SWTargetingConsoleSettingsChangedEvent>(OnSettingsChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateDelay)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<SWDroidTargetingTabletComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out var tablet, out var ui))
        {
            if (!_ui.IsUiOpen((uid, ui), SWTurretConsoleUiKey.Key))
                continue;

            UpdateInterface(uid, tablet);
        }
    }

    private void OnBoundUIOpened(Entity<SWDroidTargetingTabletComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, SWTurretConsoleUiKey.Key))
            return;

        ent.Comp.CurrentUiUser = args.Actor;
        UpdateInterface(ent.Owner, ent.Comp);
    }

    private void OnBoundUIClosed(Entity<SWDroidTargetingTabletComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!Equals(args.UiKey, SWTurretConsoleUiKey.Key))
            return;

        if (ent.Comp.CurrentUiUser == args.Actor)
            ent.Comp.CurrentUiUser = null;
    }

    private void OnSettingsChanged(Entity<SWDroidTargetingTabletComponent> ent, ref SWTargetingConsoleSettingsChangedEvent args)
    {
        ent.Comp.CurrentUiUser = args.User;
        UpdateInterface(ent.Owner, ent.Comp);
    }

    private void UpdateInterface(EntityUid uid, SWDroidTargetingTabletComponent tablet)
    {
        if (!TryComp<SWTurretConsoleComponent>(uid, out var console))
            return;

        var factions = new Dictionary<string, string>();
        foreach (var (id, ent) in _factions.GetFactions())
        {
            if (ent == null)
                continue;

            factions[id] = ent.Value.Comp.Name;
        }

        TryComp<SWTargetingConfigTargetsComponent>(uid, out var targets);
        var state = new SWDroidControlDatapadBoundUserInterfaceState(
            new Dictionary<string, bool>(console.JobsAllowed),
            factions,
            GetDroidEntries(uid),
            GetDroidEntries(uid, tablet.CurrentUiUser),
            GetMapUid(tablet.CurrentUiUser),
            tablet.Enabled,
            targets?.TargetHumanoidsWithoutId ?? false,
            targets?.TargetAnimals ?? false);

        _ui.SetUiState(uid, SWTurretConsoleUiKey.Key, state);
    }

    private List<SWDroidControlDatapadDroidEntry> GetDroidEntries(EntityUid uid, EntityUid? user = null)
    {
        var entries = new List<SWDroidControlDatapadDroidEntry>();
        if (!TryComp<SWTargetingConfigTargetsComponent>(uid, out var targets))
            return entries;

        var userGrid = GetGridUid(user);

        foreach (var droid in targets.Targets)
        {
            if (TerminatingOrDeleted(droid))
                continue;

            if (!TryComp(droid, out TransformComponent? xform))
                continue;

            if (user != null && (userGrid == null || xform.GridUid != userGrid))
                continue;

            entries.Add(new SWDroidControlDatapadDroidEntry(
                GetNetEntity(droid),
                GetNetCoordinates(xform.Coordinates),
                Name(droid),
                GetCoordinates(droid, xform),
                GetCharge(droid),
                GetStatus(droid)));
        }

        return entries;
    }

    private NetEntity? GetMapUid(EntityUid? user)
    {
        var grid = GetGridUid(user);
        return grid == null ? null : GetNetEntity(grid.Value);
    }

    private EntityUid? GetGridUid(EntityUid? user)
    {
        if (user == null || !TryComp(user.Value, out TransformComponent? xform))
            return null;

        return xform.GridUid;
    }

    private string GetCoordinates(EntityUid droid, TransformComponent xform)
    {
        var coordinates = _transform.GetMapCoordinates(droid, xform);
        return Loc.GetString(
            "sw-droid-control-datapad-coordinates",
            ("x", MathF.Round(coordinates.Position.X)),
            ("y", MathF.Round(coordinates.Position.Y)));
    }

    private string GetCharge(EntityUid droid)
    {
        if (!_powerCell.TryGetBatteryFromSlotOrEntity(droid, out var battery))
            return Loc.GetString("sw-droid-control-datapad-no-battery");

        var charge = _battery.GetChargeLevel(battery.Value.AsNullable());
        return Loc.GetString("sw-droid-control-datapad-charge-percent", ("percent", MathF.Round(charge * 100f)));
    }

    private string GetStatus(EntityUid droid)
    {
        if (TryComp<MobStateComponent>(droid, out var mobState))
        {
            return mobState.CurrentState switch
            {
                MobState.Alive => Loc.GetString("sw-droid-control-datapad-status-alive"),
                MobState.Critical => Loc.GetString("sw-droid-control-datapad-status-critical"),
                MobState.Dead => Loc.GetString("sw-droid-control-datapad-status-dead"),
                _ => Loc.GetString("sw-droid-control-datapad-status-unknown")
            };
        }

        return Loc.GetString("sw-droid-control-datapad-status-unknown");
    }
}
