using Content.Server._SW.Gamerules.Respawn;
using Content.Server._SW.Gamerules.TimerSpeedup;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Whitelist;
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
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly SWTimerSpeedupSystem _timerSpeedup = default!;

    /// <summary>
    /// Half the diagonal of a tile, so the check covers the whole tile the rock would fill.
    /// </summary>
    private const float MobClearance = 0.71f;

    protected override void Started(EntityUid uid, SWRespawnEntitiesGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        component.NextSpawn = _gameTiming.CurTime + _timerSpeedup.Scale(component.Delay);
        component.NextWarning = component.NextSpawn - _timerSpeedup.Scale(component.DelayBeforeSpawn);
        component.WarningDispatched = false;
        component.InitialSpawnDone = false;
    }

    protected override void ActiveTick(EntityUid uid, SWRespawnEntitiesGameruleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        // The first fill stocks the mines for the round. Nothing collapsed yet, so nothing is announced.
        if (!component.InitialSpawnDone)
        {
            component.InitialSpawnDone = true;
            Respawn();
            return;
        }

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
        Respawn();
        var delay = _timerSpeedup.Scale(component.Delay);
        component.NextSpawn = _gameTiming.CurTime + delay;
        component.NextWarning = _gameTiming.CurTime + delay - _timerSpeedup.Scale(component.DelayBeforeSpawn);
        component.WarningDispatched = false;
    }

    private void Respawn()
    {
        var query = EntityQueryEnumerator<SWRespawnGameruleSpawnpointComponent>();
        while (query.MoveNext(out var owner, out var comp))
        {
            var coordinates = _sharedTransformSystem.GetMapCoordinates(owner);

            // A rock spawned on a mob traps it inside the wall.
            if (_entityLookupSystem.GetEntitiesInRange<MobStateComponent>(coordinates, MobClearance).Count > 0)
                continue;

            if (_entityLookupSystem.GetEntitiesIntersecting(owner).All(x => _whitelistSystem.IsWhitelistFail(comp.Blacklist, x)))
            {
                var res = Spawn(comp.RespawnProto, coordinates);
            }
        }
    }

    //private bool IsTheSameAlreadySpawned(EntityUid ent, SWRespawnGameruleSpawnpointComponent comp)
    //{
    //    var proto = Prototype(ent);
    //    if (proto == null)
    //    {
    //        return false;
    //    }
    //    return proto.ID == comp.RespawnProto;
    //}
}
