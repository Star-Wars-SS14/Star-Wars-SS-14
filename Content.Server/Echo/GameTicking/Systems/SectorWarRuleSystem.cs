using Content.Server.Echo.GameTicking.Rules.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.GridPreloader;
using Content.Server.Maps;
using Content.Shared.Echo.GameTicking.Components;
using Content.Shared.GameTicking.Components;
using Robust.Server.GameObjects;
using Robust.Server.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server.Echo.GameTicking.Systems;

public sealed class SectorWarRuleSystem : GameRuleSystem<SectorWarRuleComponent>
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

    protected override void Added(EntityUid uid,
        SectorWarRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleAddedEvent args)
    {
        var maps = new List<GameMapPrototype>();
        foreach (var map in component.Maps)
        {
            var gameMap = _prototypeManager.Index(map);
            maps.Add(gameMap);
            var mapUid = _map.CreateMap(out var mapId, false);
            GameTicker.LoadGameMap(gameMap, mapId, new MapLoadOptions{LoadMap = true, DoMapInit = true});
            //_map.InitializeMap(mapId);
            //_gameTicker.LoadGameMap(_prototype.Index<GameMapPrototype>(map.Id), mapId, null);

            Log.Info($"Created map {mapId} for {ToPrettyString(uid):rule}");
            /*
            if (gameMap.MapPath is var path)
            {
                var options = new MapLoadOptions { LoadMap = true };
                if (!_mapLoader.TryLoad(mapId, path.ToString(), out var roots, options))
                {
                    Log.Error($"Failed to load map from {path}!");
                    Del(mapUid);
                    ForceEndSelf(uid, gameRule);
                    return;
                }

                grids = roots;
            }
            */
            //_map.SetPaused(mapId, false);
            EnsureComp<CapturableStationComponent>(_map.GetMap(mapId));
            var ev = new LoadingMapsEvent(maps);
            RaiseLocalEvent(uid, ev);
        }
    }
}
