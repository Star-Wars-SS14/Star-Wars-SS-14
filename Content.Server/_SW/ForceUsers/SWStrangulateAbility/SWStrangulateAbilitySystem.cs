using Content.Shared.Actions;
using Content.Shared.Mobs.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Components;
using Content.Shared._SW;
using Content.Server._SW.SWTriggerOnSay;
using Content.Shared.Mobs;
using Content.Server.Popups;
using Content.Server._SW.SWDottyAccent;
using Content.Server.Examine;
using Content.Shared.Tag;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared._SW.ForceAbilities.SWStrangulatуAbility;
using Content.Shared._SW.ForceAbilities.SWStrangulateAbility;

namespace Content.Server._SW.SWStrangulateAbility;

public sealed partial class SWStrangulateAbilitySystem : EntitySystem
{
    [Dependency] SharedActionsSystem _actions = default!;
    [Dependency] SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] PopupSystem _popup = default!;
    [Dependency] private readonly ExamineSystem _examineSystem = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWStrangulateAbilityComponent, SWRequestStrangulateEvent>(OnRequest);
        SubscribeLocalEvent<SWStrangulateAbilityComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<SWBeingStrangulatedComponent, SWCheckIfCanBreatheEvent>(OnCheck);
    }

    private void OnCheck(Entity<SWBeingStrangulatedComponent> ent, ref SWCheckIfCanBreatheEvent args)
    {
        args.CanBreathe = false;
        args.Handled = true;
    }
    private void OnCompInit(Entity<SWStrangulateAbilityComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent.Owner, ent.Comp.Action);
    }

    private void OnRequest(Entity<SWStrangulateAbilityComponent> ent, ref SWRequestStrangulateEvent args)
    {
        var oldTarget = ent.Comp.CurrentTarget;
        if (ent.Comp.CurrentTarget != null)
        {
            StopStrangulating(ent, ent.Comp.CurrentTarget.Value);
            args.Handled = true;
        }
        if (args.Target != oldTarget
            && CheckIfAbleToStrangulate(ent, args.Target)
            && StartStrangulating(ent, args.Target))
        {
            args.Handled = true;
        }
    }

    private bool StartStrangulating(Entity<SWStrangulateAbilityComponent> ent, EntityUid target)
    {
        //check if target is an alive mob
        if (!TryComp<MobStateComponent>(target, out var comp) ||
            comp.CurrentState != MobState.Alive)
        {
            return false;
        }

        if (_tag.HasTag(target, "ForceUser"))
        {
            return false;
        }

        ent.Comp.CurrentTarget = target;
        AddComp<SWBeingStrangulatedComponent>(target);
        if (TryComp<PhysicsComponent>(target, out var physComp))
        {
            _physicsSystem.SetBodyStatus(target, physComp, BodyStatus.InAir);
        }
        AddComp<SWTriggerOnSayComponent>(target);
        var emitComp = AddComp<EmitSoundOnTriggerComponent>(target);
        emitComp.Sound = ent.Comp.GaspSound;
        var message = Loc.GetString(ent.Comp.StartStrangulateMessage, ("name", Name(target)));
        _popup.PopupEntity(message, target);
        AddComp<SWDottyAccentComponent>(target);
        return true;
    }

    private void StopStrangulating(Entity<SWStrangulateAbilityComponent> ent, EntityUid target)
    {
        if (target != ent.Comp.CurrentTarget)
        {
            var _s = Logger.GetSawmill("Strngulate");
            _s.Error("Trying to stop strngulating of wrong target");
            return;
        }
        ent.Comp.CurrentTarget = null;
        RemComp<SWBeingStrangulatedComponent>(target);
        if (TryComp<PhysicsComponent>(target, out var physComp))
        {
            _physicsSystem.SetBodyStatus(target, physComp, BodyStatus.OnGround);
        }
        RemComp<EmitSoundOnTriggerComponent>(target);
        RemComp<SWTriggerOnSayComponent>(target);
        var message = Loc.GetString(ent.Comp.StopStrangulateMessage, ("name", Name(target)));
        _popup.PopupEntity(message, target);
        RemComp<SWDottyAccentComponent>(target);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWStrangulateAbilityComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.CurrentTarget != null)
            {
                var ent = new Entity<SWStrangulateAbilityComponent>(uid, comp);
                if (!CheckIfAbleToStrangulate(ent, comp.CurrentTarget.Value))
                {
                    StopStrangulating(ent, comp.CurrentTarget.Value);
                }
            }
        }
    }

    private bool CheckIfAbleToStrangulate(Entity<SWStrangulateAbilityComponent> ent, EntityUid target)
    {
        {
            if (TryComp<MobStateComponent>(target, out var comp)
                && comp.CurrentState != MobState.Alive)
            {
                return false;
            }
        }

        {
            if (TryComp<MobStateComponent>(ent.Owner, out var comp)
                && comp.CurrentState != MobState.Alive)
            {
                return false;
            }
        }

        if (!_examineSystem.InRangeUnOccluded(ent.Owner, target, ent.Comp.Radius))
        {
            return false;
        }

        return true;
    }
}



