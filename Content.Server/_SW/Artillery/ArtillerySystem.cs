using System.Linq;
using Content.Server.Explosion.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using System.Numerics;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
namespace Content.Server._SW.Artillery;
public sealed class ArtillerySystem : EntitySystem
{
    [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly IRobustRandom _randomSystem = default!;
    [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
    [Dependency] private readonly IGameTiming _time = default!;
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
                // Выполняем действия любые тут                
                comp.TimesLeft--;
                _s.Debug($"left: {comp.TimesLeft}");
                for (int i = 0; i < comp.ExplosionsCount; i++)
                {
                    MapCoordinates? coords = SelectRandomTileInRange(uid, Transform(uid), comp.TileRadius);
                    if (coords == null)
                    {
                        continue;
                    }
                    _s.Debug(coords.Value.ToString());
                    _explosionSystem.QueueExplosion
                        (coords.Value,
                        comp.ExplosionType,
                        comp.TotalIntensity,
                        comp.IntensitySlope,
                        comp.MaxIntensity);
                    _s.Debug("queued");
                }
                // Устанавливаем следующий момент выполнения
                comp.NextTime = _time.CurTime + TimeSpan.FromSeconds(comp.DelayBetweenTimes);
            } else if (comp.TimesLeft == 0){
                QueueDel(uid);
            }
        }
    }

    private void HandleTriggerArtillery(Entity<ArtilleryOnTriggerComponent> ent, ref TriggerEvent args)
    {
        _s = Logger.GetSawmill("artilery");
        _s.Debug($"uid: {ent.Owner}");
        ent.Comp.Enabled = true;
        ent.Comp.TimesLeft = ent.Comp.TimesCount;
    }

    private MapCoordinates? SelectRandomTileInRange(EntityUid uid, TransformComponent userXform, int radius)
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
        IEnumerable<TileRef> tilesInRange = _mapSystem.GetLocalTilesIntersecting(uid, grid, shape, false);

        if (tilesInRange.Count() == 0)
        {
            return null;
        }
        EntityCoordinates? targetCoords = null;


        int index = _randomSystem.Next(tilesInRange.Count());
        TileRef selectedTile = tilesInRange.ElementAt(index);
        targetCoords = new EntityCoordinates(gridUid,
                    _mapSystem.TileCenterToVector(gridUid, grid, selectedTile.GridIndices));
       
        MapCoordinates result = _sharedTransformSystem.ToMapCoordinates(targetCoords.Value);
        return result;
    }
}
