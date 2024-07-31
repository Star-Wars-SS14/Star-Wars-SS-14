using Content.Server.Echo.GameTicking.Rules.Components;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.GridPreloader;
using Content.Server.Maps;
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
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly GridPreloaderSystem _gridPreloader = default!;

    protected override void Added(EntityUid uid,
        SectorWarRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleAddedEvent args)
    {
        foreach (var map in component.Maps)
        {
            // grid preloading needs map to init after moving it
            var mapUid = _map.CreateMap(out var mapId, runMapInit: true);

            Log.Info($"Created map {mapId} for {ToPrettyString(uid):rule}");

            IReadOnlyList<EntityUid> grids;
            var gameMap = _prototypeManager.Index(map);

            GameTicker.LoadGameMap(gameMap, mapId, new MapLoadOptions());

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

            _map.InitializeMap(mapUid);
            
            var ev = new RuleLoadedGridsEvent(mapId, grids);
            RaiseLocalEvent(uid, ref ev);
        }
    }
}
