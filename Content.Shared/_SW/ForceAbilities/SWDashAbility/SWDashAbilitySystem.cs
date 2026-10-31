using Content.Shared._SW.Vehicle;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Sprite;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.ForceAbilities;

public sealed class SWDashAbilitySystem : EntitySystem
{
    [Dependency] SharedActionsSystem _actions = default!;
    [Dependency] IGameTiming _gameTiming = default!;    
    [Dependency] MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWDashAbilityComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<SWDashAbilityComponent, DashRequestedEvent>(OnTryDash);
        SubscribeLocalEvent<SWDashAbilityComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
    }

    private void OnRefreshMovement(Entity<SWDashAbilityComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.IsDashing)
        {
            args.ModifySpeed(ent.Comp.SpeedWalkModifier, ent.Comp.SpeedSprintModifier);
        }
    }

    private void OnTryDash(Entity<SWDashAbilityComponent> ent, ref DashRequestedEvent args)
    {
        if (args.Handled)
        {
            return;
        }
        StartDash(ent);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWDashAbilityComponent>();
        while (query.MoveNext(out var ent, out var comp))
        {
            if (comp.IsDashing && _gameTiming.CurTime >= comp.DashEndTime)
            {
                StopDashing(ent, comp);
            }
        }
    }

    private void StopDashing(EntityUid uid, SWDashAbilityComponent comp)
    {
        comp.IsDashing = false;
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
        comp.SwayTime = 0f;
    }
    
    private void StartDash(Entity<SWDashAbilityComponent> ent)
    {
        ent.Comp.IsDashing = true;
        ent.Comp.DashEndTime = _gameTiming.CurTime + TimeSpan.FromSeconds(ent.Comp.DashDuration);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnCompInit(Entity<SWDashAbilityComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent.Owner, ent.Comp.Action);
    }
}

public sealed partial class DashRequestedEvent : InstantActionEvent
{
}