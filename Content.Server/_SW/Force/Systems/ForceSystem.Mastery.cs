using System.Linq;
using Content.Server._SW.Force.Components;
using Content.Shared.Popups;

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
            InitializePopups(user, lightForceComponent: lightForceComponent);
            InitializeMasteryAlerts(user, lightForceComponent: lightForceComponent, oldJediMasteryLevel: (lightForceComponent.Mastery - 1));
            InitializeMasteryActions(user, lightForceComponent: lightForceComponent);

            forceComponent.Experience = 0;
        }

        if (TryComp<DarkForceComponent>(user, out var darkForceComponent))
        {
            if (darkForceComponent.Mastery == Enum.GetValues(typeof(SithMasteryLevel)).Cast<SithMasteryLevel>().Max())
                return;

            darkForceComponent.Mastery += 1;
            InitializePopups(user, darkForceComponent);
            InitializeMasteryAlerts(user, darkForceComponent, oldSithMasteryLevel: (darkForceComponent.Mastery - 1));
            InitializeMasteryActions(user, darkForceComponent);

            forceComponent.Experience = 0;
        }
    }

    private void InitializePopups(EntityUid? user, DarkForceComponent? darkForceComponent = null, LightForceComponent? lightForceComponent = null)
    {
        if (darkForceComponent != null)
        {
            _popupSystem.PopupEntity(darkForceComponent.PopupsMessages[darkForceComponent.Mastery],
                (EntityUid) user!,
                PopupType.LargeCaution);
        }

        if (lightForceComponent != null)
        {
            _popupSystem.PopupEntity(lightForceComponent.PopupsMessages[lightForceComponent.Mastery],
                (EntityUid) user!,
                PopupType.LargeCaution);
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
