using Content.Server._SW.Force.Components;
using Content.Server.Store.Systems;
using Content.Shared._SW;
using Content.Shared.Alert;
using Content.Shared.FixedPoint;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem
{
    public void InitializeMastery()
    {
        SubscribeLocalEvent<StoreComponent, MasteryUpEvent>(OnMasteryUpBought);
        SubscribeLocalEvent<StoreComponent, MasteryLightingUpgradeEvent>(OnMasteryLightingUpgrade);
        SubscribeLocalEvent<ForceComponent, OpenMasteryShopAlertEvent>(OnOpenMasteryShopAlert);
    }

    private void OnMasteryUpBought(EntityUid player, StoreComponent storeComponent, MasteryUpEvent args)
    {
        if (!TryComp<ForceComponent>(player, out _))
            return;

        if (TryComp<DarkForceComponent>(player, out var darkForceComponent))
        {
            if (darkForceComponent.Mastery == SithMasteryLevel.SithMasteryLevel3)
                return;

            darkForceComponent.Mastery += 1;
            InitializeMasteryAlerts(player, darkForceComponent, oldSithMasteryLevel: darkForceComponent.Mastery - 1);
        }

        if (TryComp<LightForceComponent>(player, out var lightForceComponent))
        {
            if (lightForceComponent.Mastery == JediMasteryLevel.JediMasteryLevel3)
                return;

            lightForceComponent.Mastery += 1;
            InitializeMasteryAlerts(player,
                lightForceComponent: lightForceComponent,
                oldJediMasteryLevel: lightForceComponent.Mastery - 1);
        }
    }

    private void OnMasteryLightingUpgrade(EntityUid player,
        StoreComponent storeComponent,
        MasteryLightingUpgradeEvent args)
    {
        if (!TryComp<ForceComponent>(player, out _) || !TryComp<DarkForceComponent>(player, out var darkForceComponent))
            return;

        darkForceComponent.DamageLighting += darkForceComponent.BonusDamageFromUpgrade;
        darkForceComponent.BoltCount += darkForceComponent.UpgradeBoltCount;
    }

    private void InitializeMasteryAlerts(EntityUid? user,
        DarkForceComponent? darkForceComponent = null,
        LightForceComponent? lightForceComponent = null,
        JediMasteryLevel? oldJediMasteryLevel = null,
        SithMasteryLevel? oldSithMasteryLevel = null)
    {
        if (darkForceComponent != null)
        {
            if (oldSithMasteryLevel != null)
            {
                _alertsSystem.ClearAlert((EntityUid) user!,
                    darkForceComponent.SithMasteryAlerts[(SithMasteryLevel) oldSithMasteryLevel]);
            }

            _alertsSystem.ShowAlert((EntityUid) user!,
                darkForceComponent.SithMasteryAlerts[darkForceComponent.Mastery]);
        }

        if (lightForceComponent != null)
        {
            if (oldJediMasteryLevel != null)
            {
                _alertsSystem.ClearAlert((EntityUid) user!,
                    lightForceComponent.JediMasteryAlerts[(JediMasteryLevel) oldJediMasteryLevel]);
            }

            _alertsSystem.ShowAlert((EntityUid) user!,
                lightForceComponent.JediMasteryAlerts[lightForceComponent.Mastery]);
        }
    }

    private void OnOpenMasteryShopAlert(EntityUid uid, ForceComponent component, ref OpenMasteryShopAlertEvent args)
    {
        if (!TryComp<StoreComponent>(uid, out var storeComponent))
        {
            args.Handled = false;
            return;
        }

        if (!TryComp<ForceComponent>(uid, out var forceComponent))
        {
            args.Handled = false;
            return;
        }

        Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> newBalance = new()
        {
            { forceComponent.MasteryCurrencyProto, forceComponent.Experience }
        };

        storeComponent.Balance = newBalance;
        _storeSystem.ToggleUi(uid, uid, storeComponent);

        args.Handled = true;
    }
}
