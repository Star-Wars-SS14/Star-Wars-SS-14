using Content.Shared.Explosion.EntitySystems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Chat.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Trigger.Systems;
using Content.Shared.Chat;

namespace Content.Server._SW.SWTriggerOnSay;

public sealed class SWTriggerOnSaySystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _triggerSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTriggerOnSayComponent, EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(Entity<SWTriggerOnSayComponent> ent, ref EntitySpokeEvent args)
    {
        _triggerSystem.Trigger(ent.Owner);
    }
}
