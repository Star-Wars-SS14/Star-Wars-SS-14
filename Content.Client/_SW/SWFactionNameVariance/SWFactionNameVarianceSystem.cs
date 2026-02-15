using Content.Shared._SW.TaskSystem;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Robust.Client.Player;
using Robust.Shared.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Shared._SW.SWFactionNameVariance;

namespace Content.Client._SW.SWFactionNameVariance;
public sealed class SWFactionNameVarianceSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _metaSystem = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    
    public override void Initialize()

    {
        base.Initialize();
        //SubscribeLocalEvent<SWFactionNameVarianceComponent, MapInitEvent>(OnInit);
    }

    //private void OnInit(Entity<SWFactionNameVarianceComponent> ent, ref MapInitEvent args)
    //{
    //    ISawmill _s = Logger.GetSawmill("asffds");
    //    _s.Fatal("0");
    //    if (_player.LocalEntity == null) {
    //        return;
    //    }
    //    var player = _player.LocalEntity.Value;
    //    if (!TryComp<SWFactionMemberComponent>(player, out var comp))
    //    {
    //        return;
    //    }
    //    _s.Fatal("1");
    //    var faction = comp.Faction;
    //    _s.Fatal($"{faction}");
    //    if (ent.Comp.FactionsToName.TryGetValue(faction, out var locId))
    //    {
    //        var entity = ent.Owner;
    //        _metaSystem.SetEntityName(entity, "aboba");
    //        _s.Fatal($"renmamed {entity}");
    //    }
    //}

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MetaDataComponent, SWFactionNameVarianceComponent>();
        while (query.MoveNext(out var uid, out var meta, out var comp))
        {
            if (_player.LocalEntity == null)
            {
                continue;
            }
            var player = _player.LocalEntity.Value;
            if (!TryComp<SWFactionMemberComponent>(player, out var factComp))
            {
                continue;
            }
            var faction = factComp.Faction;
            if (comp.FactionsToName.TryGetValue(faction, out var locId))
            {
                string newName = Loc.GetString(locId);
                if (meta.EntityName != newName)
                {
                    _metaSystem.SetEntityName(uid, newName);
                }
            }
        }
    }
}
