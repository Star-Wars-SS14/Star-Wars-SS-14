using Content.Server.Chat.Managers;
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
    [Dependency] private readonly IChatManager _chatManager = default!;

    public void TriggerStorm(EntityUid caller)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var _, out var comp, out var _))
        {
            if (comp.IsTriggered)
            {
                continue;
            }

            comp.IsTriggered = true;
            comp.AffectedMaps.Add(Transform(caller).MapID);
            comp.TimeOfStart = _timing.CurTime + comp.DelayBetweenTriggerAndStarting;
            comp.TimeOfEnd = comp.TimeOfStart + comp.Duration;
            _chatManager.DispatchServerAnnouncement(Loc.GetString(comp.StartAnnouncementId), colorOverride: comp.Color);
        }
    }
    public void StartStorm(SWRadioactiveStormGameruleComponent component)
    {
        if (component.InProgress)
        {
            return;
        }

        component.InProgress = true;
        var proto = _prototypeManager.Index(component.WeatherId);
        foreach (var map in component.AffectedMaps)
        {
            _sharedWeatherSystem.SetWeather(map, proto, _timing.CurTime + component.Duration);
        }
    }

    protected override void ActiveTick(EntityUid uid, SWRadioactiveStormGameruleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.TimeOfStart != null && _timing.CurTime >= component.TimeOfStart && !component.InProgress)
        {
            StartStorm(component);
        }

        if (component.TimeOfEnd != null && _timing.CurTime >= component.TimeOfEnd && component.InProgress)
        {
            EndStorm(component);
        }

        if (!component.InProgress)
        {
            return;
        }

        if (_timing.CurTime <= component.NextTimeDamageApplied)
        {
            return;
        }

        var query = EntityQueryEnumerator<DamageableComponent, MobStateComponent>();

        while (query.MoveNext(out var entity, out var _, out var _))
        {
            _transformSystem.TryGetGridTilePosition(entity, out var tilePosition);
            var grid = _transformSystem.GetGrid(entity);

            if (grid == null)
            {
                continue;
            }

            if (!TryComp<MapGridComponent>(grid, out var gridComp))
            {
                continue;
            }

            if (!component.AffectedMaps.Contains(Transform(entity).MapID))
            {
                continue;
            }

            if (!TryComp<RoofComponent>(grid, out var roofComp))
            {
                _damageableSystem.TryChangeDamage(entity, component.Damage);
                continue;
            }

            var ent = new Entity<MapGridComponent, RoofComponent>(grid.Value, gridComp, roofComp);
            if (!_roofSystem.IsRooved(ent, tilePosition) && _timing.CurTime >= component.NextTimeDamageApplied)
            {
                _damageableSystem.TryChangeDamage(entity, component.Damage);
            }
        }
        component.NextTimeDamageApplied = _timing.CurTime + component.Cooldown;
    }

    public void EndStorm(SWRadioactiveStormGameruleComponent component)
    {
        if (!component.InProgress)
        {
            return;
        }
        _chatManager.DispatchServerAnnouncement(Loc.GetString(component.EndAnnouncementId), colorOverride: component.Color);

        component.InProgress = false;
        component.IsTriggered = false;
        component.TimeOfStart = null;
        component.TimeOfEnd = null;
        foreach (var map in component.AffectedMaps)
        {
            _sharedWeatherSystem.SetWeather(map, null, null);
        }
    }
}
