using Content.Server.GameTicking.Rules;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Weather;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
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
    [Dependency] private readonly SharedWeatherSystem _sharedWeatherSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    public void StartStorm(EntityUid caller)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var _, out var comp, out var _))
        {
            if (comp.InProgress)
            {
                continue;
            }

            comp.InProgress = true;
            var proto = _prototypeManager.Index(comp.WeatherId);
            //_sharedWeatherSystem.SetWeather(Transform(caller).MapID, proto, _timing.CurTime + comp.Duration);
        }
    }

    protected override void ActiveTick(EntityUid uid, SWRadioactiveStormGameruleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        var _s = Logger.GetSawmill("sadfg");
        _s.Fatal("0");
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (!component.InProgress)
        {
            return;
        }

        if (_timing.CurTime <= component.NextTimeDamageApplied)
        {
            return;
        }

        _s.Fatal("1");
        var query = EntityQueryEnumerator<DamageableComponent, MobStateComponent>();
        int count = 0;
        while (query.MoveNext(out var entity, out var _, out var comp))
        {
            count++;
            _transformSystem.TryGetGridTilePosition(entity, out var tilePosition);
            var grid = _transformSystem.GetGrid(entity);
            //_s.Fatal($"{entity} 0");
            if (grid == null)
            {
                continue;
            }
            //_s.Fatal($"{entity} 1");

            if (!TryComp<MapGridComponent>(grid, out var gridComp))
            {
                continue;
            }
            //_s.Fatal($"{entity} 2");
            if (!TryComp<RoofComponent>(grid, out var roofComp))
            {
                continue;
            }
            //_s.Fatal($"{entity} 3");
            var ent = new Entity<MapGridComponent, RoofComponent>(grid.Value, gridComp, roofComp);

            //_s.Fatal($"{entity} pos: {tilePosition.X} {tilePosition.Y}");
            //_s.Fatal($"{component.Damage.GetTotal()}");
            if (/*!_roofSystem.IsRooved(ent, tilePosition) &&*/ _timing.CurTime >= component.NextTimeDamageApplied)
            {
                //_s.Fatal($"applied to {entity}");
                _damageableSystem.TryChangeDamage(entity, component.Damage);
            }
        }
        _s.Fatal($"count {count}");
        component.NextTimeDamageApplied = _timing.CurTime + component.Cooldown;
    }
}
