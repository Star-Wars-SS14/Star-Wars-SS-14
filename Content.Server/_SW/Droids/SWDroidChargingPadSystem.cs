using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Emp;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell;
using Content.Shared.Whitelist;
using Robust.Shared.Map;

namespace Content.Server._SW.Droids;

public sealed class SWDroidChargingPadSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly PowerReceiverSystem _receiver = default!;
    [Dependency] private readonly PredictedBatterySystem _battery = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    private readonly HashSet<EntityUid> _nearby = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDroidChargingPadComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWDroidChargingPadComponent, TransformComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var component, out var transform, out var receiver))
        {
            component.Accumulator += frameTime;
            if (component.Accumulator < component.UpdateDelay)
                continue;

            var elapsed = component.Accumulator;
            component.Accumulator = 0f;

            UpdatePad((uid, component, transform, receiver), elapsed);
        }
    }

    private void OnShutdown(EntityUid uid, SWDroidChargingPadComponent component, ComponentShutdown args)
    {
        _receiver.SetLoad(uid, component.PassiveDraw);
    }

    private void UpdatePad(Entity<SWDroidChargingPadComponent, TransformComponent, ApcPowerReceiverComponent> pad, float elapsed)
    {
        var status = GetStatus(pad, elapsed);
        var load = status == CellChargerStatus.Charging
            ? pad.Comp1.ChargeRate
            : pad.Comp1.PassiveDraw;

        _receiver.SetLoad(pad.Owner, load);
        _appearance.SetData(pad.Owner, CellVisual.Light, status);
        _appearance.SetData(pad.Owner, CellVisual.Occupied, status is CellChargerStatus.Charging or CellChargerStatus.Charged);
    }

    private CellChargerStatus GetStatus(Entity<SWDroidChargingPadComponent, TransformComponent, ApcPowerReceiverComponent> pad, float elapsed)
    {
        if (!pad.Comp2.Anchored ||
            !_receiver.IsPowered(pad.Owner, pad.Comp3) ||
            HasComp<EmpDisabledComponent>(pad.Owner))
        {
            return CellChargerStatus.Off;
        }

        if (!TryFindChargeTarget(pad, out var battery))
            return CellChargerStatus.Empty;

        if (_battery.IsFull(battery.AsNullable()))
            return CellChargerStatus.Charged;

        var changed = _battery.ChangeCharge(battery.AsNullable(), pad.Comp1.ChargeRate * elapsed);
        return changed > 0f
            ? CellChargerStatus.Charging
            : CellChargerStatus.Charged;
    }

    private bool TryFindChargeTarget(
        Entity<SWDroidChargingPadComponent, TransformComponent, ApcPowerReceiverComponent> pad,
        out Entity<PredictedBatteryComponent> battery)
    {
        battery = default;
        _nearby.Clear();

        _lookup.GetEntitiesInRange(pad.Comp2.Coordinates, pad.Comp1.Range, _nearby, LookupFlags.Dynamic | LookupFlags.Approximate);

        var hasFullBattery = false;
        Entity<PredictedBatteryComponent> fullBattery = default;
        foreach (var target in _nearby)
        {
            if (target == pad.Owner ||
                _whitelist.IsWhitelistFail(pad.Comp1.Whitelist, target) ||
                !_powerCell.TryGetBatteryFromSlotOrEntity(target, out var candidate))
            {
                continue;
            }

            if (!_battery.IsFull(candidate.Value.AsNullable()))
            {
                battery = candidate.Value;
                return true;
            }

            if (!hasFullBattery)
            {
                hasFullBattery = true;
                fullBattery = candidate.Value;
            }
        }

        battery = fullBattery;
        return hasFullBattery;
    }
}
