using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared.GameTicking;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar;

/// <summary>Sends the owners of every world to the players who have the galaxy map open, when they change.</summary>
public sealed class SWGlobalWarGalaxySystem : EntitySystem
{
    private const float CheckSeconds = 1;

    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;

    private readonly HashSet<ICommonSession> _viewers = new();
    private string _lastKey = string.Empty;
    private float _untilCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWGlobalWarGalaxyRequestEvent>(OnRequest);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _viewers.Clear());
    }

    private void OnRequest(SWGlobalWarGalaxyRequestEvent args, EntitySessionEventArgs session)
    {
        if (!args.Open)
        {
            _viewers.Remove(session.SenderSession);
            return;
        }

        _viewers.Add(session.SenderSession);
        RaiseNetworkEvent(new SWGlobalWarGalaxyStateEvent(Snapshot()), session.SenderSession);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_viewers.Count == 0)
            return;

        _untilCheck -= frameTime;
        if (_untilCheck > 0)
            return;

        _untilCheck = CheckSeconds;
        _viewers.RemoveWhere(viewer => viewer.Status is SessionStatus.Disconnected or SessionStatus.Zombie);
        var worlds = Snapshot();
        var key = string.Join(',', worlds.Select(world => $"{world.Planet.Id}:{(int) world.Orbit}{(int) world.Surface}"));
        if (key == _lastKey)
            return;

        _lastKey = key;
        foreach (var viewer in _viewers)
            RaiseNetworkEvent(new SWGlobalWarGalaxyStateEvent(worlds), viewer);
    }

    private List<SWGlobalWarGalaxyWorld> Snapshot()
    {
        var worlds = new List<SWGlobalWarGalaxyWorld>();
        if (!_rules.TryGetGlobalWarGamerule(out var rule))
            return worlds;

        foreach (var (id, uid) in rule.Comp.PlanetEntities)
        {
            if (TryComp<SWGlobalWarPlanetComponent>(uid, out var planet))
                worlds.Add(new SWGlobalWarGalaxyWorld(id, planet.OrbitOwner, planet.SurfaceOwner));
        }

        return worlds;
    }
}
