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
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (!_ui.IsUiOpen((uid, ui), SWTurretConsoleUiKey.Key))
                continue;

            UpdateInterface(uid);
        }
    }

    private void OnBoundUIOpened(Entity<SWDroidTargetingTabletComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, SWTurretConsoleUiKey.Key))
            return;

        UpdateInterface(ent.Owner);
    }

    private void OnSettingsChanged(Entity<SWDroidTargetingTabletComponent> ent, ref SWTargetingConsoleSettingsChangedEvent args)
    {
        UpdateInterface(ent.Owner);
    }

    private void UpdateInterface(EntityUid uid)
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

        var state = new SWDroidControlDatapadBoundUserInterfaceState(
            new Dictionary<string, bool>(console.JobsAllowed),
            factions,
            GetDroidEntries(uid));

        _ui.SetUiState(uid, SWTurretConsoleUiKey.Key, state);
    }

    private List<SWDroidControlDatapadDroidEntry> GetDroidEntries(EntityUid uid)
    {
        var entries = new List<SWDroidControlDatapadDroidEntry>();
        if (!TryComp<SWTargetingConfigTargetsComponent>(uid, out var targets))
            return entries;

        foreach (var droid in targets.Targets)
        {
            if (TerminatingOrDeleted(droid))
                continue;

            entries.Add(new SWDroidControlDatapadDroidEntry(
                Name(droid),
                GetCoordinates(droid),
                GetCharge(droid),
                GetStatus(droid)));
        }

        return entries;
    }

    private string GetCoordinates(EntityUid droid)
    {
        if (!TryComp(droid, out TransformComponent? xform))
            return Loc.GetString("sw-droid-control-datapad-unknown");

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
