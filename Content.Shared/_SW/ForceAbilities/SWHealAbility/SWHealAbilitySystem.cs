using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Medical;
using Content.Shared.Medical.Healing;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.ForceAbilities.SWHealAbility;
public sealed class SWHealAbilitySystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWHealAbilityComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<SWHealAbilityComponent, SWHealRequestedEvent>(OnHealRequested);
        SubscribeLocalEvent<DamageableComponent, SWHealDoAfterEvent>(OnDoAfter);
    }

    private void OnCompInit(Entity<SWHealAbilityComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent.Owner, ent.Comp.HealAction);
    }

    private void OnHealRequested(Entity<SWHealAbilityComponent> ent, ref SWHealRequestedEvent args)
    {
        args.Handled = TryHeal(ent, args.Target, ent);
    }

    private void OnDoAfter(Entity<DamageableComponent> target, ref SWHealDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (!TryComp(args.User, out SWHealAbilityComponent? healing))
            return;

        if (healing.DamageContainers is not null &&
            target.Comp.DamageContainerID is not null &&
            !healing.DamageContainers.Contains(target.Comp.DamageContainerID.Value))
        {
            return;
        }

        TryComp<BloodstreamComponent>(target, out var bloodstream);

        // Heal some bloodloss damage.
        if (healing.BloodlossModifier != 0 && bloodstream != null)
        {
            var isBleeding = bloodstream.BleedAmount > 0;
            _bloodstreamSystem.TryModifyBleedAmount((target.Owner, bloodstream), healing.BloodlossModifier);
            if (isBleeding != bloodstream.BleedAmount > 0)
            {
                var popup = (args.User == target.Owner)
                    ? Loc.GetString("medical-item-stop-bleeding-self")
                    : Loc.GetString("medical-item-stop-bleeding", ("target", Identity.Entity(target.Owner, EntityManager)));
                _popupSystem.PopupClient(popup, target, args.User);
            }
        }

        // Restores missing blood
        if (healing.ModifyBloodLevel != 0 && bloodstream != null)
            _bloodstreamSystem.TryModifyBloodLevel((target.Owner, bloodstream), healing.ModifyBloodLevel);

        if (!_damageable.TryChangeDamage(target.Owner, healing.Damage * _damageable.UniversalTopicalsHealModifier, out var healed, true, origin: args.Args.User) && healing.BloodlossModifier != 0)
            return;

        var total = healed.GetTotal();

        // Re-verify that we can heal the damage.
        var dontRepeat = false;

        if (target.Owner != args.User)
        {
            _adminLogger.Add(LogType.Healed,
                $"{ToPrettyString(args.User):user} healed {ToPrettyString(target.Owner):target} for {total:damage} damage");
        }
        else
        {
            _adminLogger.Add(LogType.Healed,
                $"{ToPrettyString(args.User):user} healed themselves for {total:damage} damage");
        }

        _audio.PlayPredicted(healing.HealingEndSound, target.Owner, args.User);

        // Logic to determine the whether or not to repeat the healing action
        args.Repeat = HasDamage((args.User, healing), target) && !dontRepeat;
        args.Handled = true;

        if (!args.Repeat)
        {
            _popupSystem.PopupClient(Loc.GetString("medical-item-finished-using", ("item", args.User)), target.Owner, args.User);
            return;
        }

        // Update our self heal delay so it shortens as we heal more damage.
        if (args.User == target.Owner)
            args.Args.Delay = healing.Delay;
    }

    private bool TryHeal(Entity<SWHealAbilityComponent> healing, Entity<DamageableComponent?> target, EntityUid user)
    {
        if (!Resolve(target, ref target.Comp, false))
            return false;

        if (healing.Comp.DamageContainers is not null &&
            target.Comp.DamageContainerID is not null &&
            !healing.Comp.DamageContainers.Contains(target.Comp.DamageContainerID.Value))
        {
            return false;
        }

        if (user != target.Owner && !_interactionSystem.InRangeUnobstructed(user, target.Owner, popup: true))
            return false;

        if (TryComp<StackComponent>(healing, out var stack) && stack.Count < 1)
            return false;

        if (!HasDamage(healing, target!))
        {
            _popupSystem.PopupClient(Loc.GetString("medical-item-cant-use", ("item", healing.Owner)), healing, user);
            return false;
        }

        _audio.PlayPredicted(healing.Comp.HealingBeginSound, healing, user);

        var isNotSelf = user != target.Owner;

        if (isNotSelf)
        {
            var msg = Loc.GetString("medical-item-popup-target", ("user", Identity.Entity(user, EntityManager)), ("item", healing.Owner));
            _popupSystem.PopupEntity(msg, target, target, PopupType.Medium);
        }

        var delay = healing.Comp.Delay;

        var doAfterEventArgs =
            new DoAfterArgs(EntityManager, user, delay, new SWHealDoAfterEvent(), target, target: target)
            {
                // Didn't break on damage as they may be trying to prevent it and
                // not being able to heal your own ticking damage would be frustrating.
                NeedHand = true,
                BreakOnMove = true,
                BreakOnWeightlessMove = false,
            };
        
        return _doAfter.TryStartDoAfter(doAfterEventArgs);
    }

    private bool HasDamage(Entity<SWHealAbilityComponent> healing, Entity<DamageableComponent> target)
    {
        var damageableDict = target.Comp.Damage.DamageDict;
        var healingDict = healing.Comp.Damage.DamageDict;
        foreach (var type in healingDict)
        {
            if (damageableDict[type.Key].Value > 0)
            {
                return true;
            }
        }

        if (TryComp<BloodstreamComponent>(target, out var bloodstream))
        {
            // Is ent missing blood that we can restore?
            if (healing.Comp.ModifyBloodLevel > 0
                && _solutionContainerSystem.ResolveSolution(target.Owner, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution)
                && _bloodstreamSystem.GetBloodLevel((target, bloodstream)) < 1)
            {
                return true;
            }

            // Is ent bleeding and can we stop it?
            if (healing.Comp.BloodlossModifier < 0 && bloodstream.BleedAmount > 0)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed partial class SWHealRequestedEvent : EntityTargetActionEvent
{

}

[Serializable, NetSerializable]
public sealed partial class SWHealDoAfterEvent : SimpleDoAfterEvent
{
}
