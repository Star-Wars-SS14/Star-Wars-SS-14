using Content.Server.Actions;
using Content.Server.Stunnable;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Tag;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Shared._SW;

namespace Content.Server._SW;

public sealed class SWPushAbilitySystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actionsSystem = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly StunSystem _stun = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWPushAbilityComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<SWPushAbilityComponent, ForceTryPushAbilityEvent>(OnTryPush);
    }

    private void OnTryPush(Entity<SWPushAbilityComponent> ent, ref ForceTryPushAbilityEvent args)
    {
        if (_tag.HasTag(args.Target, "ForceUser"))
        {
            return;
        }

        _stun.TryParalyze(args.Target, TimeSpan.FromSeconds(ent.Comp.KnockedDownDuration), false);
        args.Handled = true;
    }

    private void OnCompInit(Entity<SWPushAbilityComponent> ent, ref ComponentInit args)
    {
        _actionsSystem.AddAction(ent.Owner, ent.Comp.ActionPrototype);
    }
}


