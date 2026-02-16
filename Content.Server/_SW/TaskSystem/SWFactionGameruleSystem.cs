using Content.Server.GameTicking.Rules;
using Content.Shared._SW.TaskSystem;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Random.Helpers;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem;

public sealed class SWFactionGameruleSystem : GameRuleSystem<SWFactionGameruleComponent>
{
    public override void Initialize()
    {
        base.Initialize();
    }

    protected override void Added(EntityUid uid, SWFactionGameruleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);
        foreach (string proto in component.FactionsPrototypes)
        {
            EntityUid factionUid = Spawn(proto);
            SWFactionComponent comp = EnsureComp<SWFactionComponent>(factionUid);
            component.FactionProtoToEntityDictionary[proto] = new(factionUid, comp);
            comp.CurrentScore = comp.StartScore;
        }
    }

    public Dictionary<string, Entity<SWFactionComponent>?> GetFactions()
    {
        var query = EntityQueryEnumerator<SWFactionGameruleComponent>();
        query.MoveNext(out var _, out var comp);
        if (comp != null)
        {
            return comp.FactionProtoToEntityDictionary;
        }
        return new Dictionary<string, Entity<SWFactionComponent>?>();
    }

    public List<Entity<SWFactionComponent>> GetFactionsEntities()
    {
        var dict = GetFactions();
        List<Entity<SWFactionComponent>> res = new();
        foreach (var faction in dict)
        {
            if (faction.Value != null) //Отсеиваем фракции, которые не были заспавнены по какой-то причине
            {
                res.Add(faction.Value.Value);
            }
        }
        return res;
    }

    public Entity<SWFactionComponent>? GetFactionEntity(string protoid)
    {
        Dictionary<string, Entity<SWFactionComponent>?> dict = GetFactions();

        if (dict.TryGetValue(protoid, out var ent))
        {
            return ent;
        }
        return null;
    }
}
