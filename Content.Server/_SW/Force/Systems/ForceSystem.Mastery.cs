using System.Linq;
using Content.Server._SW.Force.Components;
using Content.Server.Store.Systems;
using Content.Shared.Alert;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Systems;

public sealed partial class ForceSystem
{
    public void InitializeMastery() { }

    private void UpdateMastery(EntityUid? user, ForceComponent forceComponent)
    {
        if(user == null)
            return;

        if(forceComponent.Experience < forceComponent.ExperienceToNewLevel)
            return;

        if (TryComp<LightForceComponent>(user, out var lightForceComponent))
        {
            if (lightForceComponent.Mastery == Enum.GetValues(typeof(JediMasteryLevel)).Cast<JediMasteryLevel>().Max())
                return;

            lightForceComponent.Mastery += 1;

            InitializeMasteryAlerts(user, lightForceComponent: lightForceComponent, oldJediMasteryLevel: (lightForceComponent.Mastery - 1));
            InitializeMasteryActions(user, lightForceComponent: lightForceComponent);

            forceComponent.Experience = 0;
        }

        if (TryComp<DarkForceComponent>(user, out var darkForceComponent))
        {
            if (darkForceComponent.Mastery == Enum.GetValues(typeof(SithMasteryLevel)).Cast<SithMasteryLevel>().Max())
                return;

            darkForceComponent.Mastery += 1;

            InitializeMasteryAlerts(user, darkForceComponent, oldSithMasteryLevel: (darkForceComponent.Mastery - 1));
            InitializeMasteryActions(user, darkForceComponent);

            forceComponent.Experience = 0;
        }
    }

    private void InitializeMasteryAlerts(EntityUid? user, DarkForceComponent? darkForceComponent = null, LightForceComponent? lightForceComponent = null, JediMasteryLevel? oldJediMasteryLevel = null, SithMasteryLevel? oldSithMasteryLevel = null)
    {
        if (darkForceComponent != null)
        {
            if(oldSithMasteryLevel != null)
                _alertsSystem.ClearAlert((EntityUid) user!, darkForceComponent.SithMasteryAlerts[(SithMasteryLevel) oldSithMasteryLevel]);

            _alertsSystem.ShowAlert((EntityUid) user!, darkForceComponent.SithMasteryAlerts[darkForceComponent.Mastery]);
        }

        if (lightForceComponent != null)
        {
            if(oldJediMasteryLevel != null)
                _alertsSystem.ClearAlert((EntityUid) user!, lightForceComponent.JediMasteryAlerts[(JediMasteryLevel) oldJediMasteryLevel]);

            _alertsSystem.ShowAlert((EntityUid) user!, lightForceComponent.JediMasteryAlerts[lightForceComponent.Mastery]);
        }
    }

    private void InitializeMasteryActions(EntityUid? user,
        DarkForceComponent? darkForceComponent = null,
        LightForceComponent? lightForceComponent = null)
    {
        var target = (EntityUid) user!;

        if (lightForceComponent != null)
        {
            foreach (var ability in lightForceComponent.MasteryAbilities.Where(ability => lightForceComponent.Mastery == ability.Key))
            {
                _action.AddAction(target, ability.Value);
            }
        }

        if (darkForceComponent != null)
        {
            foreach (var ability in darkForceComponent.MasteryAbilities.Where(ability => darkForceComponent.Mastery == ability.Key))
            {
                _action.AddAction(target, ability.Value);
            }
        }
    }
}

[UsedImplicitly]
[DataDefinition]
public sealed partial class OpenMasteryShop : IAlertClick
{
    public void AlertClicked(EntityUid uid)
    {
        var entityManager = IoCManager.Resolve<IEntityManager>();

        if(!entityManager.TryGetComponent<StoreComponent>(uid, out var storeComponent))
            return;

        if(!entityManager.TryGetComponent<ForceComponent>(uid, out var forceComponent))
            return;

        Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> newBalance = new ()
        {
            { forceComponent.MasteryCurrencyProto, forceComponent.Experience }
        };

        storeComponent.Balance = newBalance;
        entityManager.EntitySysManager.GetEntitySystem<StoreSystem>().ToggleUi(uid, uid, storeComponent);
    }
}
