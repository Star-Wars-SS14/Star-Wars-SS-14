using Content.Shared._SW.TaskSystem;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.KillRandomOfFaction;

public sealed partial class SWKillRandomOfFactionSystem : EntitySystem
{
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWKillRandomOfFactionComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirements);
        SubscribeLocalEvent<SWKillRandomOfFactionComponent, SWOnTaskInitEvent>(OnTaskInit);
        SubscribeLocalEvent<DamageableComponent, MobStateChangedEvent>(OnMobStateChanged);
        _s = Logger.GetSawmill("killrandom");
    }

    private void OnCheckRequirements(Entity<SWKillRandomOfFactionComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        var query = EntityQueryEnumerator<SWKillRandomOfFactionComponent>();
        List<EntityUid?> targets = new();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.PersonToKill != null)
            {
                targets.Add(comp.PersonToKill);
            }
        }

        var query2 = EntityQueryEnumerator<SWFactionMemberComponent>();
        while (query2.MoveNext(out var uid, out var comp))
        {
            if (comp.Faction == ent.Comp.Faction && !targets.Contains(uid))
            {
                return;
            }
        }
        _s.Debug("cancelled");
        args.Cancel();
    }

    private void OnTaskInit(Entity<SWKillRandomOfFactionComponent> ent, ref SWOnTaskInitEvent args)
    {
        _s.Debug("initted");
        var query = EntityQueryEnumerator<SWFactionMemberComponent>();
        while(query.MoveNext(out var uid, out var comp))
        {
            if (comp.Faction == ent.Comp.Faction)
            {
                _s.Debug($"given {uid}");
                ent.Comp.PersonToKill = uid;
                break;
            }
        }
    }

    private void OnMobStateChanged(Entity<DamageableComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            var query = EntityQueryEnumerator<SWKillRandomOfFactionComponent>();

            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.PersonToKill == ent.Owner)
                {
                    _s.Debug("Killed");
                    RaiseLocalEvent(uid, new SWTaskProgressUpdateEvent(uid, 1.0));
                }
            }
        }
    }
}
