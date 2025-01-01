using System.Linq;
using Content.Server.Explosion.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using System.Numerics;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Server.Audio;
using Content.Shared.Coordinates;
namespace Content.Server._SW.Artillery;
public sealed class ArtillerySystem : EntitySystem
{
    [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly IRobustRandom _randomSystem = default!;
    [Dependency] private readonly IGameTiming _time = default!;
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArtilleryOnTriggerComponent, TriggerEvent>(HandleTriggerArtillery);
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ArtilleryOnTriggerComponent>(); // чекаем все сущности с указанным компонентом AutoSpeakPostingChatComponent если он у них есть, мы к ним можем обратиться 
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_time.CurTime >= comp.NextTime && comp.Enabled && comp.TimesLeft > 0)
            {
                comp.TimesLeft--;
                for (int i = 0; i < comp.CountPerTime; i++)
                {
                    EntityCoordinates? coords = SelectRandomTileInRange(uid, Transform(uid), comp.TileRadius);
                    if (coords == null)
                    {
                        continue;
                    }
                    Spawn(comp.ProtoId, coords.Value);
                }
                comp.NextTime = _time.CurTime + TimeSpan.FromSeconds(comp.DelayBetweenTimes);
            }
            else if (comp.TimesLeft == 0)
            {
                QueueDel(uid);
            }
        }
    }

    private void HandleTriggerArtillery(Entity<ArtilleryOnTriggerComponent> ent, ref TriggerEvent args)
    {
        _s = Logger.GetSawmill("art");
        _s.Debug("1");
        var playedInfo = _audioSystem.PlayPvs(ent.Comp.BombSound, ent.Owner.ToCoordinates());
        _s.Debug("2");
        Timer.Spawn(TimeSpan.FromSeconds(ent.Comp.DelayInSeconds), () =>
        {
            ent.Comp.Enabled = true;
            ent.Comp.TimesLeft = ent.Comp.TimesCount;
            if (playedInfo != null)
            {
                _audioSystem.Stop(playedInfo.Value.Entity);
            }
        });
        _s.Debug("3");
    }

    private EntityCoordinates? SelectRandomTileInRange(EntityUid uid, TransformComponent userXform, int radius)
    {
        if (userXform.GridUid == null)
        {
            return null;
        }
        EntityUid gridUid = userXform.GridUid.Value;
        if (!TryComp<MapGridComponent>(userXform.GridUid, out var grid))
            return null;
        Vector2 tmp = new Vector2(radius, radius);
        var shape = new Box2(userXform.LocalPosition - tmp, userXform.LocalPosition + tmp);
        List<TileRef> tilesInRange = _mapSystem.GetLocalTilesIntersecting(uid, grid, shape, false).ToList();

        if (tilesInRange.Count() == 0)
        {
            return null;
        }
        foreach (TileRef tile in tilesInRange)
        {
            if (tile.Tile.IsEmpty)
            {
                tilesInRange.Remove(tile);
            }
        }

        EntityCoordinates? targetCoords = null;
        int index = _randomSystem.Next(tilesInRange.Count());
        TileRef selectedTile = tilesInRange.ElementAt(index);
        targetCoords = new EntityCoordinates(gridUid,
                    _mapSystem.TileCenterToVector(gridUid, grid, selectedTile.GridIndices));
        return targetCoords;
    }
}
