using System.Linq;
using Content.Server.Explosion.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;
using System.Numerics;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Server.Audio;
using Content.Shared.Coordinates;
using Robust.Shared.Audio;
using Content.Shared.Trigger;
namespace Content.Server._SW.Artillery;
public sealed class ArtillerySystem : EntitySystem
{
    [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly IRobustRandom _randomSystem = default!;
    [Dependency] private readonly IGameTiming _time = default!;
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArtilleryOnTriggerComponent, TriggerEvent>(HandleTriggerArtillery);
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<ArtilleryOnTriggerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_time.CurTime >= comp.NextTime && _time.CurTime > comp.TimeStartBombing && comp.TimesLeft > 0)
            {
                if (comp.PlayingBombSoundInfo != null && _audioSystem.IsPlaying(comp.PlayingBombSoundInfo.Value.Entity))
                {
                    _audioSystem.Stop(comp.PlayingBombSoundInfo.Value.Entity, comp.PlayingBombSoundInfo.Value.Component);
                }
                comp.TimesLeft--;
                for (int i = 0; i < comp.CountPerTime; i++)
                {
                    EntityCoordinates? coords = SelectRandomTileInRange(uid, Transform(uid), comp.TilesRange);
                    if (coords == null)
                    {
                        continue;
                    }
                    //вместо создания взрывов спавним прототип с instant effect-ом взрыва.
                    //Это обеспечивает больше гибкости, так как можно изменить снаряды для артилерии просто изменив прототип instant effect-а.
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
        if (ent.Comp.BombSound != null)
        {
            ent.Comp.PlayingBombSoundInfo = _audioSystem.PlayPvs(new SoundPathSpecifier(ent.Comp.BombSound), ent.Owner.ToCoordinates());
        }
        ent.Comp.TimesLeft = ent.Comp.TimesCount;
        ent.Comp.TimeStartBombing = _time.CurTime + TimeSpan.FromSeconds(ent.Comp.DelayInSeconds);
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
        List<TileRef> tilesNotEmpty = new();
        foreach (TileRef tile in tilesInRange)
        {
            if (!tile.Tile.IsEmpty)
            {
                tilesNotEmpty.Add(tile);
            }
        }

        EntityCoordinates? targetCoords = null;
        int index = _randomSystem.Next(tilesNotEmpty.Count());
        TileRef selectedTile = tilesNotEmpty.ElementAt(index);
        targetCoords = new EntityCoordinates(gridUid,
                    _mapSystem.TileCenterToVector(gridUid, grid, selectedTile.GridIndices));
        return targetCoords;
    }
}
