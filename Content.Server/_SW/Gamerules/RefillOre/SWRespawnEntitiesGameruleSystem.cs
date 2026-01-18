using Content.Server._SW.Gamerules.Respawn;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Gamerules.RefillOre;
public sealed class SWRespawnEntitiesGameruleSystem : GameRuleSystem<SWRespawnEntitiesGameruleComponent>
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;

    protected override void Started(EntityUid uid, SWRespawnEntitiesGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        component.NextSpawn = _gameTiming.CurTime + component.Delay;
        component.NextWarning = _gameTiming.CurTime + component.Delay - component.DelayBeforeSpawn;
        component.WarningDispatched = false;
    }
    protected override void ActiveTick(EntityUid uid, SWRespawnEntitiesGameruleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.NextWarning >= _gameTiming.CurTime)
        {
            return;
        }

        if (component.NextWarning < _gameTiming.CurTime && !component.WarningDispatched)
        {
            _chatManager.DispatchServerAnnouncement(Loc.GetString(component.AnnouncementWarningId),
                                                              component.AnnounceColor);
            component.WarningDispatched = true;
            return;
        }

        if (component.NextSpawn >= _gameTiming.CurTime)
        {
            return;
        }

        _chatManager.DispatchServerAnnouncement(Loc.GetString(component.AnnouncementId),
                                                              component.AnnounceColor);
        var query = EntityQueryEnumerator<SWRespawnGameruleSpawnpointComponent>();
        while (query.MoveNext(out var owner, out var comp))
        {
            if (!_entityLookupSystem.GetEntitiesIntersecting(owner).Any(x => IsTheSameAlreadySpawned(x, comp)))
            {
                Spawn(comp.RespawnProto, _sharedTransformSystem.GetMapCoordinates(owner));
            }
        }
        component.NextSpawn = _gameTiming.CurTime + component.Delay;
        component.NextWarning = _gameTiming.CurTime + component.Delay - component.DelayBeforeSpawn;
        component.WarningDispatched = false;
    }

    private bool IsTheSameAlreadySpawned(EntityUid ent, SWRespawnGameruleSpawnpointComponent comp)
    {
        var proto = Prototype(ent);
        if (proto == null)
        {
            return false;
        }
        return proto.ID == comp.RespawnProto;
    }
}
