using Content.Server.GameTicking.Rules;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Robust.Shared.Map.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Gamerules.RadioactiveStorm;
public sealed class SWRadioactiveStormGameruleSystem : GameRuleSystem<SWRadioactiveStormGameruleComponent>
{
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly SharedRoofSystem _roofSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    public void StartStorm()
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var _, out var comp, out var _))
        {
            comp.InProgress = true;
        }
    }

    protected override void ActiveTick(EntityUid uid, SWRadioactiveStormGameruleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (!component.InProgress)
        {
            return;
        }

        var query = EntityQueryEnumerator<DamageableComponent>();
        while (query.MoveNext(out var entity, out var comp))
        {
            _transformSystem.TryGetGridTilePosition(entity, out var tilePosition);
            var grid = _transformSystem.GetGrid(entity);
            if (grid == null)
            {
                continue;
            }

            if (!TryComp<RoofComponent>(grid, out var roofComp))
            {
                continue;
            }

            if (!TryComp<MapGridComponent>(grid, out var gridComp))
            {
                continue;
            }

            var ent = new Entity<MapGridComponent, RoofComponent>(grid.Value, gridComp, roofComp);

            if (!_roofSystem.IsRooved(ent, tilePosition))
            {
                _damageableSystem.TryChangeDamage(entity, component.Damage);
            }
        }
    }
}
